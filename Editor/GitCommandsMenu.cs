#region

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
// These scripts compile into Assembly-CSharp-Editor, so a game type named `Environment`
// in the global namespace would otherwise shadow System.Environment.
using Environment = System.Environment;
using PlatformID = System.PlatformID;

#endregion

public class GitCommandsMenu : EditorWindow
{
    const string TimeoutPrefKey = "ColdSnap.Git.TimeoutSeconds";
    const int DefaultTimeoutSeconds = 120;
    const int MaxLogLinesInMemory = 4000;
    const int MaxLogCharsInGui = 14000;

    // Marker replaced at run time with `commit --file=<temp file>` so the message survives quotes and newlines.
    const string CommitCommandPlaceholder = "\0commit\0";

    // Cached on the main thread: Unity APIs (Application.dataPath) are not safe to touch from a worker thread.
    static string projectPath;
    static string logFilePath;
    static readonly object LogFileLock = new object();

    static readonly Queue<Action> MainThreadQueue = new Queue<Action>();

    string commitMessage = "";
    string branchName = "";
    string statusSummary = "";
    string lastMessage = "";
    bool lastMessageIsError;
    bool showMoreOptions;
    bool showLog = true;
    bool isBusy;
    bool isRefreshing;
    bool shouldCloseOnNextGUI;
    Vector2 scroll;
    Vector2 logScroll;

    // Live progress for whatever sequence is currently running.
    string busyLabel = "";
    string busyStep = "";
    int busyStepIndex;
    int busyStepCount;
    double busyStartTime;
    double lastBusyRepaint;

    readonly List<string> logLines = new List<string>();
    readonly object logLock = new object();
    int logVersion;
    int cachedLogVersion = -1;
    string cachedLogText = "";

    readonly object processLock = new object();
    Process runningProcess;
    bool cancelRequested;

    string indexLockPath;

    [MenuItem("ColdSnap/Git/Quick Commit %#s")] // Ctrl+Shift+S
    public static void ShowWindow() {
        var window = GetWindow<GitCommandsMenu>("Git");
        window.minSize = new Vector2(420, 420);
        window.Show();
    }

    [InitializeOnLoadMethod]
    static void HookMainThreadPump() {
        EditorApplication.update -= PumpMainThreadQueue;
        EditorApplication.update += PumpMainThreadQueue;
    }

    static void PumpMainThreadQueue() {
        while (true) {
            Action action;
            lock (MainThreadQueue) {
                if (MainThreadQueue.Count == 0) return;
                action = MainThreadQueue.Dequeue();
            }

            try {
                action();
            } catch (Exception e) {
                Debug.LogException(e);
            }
        }
    }

    static void RunOnMainThread(Action action) {
        lock (MainThreadQueue) {
            MainThreadQueue.Enqueue(action);
        }
    }

    // Same, but skipped if the window was closed while the command was still running.
    void RunOnMainThreadIfAlive(Action action) {
        RunOnMainThread(() => {
            if (this == null) return;
            action();
        });
    }

    void OnEnable() {
        CachePaths();
        Log($"--- Git window opened ({DateTime.Now:yyyy-MM-dd HH:mm:ss}) ---");
        EditorApplication.update += OnEditorUpdate;
        RefreshStatus();
        // Automatically focus the commit message text area when the window opens
        EditorApplication.delayCall += () => EditorGUI.FocusTextInControl("CommitMessageTextArea");
    }

    void OnDisable() {
        EditorApplication.update -= OnEditorUpdate;
        if (isBusy) {
            Log("!! Git window closed while a command was still running. The command keeps going; see this log file for the outcome.");
        }
    }

    static void CachePaths() {
        if (!string.IsNullOrEmpty(projectPath)) return;
        string dataPath = Application.dataPath;
        projectPath = dataPath.Substring(0, dataPath.Length - "/Assets".Length);
        logFilePath = Path.Combine(projectPath, "Logs", "ColdSnapGit.log");
    }

    void OnEditorUpdate() {
        // Keep the elapsed timer and the streaming log alive without repainting every frame.
        if (!isBusy) return;
        if (EditorApplication.timeSinceStartup - lastBusyRepaint < 0.25) return;
        lastBusyRepaint = EditorApplication.timeSinceStartup;
        Repaint();
    }

