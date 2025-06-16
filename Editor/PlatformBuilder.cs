#region

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

#endregion

public class PlatformBuilder : EditorWindow
{
    enum BuildPlatform
    {
        Windows,
        MacOS,
        Android,
        WebGL,
    }

    BuildPlatform selectedPlatform = BuildPlatform.Windows;
    bool isDebugBuild;
    bool autoRunPlayer;
    bool buildAllScenes;
    string appName = "";

    #region Menu Items

    [MenuItem("ColdSnap/Build/Build Game &#B", priority = 0)]
    public static void ShowBuildWindow() {
        var window = GetWindow<PlatformBuilder>(true, "Build Game", true);
        window.minSize = new Vector2(350, 280);
        window.maxSize = new Vector2(350, 280);
        window.LoadPreferences();
        window.ShowUtility();
    }

    [MenuItem("ColdSnap/Build/Open Build Folder", priority = 100)]
    public static void OpenBuildFolderMenu() {
        OpenBuildLocation("./Builds/");
    }

    #endregion

    #region Editor Window GUI

    void OnGUI() {
        GUILayout.Label("Build Settings", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);

        selectedPlatform = (BuildPlatform)EditorGUILayout.EnumPopup("Platform:", selectedPlatform);

        EditorGUILayout.HelpBox("Avoid spaces in the app name for best results.", MessageType.Info);
        string appNameLabel = buildAllScenes ? "App Name (Project):" : "App Name (Scene):";
        if (!buildAllScenes) {
            if (appName == GetDefaultAppName()) appName = SceneManager.GetActiveScene().name;
        }
        else {
            if (appName == SceneManager.GetActiveScene().name) appName = GetDefaultAppName();
        }
        appName = EditorGUILayout.TextField(appNameLabel, appName);

        bool previousBuildAllScenes = buildAllScenes;
        buildAllScenes = EditorGUILayout.Toggle("Build All Scenes", buildAllScenes);
        if (previousBuildAllScenes != buildAllScenes) {
            if (buildAllScenes)
                appName = GetDefaultAppName();
            else
                appName = SceneManager.GetActiveScene().name;
        }
        isDebugBuild = EditorGUILayout.Toggle("Debug Build", isDebugBuild);
        autoRunPlayer = EditorGUILayout.Toggle("Auto Start Game", autoRunPlayer);

        EditorGUILayout.Space(15);

        GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
        if (GUILayout.Button("Build Game", GUILayout.Height(30))) {
            if (string.IsNullOrEmpty(appName))
                EditorUtility.DisplayDialog("Error", "App Name cannot be empty.", "OK");
            else {
                TriggerBuild();
                Close();
            }
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(5);

        if (GUILayout.Button("Cancel")) Close();
    }

    #endregion

    #region Build Logic

    void TriggerBuild() {
        var target = GetBuildTarget(selectedPlatform);
        string extension = GetExtension(selectedPlatform);

        var options = BuildOptions.None;
        if (isDebugBuild) options |= BuildOptions.Development;
        if (autoRunPlayer) options |= BuildOptions.AutoRunPlayer;

        SavePreferences();

        BuildGame(target, options, buildAllScenes, appName, extension);
    }

    static void BuildGame(BuildTarget target, BuildOptions options, bool buildAll, string name, string extension) {
        // Force lowercase product name for WebGL to avoid case issues with generated files
        PlayerSettings.productName = target == BuildTarget.WebGL ? name.ToLowerInvariant() : name;

        if (target == BuildTarget.Android)
            PlayerSettings.Android.bundleVersionCode++;
        else if (target == BuildTarget.StandaloneOSX)
            EditorUserBuildSettings.SetPlatformSettings("Standalone", "OSXUniversal", "Architecture", "x64ARM64");
        else if (target == BuildTarget.WebGL)
            options &= ~BuildOptions.AutoRunPlayer;

        string platformName = GetFriendlyPlatformName(target);
        string buildPath = Path.GetFullPath($"./Builds/{platformName}/");
        string appFolder = Path.Combine(buildPath, name);
        Directory.CreateDirectory(appFolder);
        string outputPath = target == BuildTarget.WebGL ? appFolder : Path.Combine(appFolder, name + extension);
        string[] scenes = buildAll
            ? GetEnabledScenes()
            : new[] {
                SceneManager.GetActiveScene().path,
            };

        if (scenes.Length == 0) {
            Debug.LogError("No scenes found to build.");
            return;
        }

        Debug.Log($"Starting build for {name} on {platformName} with scenes: {string.Join(", ", scenes)}");

        var report = BuildPipeline.BuildPlayer(scenes, outputPath, target, options);

        if (report.summary.result == BuildResult.Succeeded) {
            Debug.Log($"Build successful! Output: {outputPath}");
            OpenBuildLocation(buildPath);
        } else
            Debug.LogError($"Build failed: {report.summary.result}");
    }


    #endregion

    #region Helper Functions

    static void OpenBuildLocation(string path) {
        string normalizedPath = Path.GetFullPath(path);
        if (Directory.Exists(normalizedPath))
            Process.Start(new ProcessStartInfo {
                FileName = normalizedPath,
                UseShellExecute = true,
                Verb = "open",
            });
        else
            Debug.LogWarning($"Build folder not found at: {normalizedPath}");
    }

    static string[] GetEnabledScenes() {
        return EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
    }

    static BuildTarget GetBuildTarget(BuildPlatform platform) {
        switch (platform) {
            case BuildPlatform.Windows: return BuildTarget.StandaloneWindows64;
            case BuildPlatform.MacOS: return BuildTarget.StandaloneOSX;
            case BuildPlatform.Android: return BuildTarget.Android;
            case BuildPlatform.WebGL: return BuildTarget.WebGL;
            default: throw new ArgumentOutOfRangeException(nameof(platform), platform, null);
        }
    }

    static string GetExtension(BuildPlatform platform) {
        switch (platform) {
            case BuildPlatform.Windows: return ".exe";
            case BuildPlatform.MacOS: return ".app";
            case BuildPlatform.Android: return ".apk";
            case BuildPlatform.WebGL: return "";
            default: return "";
        }
    }

    static string GetFriendlyPlatformName(BuildTarget target) {
        switch (target) {
            case BuildTarget.StandaloneWindows:
            case BuildTarget.StandaloneWindows64:
                return "Windows";
            case BuildTarget.StandaloneOSX:
                return "MacOS";
            case BuildTarget.Android:
                return "Android";
            case BuildTarget.WebGL:
                return "WebGL";
            default:
                return target.ToString();
        }
    }

    void SavePreferences() {
        EditorPrefs.SetInt("PlatformBuilder_SelectedPlatform", (int)selectedPlatform);
        EditorPrefs.SetBool("PlatformBuilder_IsDebugBuild", isDebugBuild);
        EditorPrefs.SetBool("PlatformBuilder_AutoRunPlayer", autoRunPlayer);
        EditorPrefs.SetBool("PlatformBuilder_BuildAllScenes", buildAllScenes);
        EditorPrefs.SetString("PlatformBuilder_AppName", appName);
    }

    void LoadPreferences() {
        selectedPlatform = (BuildPlatform)EditorPrefs.GetInt("PlatformBuilder_SelectedPlatform", (int)BuildPlatform.Windows);
        isDebugBuild = EditorPrefs.GetBool("PlatformBuilder_IsDebugBuild", false);
        autoRunPlayer = EditorPrefs.GetBool("PlatformBuilder_AutoRunPlayer", false);
        buildAllScenes = EditorPrefs.GetBool("PlatformBuilder_BuildAllScenes", false);
        appName = EditorPrefs.GetString("PlatformBuilder_AppName", GetDefaultAppName());
        if (!buildAllScenes) appName = SceneManager.GetActiveScene().name;
    }

    static string GetDefaultAppName() {
        return Directory.GetParent(Application.dataPath).Name;
    }

    #endregion
}