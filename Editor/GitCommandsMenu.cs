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

    void OnGUI() {
        GUILayout.Space(10);
        GUILayout.Label("Commit Message", EditorStyles.boldLabel);
        commitMessage = EditorGUILayout.TextArea(commitMessage, GUILayout.Height(60));

        GUILayout.Space(10);
        GUILayout.BeginHorizontal();

        if (GUILayout.Button("Commit + Push", GUILayout.Height(30))) {
            if (string.IsNullOrWhiteSpace(commitMessage))
                EditorUtility.DisplayDialog("Error", "Commit message cannot be empty.", "OK");
            else {
                var addResult = ExecuteGitCommand("add .");
                if (addResult.ExitCode != 0) {
                    EditorUtility.DisplayDialog("Git Error", $"git add failed.\nError: {addResult.Error}", "OK");
                    return;
                }
                var commitResult = ExecuteGitCommand($"commit -m \"{commitMessage}\"");
                if (commitResult.ExitCode != 0) {
                    EditorUtility.DisplayDialog("Git Error", $"git commit failed.\nError: {commitResult.Error}", "OK");
                    return;
                }
                var pushResult = ExecuteGitCommand("push");
                if (pushResult.ExitCode != 0) {
                    EditorUtility.DisplayDialog("Git Error", $"git push failed.\nError: {pushResult.Error}", "OK");
                    return;
                }
                EditorUtility.DisplayDialog("Success", "Git commit and push succeeded.", "OK");
                Close();
            }
        }

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
        GUI.backgroundColor = Color.white;

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
        process.StartInfo.UseShellExecute = true;

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
        // Do not wait for exit; run in background
    }
}