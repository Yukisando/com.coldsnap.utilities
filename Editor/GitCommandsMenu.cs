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
                ExecuteGitCommand("add .");
                ExecuteGitCommand($"commit -m \"{commitMessage}\"");
                var result = ExecuteGitCommand("push");

                if (result.ExitCode != 0) {
                    Debug.LogError($"Push failed:\n{result.Error}");
                    EditorUtility.DisplayDialog("Push Failed", result.Error, "OK");
                }
                else {
                    Debug.Log("Push succeeded.");
                    Close();
                }
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