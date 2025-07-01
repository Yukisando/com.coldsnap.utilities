#region

using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

#endregion

public class GitCommandsMenu : EditorWindow
{
    string commitMessage = "";

    [MenuItem("ColdSnap/Git/Quick Commit %#s")] // Ctrl+Shift+S
    public static void ShowWindow() {
        var window = GetWindow<GitCommandsMenu>("Git Commit");
        window.minSize = new Vector2(400, 150);
        window.maxSize = new Vector2(400, 150);
        window.Show();
    }

    void OnEnable() {
        // Automatically focus the commit message text area when the window opens
        EditorApplication.delayCall += () => EditorGUI.FocusTextInControl("CommitMessageTextArea");
    }

    void OnGUI() {
        GUILayout.Space(10);
        GUILayout.Label("Commit Message", EditorStyles.boldLabel);

        // Assign a control name to the text area for focusing
        GUI.SetNextControlName("CommitMessageTextArea");
        commitMessage = EditorGUILayout.TextArea(commitMessage, GUILayout.Height(60));

        GUILayout.Space(10);
        GUILayout.BeginHorizontal();

        // Disable the button if the commit message is empty or whitespace
        bool canCommit = !string.IsNullOrWhiteSpace(commitMessage);
        GUI.enabled = canCommit;
        if (GUILayout.Button("Commit + Push", GUILayout.Height(30)) || (canCommit && Event.current.isKey && Event.current.keyCode == KeyCode.Return)) {
            if (string.IsNullOrWhiteSpace(commitMessage)) {
                EditorUtility.DisplayDialog("Error", "Commit message cannot be empty.", "OK");
            } else {
                Close(); // Close the window immediately after pressing the button
                // Run git commands in background
                System.Threading.Tasks.Task.Run(() => {
                    var addResult = ExecuteGitCommand("add .");
                    if (addResult.ExitCode != 0) {
                        EditorApplication.delayCall += () => {
                            EditorUtility.DisplayDialog("Git Error", $"git add failed.\nError: {addResult.Error}", "OK");
                        };
                        return;
                    }
                    var commitResult = ExecuteGitCommand($"commit -m \"{commitMessage}\"");
                    if (commitResult.ExitCode != 0) {
                        EditorApplication.delayCall += () => {
                            EditorUtility.DisplayDialog("Git Error", $"git commit failed.\nError: {commitResult.Error}", "OK");
                        };
                        return;
                    }
                    var pushResult = ExecuteGitCommand("push");
                    if (pushResult.ExitCode != 0) {
                        EditorApplication.delayCall += () => {
                            EditorUtility.DisplayDialog("Git Error", $"git push failed.\nError: {pushResult.Error}", "OK");
                        };
                        return;
                    }
                    // Log success
                    EditorApplication.delayCall += () => {
                        Debug.Log("Git commit and push succeeded.");
                    };
                });
            }
        }
        GUI.enabled = true;

        // Ensure GUI layout groups are properly closed
        try {
            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f); // light red tint
            if (GUILayout.Button("Discard All Changes", GUILayout.Height(30))) {
                GUI.backgroundColor = Color.white;
                if (EditorUtility.DisplayDialog("Discard All Changes",
                        "Are you sure you want to discard ALL uncommitted changes?",
                        "Yes", "No")) {
                    ExecuteGitCommand("reset --hard");
                    ExecuteGitCommand("clean -fd");
                    Close();
                }
            }
        } finally {
            // Always reset GUI state to avoid layout errors
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;
        }

        GUILayout.EndHorizontal();
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

        process.Start();
        process.WaitForExit();

        return new GitResult {
            ExitCode = process.ExitCode,
        };
    }

    static void ExecuteGitCommandAsync(string arguments) {
        string projectPath = Application.dataPath.Substring(0, Application.dataPath.Length - "/Assets".Length);
        var process = new Process();
        process.StartInfo.FileName = "git";
        process.StartInfo.Arguments = arguments;
        process.StartInfo.WorkingDirectory = projectPath;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.UseShellExecute = true;
        process.Start();
    }
}