#region

using System;
using System.Collections.Generic;
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
		WebGL
	}

	BuildPlatform selectedPlatform = BuildPlatform.Windows;
	bool isDebugBuild;
	bool autoRunPlayer;
	bool currentSceneOnly = true; // New field - defaults to true
	string appName = "";

	// Scene selection variables - updated to support ordering
	List<SceneInfo> allScenes = new List<SceneInfo>();
	Vector2 sceneScrollPosition;
	
	[System.Serializable]
	public class SceneInfo
	{
		public string path;
		public string name;
		public bool selected;
		
		public SceneInfo(string scenePath, bool isSelected = false)
		{
			path = scenePath;
			name = Path.GetFileNameWithoutExtension(scenePath);
			selected = isSelected;
		}
	}

    #region Menu Items

	[MenuItem("ColdSnap/Build/Build Game &#B", priority = 0)]
	public static void ShowBuildWindow()
	{
		PlatformBuilder window = GetWindow<PlatformBuilder>(true, "Build Game", true);
		window.minSize = new Vector2(450, 450);
		window.maxSize = new Vector2(450, 600);
		window.LoadPreferences();
		window.ShowUtility();
	}

	[MenuItem("ColdSnap/Build/Open Build Folder", priority = 100)]
	public static void OpenBuildFolderMenu()
	{
		OpenBuildLocation("./Builds/");
	}

    #endregion

    #region Editor Window GUI

	void OnGUI()
	{
		GUILayout.Label("Build Settings", EditorStyles.boldLabel);
		EditorGUILayout.Space(5);

		selectedPlatform = (BuildPlatform)EditorGUILayout.EnumPopup("Platform:", selectedPlatform);

		isDebugBuild = EditorGUILayout.Toggle("Debug Build", isDebugBuild);
		autoRunPlayer = EditorGUILayout.Toggle("Auto Start Game", autoRunPlayer);

		EditorGUILayout.Space(10);

		// Current scene toggle
		bool previousCurrentSceneOnly = currentSceneOnly;
		currentSceneOnly = EditorGUILayout.Toggle("Current Scene", currentSceneOnly);

		// If the toggle changed from false to true, we need to update selections
		if (!previousCurrentSceneOnly && currentSceneOnly)
		{
			// When switching to current scene only, select only the active scene
			RefreshSceneList();
			SelectCurrentScene();
		}
		else if (!currentSceneOnly && previousCurrentSceneOnly)
		{
			// Only refresh when switching FROM current scene mode TO scene selection mode
			RefreshSceneList();
			LoadScenePreferences();
		}

		// Update app name based on scene selection
		UpdateAppNameFromSelection();

		EditorGUILayout.HelpBox("App name will be automatically set based on scene selection.", MessageType.Info);

		// Show the calculated app name (read-only for user info)
		EditorGUI.BeginDisabledGroup(true);
		EditorGUILayout.TextField("App Name:", selectedPlatform == BuildPlatform.WebGL ? appName.ToLowerInvariant() : appName);
		EditorGUI.EndDisabledGroup();

		// Only show scene selection UI when not using current scene only
		if (!currentSceneOnly)
		{
			EditorGUILayout.Space(10);

			GUILayout.Label("Scene Selection", EditorStyles.boldLabel);
			EditorGUILayout.BeginVertical("box");

			if (allScenes.Count == 0)
			{
				EditorGUILayout.HelpBox("No scenes found in project.", MessageType.Warning);
			}
			else
			{
				// Add "Select All" and "Select None" buttons
				EditorGUILayout.BeginHorizontal();
				if (GUILayout.Button("Select All", GUILayout.MaxWidth(80)))
				{
					foreach (var scene in allScenes)
					{
						scene.selected = true;
					}
				}
				if (GUILayout.Button("Select None", GUILayout.MaxWidth(80)))
				{
					foreach (var scene in allScenes)
					{
						scene.selected = false;
					}
				}
				GUILayout.FlexibleSpace();
				EditorGUILayout.EndHorizontal();

				EditorGUILayout.Space(5);

				// Scene list with scroll view for better UX
				sceneScrollPosition = EditorGUILayout.BeginScrollView(sceneScrollPosition, GUILayout.MaxHeight(150));

				for (int i = 0; i < allScenes.Count; i++)
				{
					var scene = allScenes[i];
					EditorGUILayout.BeginHorizontal();
					
					// Checkbox for selection
					scene.selected = EditorGUILayout.Toggle(scene.selected, GUILayout.Width(20));
					
					// Scene name
					EditorGUILayout.LabelField(scene.name, GUILayout.ExpandWidth(true));
					
					// Scene path (shortened)
					string scenePath = scene.path.Replace("Assets/", "").Replace(".unity", "");
					EditorGUILayout.LabelField($"({scenePath})", EditorStyles.miniLabel, GUILayout.Width(120));
					
					// Up/Down buttons for ordering (only show for selected scenes)
					GUI.enabled = scene.selected && i > 0 && allScenes[i-1].selected;
					if (GUILayout.Button("▲", GUILayout.Width(25)))
					{
						SwapScenes(i, i - 1);
					}
					
					GUI.enabled = scene.selected && i < allScenes.Count - 1 && allScenes[i+1].selected;
					if (GUILayout.Button("▼", GUILayout.Width(25)))
					{
						SwapScenes(i, i + 1);
					}
					
					GUI.enabled = true;
					EditorGUILayout.EndHorizontal();
				}

				EditorGUILayout.EndScrollView();
			}

			EditorGUILayout.EndVertical();
		}

		EditorGUILayout.Space(15);

		// Validation and build button
		int selectedCount = currentSceneOnly ? 1 : allScenes.Count(s => s.selected);
		if (!currentSceneOnly && selectedCount == 0)
		{
			EditorGUILayout.HelpBox("Please select at least one scene to build.", MessageType.Warning);
		}

		GUI.backgroundColor = selectedCount > 0 ? new Color(0.6f, 1f, 0.6f) : Color.gray;
		EditorGUI.BeginDisabledGroup(selectedCount == 0);
		if (GUILayout.Button("Build Game", GUILayout.Height(30)))
		{
			TriggerBuild();
			Close();
		}
		EditorGUI.EndDisabledGroup();
		GUI.backgroundColor = Color.white;

		EditorGUILayout.Space(5);

		if (GUILayout.Button("Cancel")) Close();
	}

    #endregion

    #region Build Logic

	void TriggerBuild()
	{
		BuildTarget target = GetBuildTarget(selectedPlatform);
		string extension = GetExtension(selectedPlatform);

		BuildOptions options = BuildOptions.None;
		if (isDebugBuild) options |= BuildOptions.Development;
		if (autoRunPlayer) options |= BuildOptions.AutoRunPlayer;

		SavePreferences();

		// Get scenes for building based on current scene mode
		string[] selectedScenePaths;
		if (currentSceneOnly)
		{
			// Use only the current active scene
			selectedScenePaths = new[] { SceneManager.GetActiveScene().path };
		}
		else
		{
			// Use selected scenes from the list
			selectedScenePaths = allScenes
				.Where(scene => scene.selected)
				.Select(scene => scene.path)
				.ToArray();
		}

		// Use lowercase name for WebGL
		string buildName = selectedPlatform == BuildPlatform.WebGL ? appName.ToLowerInvariant() : appName;

		BuildGame(target, options, buildName, extension, selectedScenePaths);
	}

	static void BuildGame(BuildTarget target, BuildOptions options, string name, string extension, string[] scenes)
	{
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

		if (scenes.Length == 0)
		{
			Debug.LogError("No scenes selected to build.");
			return;
		}

		Debug.Log($"Starting build for {name} on {platformName} with scenes: {string.Join(", ", scenes.Select(Path.GetFileNameWithoutExtension))}");

		BuildReport report = BuildPipeline.BuildPlayer(scenes, outputPath, target, options);

		if (report.summary.result == BuildResult.Succeeded)
		{
			Debug.Log($"Build successful! Output: {outputPath}");
			OpenBuildLocation(buildPath);
		}
		else
			Debug.LogError($"Build failed: {report.summary.result}");
	}

    #endregion

    #region Helper Functions

	static void OpenBuildLocation(string path)
	{
		string normalizedPath = Path.GetFullPath(path);
		if (Directory.Exists(normalizedPath))
			Process.Start(new ProcessStartInfo
			{
				FileName = normalizedPath,
				UseShellExecute = true,
				Verb = "open"
			});
		else
			Debug.LogWarning($"Build folder not found at: {normalizedPath}");
	}

	static BuildTarget GetBuildTarget(BuildPlatform platform)
	{
		switch (platform)
		{
			case BuildPlatform.Windows: return BuildTarget.StandaloneWindows64;
			case BuildPlatform.MacOS: return BuildTarget.StandaloneOSX;
			case BuildPlatform.Android: return BuildTarget.Android;
			case BuildPlatform.WebGL: return BuildTarget.WebGL;
			default: throw new ArgumentOutOfRangeException(nameof(platform), platform, null);
		}
	}

	static string GetExtension(BuildPlatform platform)
	{
		switch (platform)
		{
			case BuildPlatform.Windows: return ".exe";
			case BuildPlatform.MacOS: return ".app";
			case BuildPlatform.Android: return ".apk";
			case BuildPlatform.WebGL: return "";
			default: return "";
		}
	}

	static string GetFriendlyPlatformName(BuildTarget target)
	{
		switch (target)
		{
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

	void SavePreferences()
	{
		EditorPrefs.SetInt("PlatformBuilder_SelectedPlatform", (int)selectedPlatform);
		EditorPrefs.SetBool("PlatformBuilder_IsDebugBuild", isDebugBuild);
		EditorPrefs.SetBool("PlatformBuilder_AutoRunPlayer", autoRunPlayer);
		EditorPrefs.SetBool("PlatformBuilder_CurrentSceneOnly", currentSceneOnly);
		EditorPrefs.SetString("PlatformBuilder_AppName", appName);
		
		// Save scene selection and ordering
		if (!currentSceneOnly && allScenes.Count > 0)
		{
			var sceneData = new List<string>();
			foreach (var scene in allScenes)
			{
				sceneData.Add($"{scene.path}|{scene.selected}");
			}
			EditorPrefs.SetString("PlatformBuilder_SceneData", string.Join(";", sceneData));
		}
	}

	void LoadPreferences()
	{
		selectedPlatform = (BuildPlatform)EditorPrefs.GetInt("PlatformBuilder_SelectedPlatform", (int)BuildPlatform.Windows);
		isDebugBuild = EditorPrefs.GetBool("PlatformBuilder_IsDebugBuild", false);
		autoRunPlayer = EditorPrefs.GetBool("PlatformBuilder_AutoRunPlayer", false);
		currentSceneOnly = EditorPrefs.GetBool("PlatformBuilder_CurrentSceneOnly", true);
		appName = EditorPrefs.GetString("PlatformBuilder_AppName", GetDefaultAppName());
		
		// Load scene list and preferences
		if (!currentSceneOnly)
		{
			RefreshSceneList();
			LoadScenePreferences();
		}
	}

	static string GetDefaultAppName()
	{
		return Directory.GetParent(Application.dataPath).Name;
	}

	void RefreshSceneList()
	{
		// Find all .scene files in the project directory, excluding package and plugin scenes
		string[] guids = AssetDatabase.FindAssets("t:Scene");
		allScenes = guids
			.Select(AssetDatabase.GUIDToAssetPath)
			.Where(path =>
				!path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase) && // Exclude package scenes
				!path.Contains("/Plugins/", StringComparison.OrdinalIgnoreCase)) // Exclude plugin scenes
			.OrderBy(path => path)
			.Select(path => new SceneInfo(path)) // Create SceneInfo objects
			.ToList();

		// Initialize or resize the selectedScenes array
		/*if (selectedScenes.Length != allScenes.Length)
		{
			bool[] newSelectedScenes = new bool[allScenes.Length];
			for (int i = 0; i < Math.Min(selectedScenes.Length, newSelectedScenes.Length); i++)
			{
				newSelectedScenes[i] = selectedScenes[i];
			}
			selectedScenes = newSelectedScenes;
		}*/
	}

	void UpdateAppNameFromSelection()
	{
		if (currentSceneOnly)
		{
			// When using current scene only, use the active scene's name
			appName = Path.GetFileNameWithoutExtension(SceneManager.GetActiveScene().name);
		}
		else
		{
			// When using scene selection, use the existing logic
			if (allScenes.Count == 0) return;

			var selectedScenes = allScenes.Where(scene => scene.selected).ToList();

			if (selectedScenes.Count == 1)
			{
				// If one scene is selected, use its name
				appName = Path.GetFileNameWithoutExtension(selectedScenes[0].path);
			}
			else if (selectedScenes.Count > 1)
			{
				// If multiple scenes are selected, use the project name
				appName = GetDefaultAppName();
			}
			else
			{
				// No scenes selected, use project name as fallback
				appName = GetDefaultAppName();
			}
		}
	}

	void SelectCurrentScene()
	{
		// Clear all selections first
		foreach (var scene in allScenes)
		{
			scene.selected = false;
		}

		// Select the currently active scene
		string activeScenePath = SceneManager.GetActiveScene().path;
		var activeScene = allScenes.FirstOrDefault(scene => scene.path == activeScenePath);
		if (activeScene != null)
		{
			activeScene.selected = true;
		}
	}

	void SwapScenes(int indexA, int indexB)
	{
		if (indexA < 0 || indexA >= allScenes.Count || indexB < 0 || indexB >= allScenes.Count)
			return;

		// Swap the scenes
		var temp = allScenes[indexA];
		allScenes[indexA] = allScenes[indexB];
		allScenes[indexB] = temp;

		// Repaint to update the UI
		Repaint();
	}

	void LoadScenePreferences()
	{
		string sceneData = EditorPrefs.GetString("PlatformBuilder_SceneData", "");
		if (string.IsNullOrEmpty(sceneData)) return;

		var sceneEntries = sceneData.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
		foreach (var entry in sceneEntries)
		{
			var parts = entry.Split('|');
			if (parts.Length != 2) continue;

			string path = parts[0];
			bool isSelected = bool.Parse(parts[1]);

			var sceneInfo = allScenes.FirstOrDefault(s => s.path == path);
			if
