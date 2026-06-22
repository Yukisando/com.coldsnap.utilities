#region

using System.Diagnostics;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

#endregion

public class GitCommandsMenu : EditorWindow
{
    string commitMessage = "";
    string branchName = "";
    string statusSummary = "";
    string lastMessage = "";
    bool lastMessageIsError;
    bool showMoreOptions;
    bool isBusy;
    Vector2 scroll;

    [MenuItem("ColdSnap/Git/Quick Commit %#s")] // Ctrl+Shift+S
    public static void ShowWindow() {
        var window = GetWindow<GitCommandsMenu>("Git");
        window.minSize = new Vector2(360, 280);
        window.Show();
    }

    void OnEnable() {
        RefreshStatus();
        // Automatically focus the commit message text area when the window opens
        EditorApplication.delayCall += () => EditorGUI.FocusTextInControl("CommitMessageTextArea");
    }

    void OnGUI() {
        EditorGUI.BeginDisabledGroup(isBusy);

        DrawHeader();
        GUILayout.Space(6);
        DrawCommitSection();
        GUILayout.Space(6);
        DrawActionButtons();
        GUILayout.Space(6);
        DrawMoreOptions();

        EditorGUI.EndDisabledGroup();

        GUILayout.FlexibleSpace();
        DrawStatusBar();
    }