    void OnGUI() {
        DrawHeader();

        using (new EditorGUI.DisabledScope(isBusy)) {
            GUILayout.Space(6);
            DrawCommitSection();
            GUILayout.Space(6);
            DrawActionButtons();
            GUILayout.Space(6);
            DrawMoreOptions();
        }

        GUILayout.Space(6);
        DrawBusyBar();
        DrawStatusBar();
        DrawLog();

        if (shouldCloseOnNextGUI) {
            shouldCloseOnNextGUI = false;
            Close();
        }
    }

    void DrawHeader() {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox)) {
            GUILayout.Label(string.IsNullOrEmpty(branchName) ? "Branch: (unknown)" : $"Branch: {branchName}", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(isBusy || isRefreshing)) {
                if (GUILayout.Button("↻", GUILayout.Width(26), GUILayout.Height(18))) {
                    RefreshStatus();
                }
            }
        }

        if (!string.IsNullOrEmpty(statusSummary)) {
            GUILayout.Label(statusSummary, EditorStyles.miniLabel);
        }

        if (!string.IsNullOrEmpty(indexLockPath)) {
            EditorGUILayout.HelpBox("A stale .git/index.lock exists. Git will refuse to stage or commit until it is removed.", MessageType.Warning);
            if (GUILayout.Button("Delete index.lock", GUILayout.Height(20))) {
                DeleteIndexLock();
            }
        }
    }

    void DrawCommitSection() {
        GUILayout.Label("Commit Message", EditorStyles.boldLabel);
        GUI.SetNextControlName("CommitMessageTextArea");
        commitMessage = EditorGUILayout.TextArea(commitMessage, GUILayout.Height(70));

        bool canCommit = !string.IsNullOrWhiteSpace(commitMessage);

        // The shortcut is handled outside the button, so it has to repeat the button's guards itself.
        // Without the isBusy check, Ctrl+Enter would start a second `git add` while the first still holds index.lock.
        bool submitViaKey = canCommit && !isBusy
                            && Event.current.type == EventType.KeyDown
                            && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                            && (Event.current.control || Event.current.command);
        if (submitViaKey) Event.current.Use();

        GUI.backgroundColor = new Color(0.5f, 0.85f, 0.55f);
        using (new EditorGUI.DisabledScope(!canCommit)) {
            if (GUILayout.Button("Commit All + Push", GUILayout.Height(40)) || submitViaKey) {
                CommitAllAndPush();
            }
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.LabelField("Ctrl+Enter to commit + push", EditorStyles.miniLabel);
    }

    void DrawActionButtons() {
        using (new EditorGUILayout.HorizontalScope()) {
            if (GUILayout.Button("Pull", GUILayout.Height(28))) {
                RunGitSequence("Pull", new[] { "pull" });
            }

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(commitMessage))) {
                if (GUILayout.Button("Commit All", GUILayout.Height(28))) {
                    RunGitSequence("Commit", new[] { "add .", CommitCommandPlaceholder }, clearMessageOnSuccess: true);
                }
            }

            if (GUILayout.Button("Push", GUILayout.Height(28))) {
                RunGitSequence("Push", new[] { "push" });
            }
        }
    }

    void DrawMoreOptions() {
        showMoreOptions = EditorGUILayout.Foldout(showMoreOptions, "More Options", true);
        if (!showMoreOptions) return;

        using (new EditorGUI.IndentLevelScope()) {
            using (new EditorGUILayout.HorizontalScope()) {
                if (GUILayout.Button("Fetch", GUILayout.Height(24))) {
                    RunGitSequence("Fetch", new[] { "fetch --all --prune" });
                }
                if (GUILayout.Button("Stage All", GUILayout.Height(24))) {
                    RunGitSequence("Stage All", new[] { "add ." });
                }
            }

            using (new EditorGUILayout.HorizontalScope()) {
                if (GUILayout.Button("Unstage All", GUILayout.Height(24))) {
                    RunGitSequence("Unstage All", new[] { "reset" });
                }
                if (GUILayout.Button("Commit + Push", GUILayout.Height(24))) {
                    CommitAllAndPush();
                }
            }

            GUILayout.Space(4);
            int timeout = EditorGUILayout.IntSlider("Command timeout (s)", TimeoutSeconds, 10, 600);
            if (timeout != TimeoutSeconds) TimeoutSeconds = timeout;

            GUILayout.Space(4);
            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f); // light red tint
            if (GUILayout.Button("Discard All Changes", GUILayout.Height(24))) {
                GUI.backgroundColor = Color.white;
                if (EditorUtility.DisplayDialog("Discard All Changes",
                        "Are you sure you want to discard ALL uncommitted changes? This cannot be undone.",
                        "Yes, Discard", "Cancel")) {
                    RunGitSequence("Discard", new[] { "reset --hard", "clean -fd" });
                }
            }
            GUI.backgroundColor = Color.white;
        }
    }

    void DrawBusyBar() {
        if (!isBusy) return;

        double elapsed = EditorApplication.timeSinceStartup - busyStartTime;
        string step = string.IsNullOrEmpty(busyStep) ? "" : $"\ngit {busyStep}";
        EditorGUILayout.HelpBox(
            $"{busyLabel} — step {busyStepIndex}/{busyStepCount}, {elapsed:0.0}s elapsed (timeout {TimeoutSeconds}s){step}",
            MessageType.Info);

        GUI.backgroundColor = new Color(1f, 0.6f, 0.4f);
        using (new EditorGUI.DisabledScope(cancelRequested)) {
            if (GUILayout.Button(cancelRequested ? "Cancelling…" : "Cancel (kill git process)", GUILayout.Height(24))) {
                CancelRunning();
            }
        }
        GUI.backgroundColor = Color.white;
    }

    void DrawStatusBar() {
        if (string.IsNullOrEmpty(lastMessage)) return;

        using (var scrollScope = new EditorGUILayout.ScrollViewScope(scroll, GUILayout.MaxHeight(80))) {
            scroll = scrollScope.scrollPosition;
            EditorGUILayout.HelpBox(lastMessage, lastMessageIsError ? MessageType.Error : MessageType.Info);
        }
    }

    void DrawLog() {
        using (new EditorGUILayout.HorizontalScope()) {
            showLog = EditorGUILayout.Foldout(showLog, "Log", true);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Copy", EditorStyles.miniButtonLeft, GUILayout.Width(46))) {
                EditorGUIUtility.systemCopyBuffer = GetLogText();
            }
            if (GUILayout.Button("Log File", EditorStyles.miniButtonMid, GUILayout.Width(60))) {
                OpenLogFile();
            }
            if (GUILayout.Button("Clear", EditorStyles.miniButtonRight, GUILayout.Width(46))) {
                lock (logLock) {
                    logLines.Clear();
                    logVersion++;
                }
            }
        }

        if (!showLog) return;

        using (var scrollScope = new EditorGUILayout.ScrollViewScope(logScroll, GUILayout.MinHeight(120))) {
            logScroll = scrollScope.scrollPosition;
            EditorGUILayout.SelectableLabel(GetLogDisplayText(), EditorStyles.textArea,
                GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true));
        }

        EditorGUILayout.LabelField($"Log file: {logFilePath}", EditorStyles.miniLabel);
    }

    void CommitAllAndPush() {
        if (string.IsNullOrWhiteSpace(commitMessage)) {
            SetMessage("Commit message cannot be empty.", true);
            return;
        }
        RunGitSequence("Commit + Push",
            new[] { "add .", CommitCommandPlaceholder, "push" },
            clearMessageOnSuccess: true,
            closeOnSuccess: true);
    }

    void RunGitSequence(string label, string[] commands, bool clearMessageOnSuccess = false, bool closeOnSuccess = false) {
        if (isBusy) {
            Log($"!! Ignored '{label}': another git command is still running.");
            return;
        }

        CachePaths();

        string messageFile = null;
        try {
            for (int i = 0; i < commands.Length; i++) {
                if (commands[i] != CommitCommandPlaceholder) continue;
                messageFile = Path.Combine(Path.GetTempPath(), $"coldsnap-commit-{Guid.NewGuid():N}.txt");
                File.WriteAllText(messageFile, commitMessage.Replace("\r\n", "\n").Trim() + "\n", new UTF8Encoding(false));
                commands[i] = $"commit --file=\"{messageFile}\"";
            }
        } catch (Exception e) {
            Log($"!! Could not write the commit message file: {e.Message}");
            SetMessage($"{label} failed: could not write the commit message file.\n{e.Message}", true);
            return;
        }

        isBusy = true;
        cancelRequested = false;
        busyLabel = label;
        busyStep = "";
        busyStepIndex = 0;
        busyStepCount = commands.Length;
        busyStartTime = EditorApplication.timeSinceStartup;
        SetMessage($"{label}…", false);
        Log($"=== {label} started ===");
        Repaint();

        int timeoutSeconds = TimeoutSeconds;
        string messageFileToDelete = messageFile;

        Task.Run(() => {
            GitResult failed = null;
            var combined = new StringBuilder();

            try {
                for (int i = 0; i < commands.Length; i++) {
                    string cmd = commands[i];
                    int stepNumber = i + 1;
                    RunOnMainThreadIfAlive(() => {
                        busyStepIndex = stepNumber;
                        busyStep = cmd;
                        Repaint();
                    });

                    var result = ExecuteGitCommand(cmd, timeoutSeconds);
                    combined.AppendLine($"$ git {cmd}");
                    combined.AppendLine(Truncate((result.Output + result.Error).Trim(), 1500));
                    if (result.ExitCode != 0) {
                        failed = result;
                        break;
                    }
                }
            } catch (Exception e) {
                // Without this the task would die silently and the window would sit on "Working…" forever.
                failed = new GitResult { Command = "(internal)", ExitCode = -1, Error = e.ToString() };
                Log($"!! Unhandled exception during '{label}': {e}");
            }

            if (messageFileToDelete != null) {
                try { File.Delete(messageFileToDelete); } catch { /* temp file, not worth reporting */ }
            }

            RunOnMainThreadIfAlive(() => {
                isBusy = false;
                busyStep = "";
                cancelRequested = false;

                if (failed != null) {
                    string err = string.IsNullOrWhiteSpace(failed.Error) ? failed.Output : failed.Error;
                    string text = $"{label} failed (git {failed.Command}, exit {failed.ExitCode}):\n{(err ?? "").Trim()}";
                    SetMessage(text, true);
                    Log($"=== {label} FAILED ===");
                    Debug.LogWarning($"[Git] {text}");
                } else {
                    if (clearMessageOnSuccess) commitMessage = "";
                    SetMessage($"{label} succeeded.\n{combined.ToString().Trim()}", false);
                    Log($"=== {label} succeeded ===");
                    Debug.Log($"[Git] {label} succeeded.");
                    if (closeOnSuccess) {
                        // Defer closing until after the current OnGUI finishes
                        shouldCloseOnNextGUI = true;
                    }
                }

                RefreshStatus();
                Repaint();
            });
        });
    }

    void CancelRunning() {
        cancelRequested = true;
        Log("!! Cancel requested by user.");

        Process process;
        lock (processLock) {
            process = runningProcess;
        }

        if (process == null) {
            Log("   No live process to kill (the command may already be finishing).");
            return;
        }

        KillProcessTree(process, "cancelled by user");
    }

    void KillProcessTree(Process process, string reason) {
        try {
            int pid = process.Id;
            Log($"   Killing git process {pid} ({reason}).");

            // Environment, not Application.platform: this can run on the worker thread when a command times out.
            if (Environment.OSVersion.Platform == PlatformID.Win32NT) {
                // git spawns helpers (credential manager, ssh); killing only the parent can leave them holding locks.
                try {
                    using (var killer = new Process()) {
                        killer.StartInfo.FileName = "taskkill";
                        killer.StartInfo.Arguments = $"/PID {pid} /T /F";
                        killer.StartInfo.CreateNoWindow = true;
                        killer.StartInfo.UseShellExecute = false;
                        killer.Start();
                        killer.WaitForExit(5000);
                    }
                } catch (Exception e) {
                    Log($"   taskkill failed: {e.Message}");
                }
            }

            if (!process.HasExited) process.Kill();
        } catch (Exception e) {
            Log($"   Could not kill the git process: {e.Message}");
        }
    }

    void RefreshStatus() {
        if (isRefreshing) return;
        CachePaths();
        isRefreshing = true;

        int timeoutSeconds = Mathf.Min(TimeoutSeconds, 30);

        Task.Run(() => {
            string branch = "(unknown)";
            string summary = "";
            string lockPath = null;

            try {
                var branchResult = ExecuteGitCommand("rev-parse --abbrev-ref HEAD", timeoutSeconds, quiet: true);
                if (branchResult.ExitCode == 0) branch = branchResult.Output.Trim();

                var statusResult = ExecuteGitCommand("status --porcelain", timeoutSeconds, quiet: true);
                if (statusResult.ExitCode == 0) {
                    int changes = string.IsNullOrWhiteSpace(statusResult.Output)
                        ? 0
                        : statusResult.Output.Trim().Split('\n').Length;
                    summary = changes == 0 ? "Working tree clean" : $"{changes} change(s) pending";
                }

                var gitDirResult = ExecuteGitCommand("rev-parse --git-dir", timeoutSeconds, quiet: true);
                if (gitDirResult.ExitCode == 0) {
                    string gitDir = gitDirResult.Output.Trim();
                    if (!Path.IsPathRooted(gitDir)) gitDir = Path.Combine(projectPath, gitDir);
                    string candidate = Path.Combine(gitDir, "index.lock");
                    if (File.Exists(candidate)) lockPath = candidate;
                }
            } catch (Exception e) {
                Log($"!! Status refresh failed: {e.Message}");
            }

            RunOnMainThreadIfAlive(() => {
                isRefreshing = false;
                branchName = branch;
                statusSummary = summary;
                indexLockPath = lockPath;
                Repaint();
            });
        });
    }

    void DeleteIndexLock() {
        if (string.IsNullOrEmpty(indexLockPath)) return;
        if (!EditorUtility.DisplayDialog("Delete index.lock",
                $"Delete\n{indexLockPath}?\n\nOnly do this when no git process is running — removing it while git is working can corrupt the index.",
                "Delete", "Cancel")) {
            return;
        }

        try {
            File.Delete(indexLockPath);
            Log($"Deleted {indexLockPath}");
            SetMessage("index.lock deleted.", false);
        } catch (Exception e) {
            Log($"!! Could not delete index.lock: {e.Message}");
            SetMessage($"Could not delete index.lock:\n{e.Message}", true);
        }

        indexLockPath = null;
        RefreshStatus();
    }

    void SetMessage(string message, bool isError) {
        lastMessage = message;
        lastMessageIsError = isError;
    }

    static int TimeoutSeconds {
        get { return Mathf.Clamp(EditorPrefs.GetInt(TimeoutPrefKey, DefaultTimeoutSeconds), 10, 600); }
        set { EditorPrefs.SetInt(TimeoutPrefKey, Mathf.Clamp(value, 10, 600)); }
    }

    static string Truncate(string text, int max) {
        if (string.IsNullOrEmpty(text) || text.Length <= max) return text;
        return text.Substring(0, max) + $"\n… (+{text.Length - max} more chars, see the log below)";
    }

    #region Logging

    void Log(string line) {
        string stamped = $"[{DateTime.Now:HH:mm:ss.fff}] {line}";

        lock (logLock) {
            logLines.Add(stamped);
            if (logLines.Count > MaxLogLinesInMemory) {
                logLines.RemoveRange(0, logLines.Count - MaxLogLinesInMemory);
            }
            logVersion++;
        }

        AppendToLogFile(stamped);
    }

    // Appended line by line so the log survives a force quit of the editor.
    static void AppendToLogFile(string line) {
        if (string.IsNullOrEmpty(logFilePath)) return;
        try {
            lock (LogFileLock) {
                string dir = Path.GetDirectoryName(logFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(logFilePath, line + Environment.NewLine);
            }
        } catch {
            // Logging must never be the thing that breaks a commit.
        }
    }

    string GetLogText() {
        lock (logLock) {
            if (cachedLogVersion != logVersion) {
                cachedLogText = string.Join("\n", logLines.ToArray());
                cachedLogVersion = logVersion;
            }
            return cachedLogText;
        }
    }

    // IMGUI text controls silently truncate somewhere past 16k characters, so only the tail is drawn.
    // Copy and the log file still carry everything.
    string GetLogDisplayText() {
        string text = GetLogText();
        if (text.Length <= MaxLogCharsInGui) return text;

        int cut = text.Length - MaxLogCharsInGui;
        int newline = text.IndexOf('\n', cut);
        if (newline >= 0) cut = newline + 1;
        return $"… earlier lines trimmed from this view — use Copy or Log File for the full log …\n{text.Substring(cut)}";
    }

    void OpenLogFile() {
        if (string.IsNullOrEmpty(logFilePath)) return;
        if (!File.Exists(logFilePath)) {
            AppendToLogFile($"[{DateTime.Now:HH:mm:ss.fff}] (log file created)");
        }
        EditorUtility.RevealInFinder(logFilePath);
    }

    #endregion

    class GitResult
    {
        public string Command;
        public int ExitCode;
        public string Output;
        public string Error;
    }

    GitResult ExecuteGitCommand(string arguments, int timeoutSeconds, bool quiet = false) {
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var stopwatch = Stopwatch.StartNew();

        if (!quiet) Log($"$ git {arguments}");

        Process process = null;
        try {
            process = new Process();
            var info = process.StartInfo;
            info.FileName = "git";
            info.Arguments = arguments;
            info.WorkingDirectory = projectPath;
            info.CreateNoWindow = true;
            info.UseShellExecute = false;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.RedirectStandardInput = true;
            info.StandardOutputEncoding = new UTF8Encoding(false);
            info.StandardErrorEncoding = new UTF8Encoding(false);
            // Never let git block waiting on a human: with no console attached those prompts hang forever.
            info.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
            info.EnvironmentVariables["GIT_PAGER"] = "cat";
            // Git Credential Manager is deliberately left interactive: its sign-in window is a real dialog the user
            // can answer. It is the timeout below, not this, that stops a missed dialog from hanging forever.
            info.EnvironmentVariables["GIT_ASKPASS"] = "";

            // Output is drained asynchronously. Reading stdout to the end first deadlocks as soon as git fills
            // the stderr pipe buffer, which `add .` does through CRLF warnings and `push` through progress output.
            process.OutputDataReceived += (sender, e) => {
                if (e.Data == null) return;
                lock (stdout) stdout.AppendLine(e.Data);
                if (!quiet) Log($"  | {e.Data}");
            };
            process.ErrorDataReceived += (sender, e) => {
                if (e.Data == null) return;
                lock (stderr) stderr.AppendLine(e.Data);
                if (!quiet) Log($"  ! {e.Data}");
            };

            process.Start();

            if (!quiet) {
                lock (processLock) runningProcess = process;
                Log($"  (pid {process.Id}, timeout {timeoutSeconds}s)");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            // Nothing here feeds git on stdin; closing it turns any prompt into an instant EOF instead of a hang.
            try { process.StandardInput.Close(); } catch { /* already closed */ }

            if (!process.WaitForExit(timeoutSeconds * 1000)) {
                Log($"!! git {arguments} exceeded the {timeoutSeconds}s timeout — killing it.");
                Log("   If this was a pull/push, check for a credential sign-in window hidden behind Unity.");
                KillProcessTree(process, "timed out");
                process.WaitForExit(5000);
                stopwatch.Stop();
                return new GitResult {
                    Command = arguments,
                    ExitCode = -1,
                    Output = stdout.ToString(),
                    Error = $"Timed out after {timeoutSeconds}s and was killed.\n{stderr}",
                };
            }

            // Parameterless call after a successful timed wait: lets the async output handlers drain.
            process.WaitForExit();
            stopwatch.Stop();

            int exitCode = process.ExitCode;
            if (!quiet) Log($"  -> exit {exitCode} in {stopwatch.ElapsedMilliseconds} ms");

            return new GitResult {
                Command = arguments,
                ExitCode = exitCode,
                Output = stdout.ToString(),
                Error = stderr.ToString(),
            };
        } catch (Exception e) {
            stopwatch.Stop();
            Log($"!! git {arguments} threw after {stopwatch.ElapsedMilliseconds} ms: {e.Message}");
            return new GitResult {
                Command = arguments,
                ExitCode = -1,
                Output = stdout.ToString(),
                Error = e.ToString(),
            };
        } finally {
            if (!quiet) {
                lock (processLock) runningProcess = null;
            }
            if (process != null) process.Dispose();
        }
    }
}