    void DrawHeader() {
        GUILayout.BeginHorizontal(EditorStyles.helpBox);
        GUILayout.Label(string.IsNullOrEmpty(branchName) ? "Branch: (unknown)" : $"Branch: {branchName}", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("↻", GUILayout.Width(26), GUILayout.Height(18))) {
            RefreshStatus();
        }
        GUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(statusSummary)) {
            GUILayout.Label(statusSummary, EditorStyles.miniLabel);
        }
    }

    void DrawCommitSection() {
        GUILayout.Label("Commit Message", EditorStyles.boldLabel);
        GUI.SetNextControlName("CommitMessageTextArea");
        commitMessage = EditorGUILayout.TextArea(commitMessage, GUILayout.Height(70));

        bool canCommit = !string.IsNullOrWhiteSpace(commitMessage);

        GUI.backgroundColor = new Color(0.5f, 0.85f, 0.55f);
        EditorGUI.BeginDisabledGroup(!canCommit);
        bool submitViaKey = canCommit && Event.current.type == EventType.KeyDown
                            && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                            && (Event.current.control || Event.current.command);
        if (GUILayout.Button("Commit All + Push", GUILayout.Height(40)) || submitViaKey) {
            CommitAllAndPush();
        }
        EditorGUI.EndDisabledGroup();
        GUI.backgroundColor = Color.white;

        EditorGUILayout.LabelField("Ctrl+Enter to commit + push", EditorStyles.miniLabel);
    }

    void DrawActionButtons() {
        GUILayout.BeginHorizontal();

        if (GUILayout.Button("Pull", GUILayout.Height(28))) {
            RunGitSequence("Pull", new[] { "pull" });
        }

        EditorGUI.BeginDisabledGroup(string.IsNullOrWhiteSpace(commitMessage));
        if (GUILayout.Button("Commit All", GUILayout.Height(28))) {
            RunGitSequence("Commit", new[] { "add .", $"commit -m \"{Sanitize(commitMessage)}\"" }, clearMessageOnSuccess: true);
        }
        EditorGUI.EndDisabledGroup();

        if (GUILayout.Button("Push", GUILayout.Height(28))) {
            RunGitSequence("Push", new[] { "push" });
        }

        GUILayout.EndHorizontal();
    }

    void DrawMoreOptions() {
        showMoreOptions = EditorGUILayout.Foldout(showMoreOptions, "More Options", true);
        if (!showMoreOptions) return;

        EditorGUI.indentLevel++;
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Fetch", GUILayout.Height(24))) {
            RunGitSequence("Fetch", new[] { "fetch --all --prune" });
        }
        if (GUILayout.Button("Stage All", GUILayout.Height(24))) {
            RunGitSequence("Stage All", new[] { "add ." });
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Unstage All", GUILayout.Height(24))) {
            RunGitSequence("Unstage All", new[] { "reset" });
        }
        if (GUILayout.Button("Commit + Push", GUILayout.Height(24))) {
            CommitAllAndPush();
        }
        GUILayout.EndHorizontal();

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
        EditorGUI.indentLevel--;
    }

    void DrawStatusBar() {
        if (isBusy) {
            EditorGUILayout.HelpBox("Working…", MessageType.Info);
            return;
        }
        if (string.IsNullOrEmpty(lastMessage)) return;

        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MaxHeight(80));
        EditorGUILayout.HelpBox(lastMessage, lastMessageIsError ? MessageType.Error : MessageType.Info);
        EditorGUILayout.EndScrollView();
    }

    void CommitAllAndPush() {
        if (string.IsNullOrWhiteSpace(commitMessage)) {
            SetMessage("Commit message cannot be empty.", true);
            return;
        }
        RunGitSequence("Commit + Push",
            new[] { "add .", $"commit -m \"{Sanitize(commitMessage)}\"", "push" },
            clearMessageOnSuccess: true);
    }

    void RunGitSequence(string label, string[] commands, bool clearMessageOnSuccess = false) {
        isBusy = true;
        SetMessage($"{label}…", false);
        Repaint();

        Task.Run(() => {
            GitResult failed = null;
            string combinedOutput = "";
            foreach (var cmd in commands) {
                var result = ExecuteGitCommand(cmd);
                combinedOutput += $"$ git {cmd}\n{result.Output}{result.Error}\n";
                if (result.ExitCode != 0) {
                    failed = result;
                    break;
                }
            }

            EditorApplication.delayCall += () => {
                isBusy = false;
                if (failed != null) {
                    string err = string.IsNullOrWhiteSpace(failed.Error) ? failed.Output : failed.Error;
                    SetMessage($"{label} failed:\n{err.Trim()}", true);
                } else {
                    if (clearMessageOnSuccess) commitMessage = "";
                    SetMessage($"{label} succeeded.\n{combinedOutput.Trim()}", false);
                    Debug.Log($"[Git] {label} succeeded.");
                }
                RefreshStatus();
                Repaint();
            };
        });
    }

    void RefreshStatus() {
        var branch = ExecuteGitCommand("rev-parse --abbrev-ref HEAD");
        branchName = branch.ExitCode == 0 ? branch.Output.Trim() : "(unknown)";

        var status = ExecuteGitCommand("status --porcelain");
        if (status.ExitCode == 0) {
            int changes = string.IsNullOrWhiteSpace(status.Output)
                ? 0
                : status.Output.Trim().Split('\n').Length;
            statusSummary = changes == 0 ? "Working tree clean" : $"{changes} change(s) pending";
        } else {
            statusSummary = "";
        }
    }

    void SetMessage(string message, bool isError) {
        lastMessage = message;
        lastMessageIsError = isError;
    }

    static string Sanitize(string message) {
        // Escape double quotes so the commit message survives the shell argument.
        return message.Replace("\"", "\\\"").Replace("\r\n", " ").Replace("\n", " ");
    }

    class GitResult
    {
        public int ExitCode;
        public string Output;
        public string Error;
    }

    static GitResult ExecuteGitCommand(string arguments) {
        string projectPath = Application.dataPath.Substring(0, Application.dataPath.Length - "/Assets".Length);

        var process = new Process();
        process.StartInfo.FileName = "git";
        process.StartInfo.Arguments = arguments;
        process.StartInfo.WorkingDirectory = projectPath;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;

        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new GitResult {
            ExitCode = process.ExitCode,
            Output = output,
            Error = error,
        };
    }
}
