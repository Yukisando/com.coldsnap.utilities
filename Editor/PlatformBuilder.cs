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

[System.Serializable]
public class SceneInfo
{
	public string scenePath;
	public string sceneName;
	public bool isSelected;
	public int buildOrder;

	public SceneInfo(string path, string name, bool selected = false, int order = 0)
	{
		scenePath = path;
		sceneName = name;
		isSelected = selected;
		buildOrder = order;
	}
}

[System.Serializable]
public class PlatformBuilderSettings
{
	public List<SceneInfo> sceneSettings = new List<SceneInfo>();
	public BuildPlatform selectedPlatform = BuildPlatform.Windows;
	public bool isDebugBuild;
	public bool autoRunPlayer;
	public bool currentSceneOnly = true;
	public string appName = "";
}

public class PlatformBuilder : EditorWindow
{
	enum BuildPlatform
	{
		Windows,
		MacOS,
		Android,
		WebGL
	}

	private static PlatformBuilderSettings settings;
	private const string SETTINGS_KEY = "PlatformBuilder_Settings";
	
	// Scene selection variables
	List<SceneInfo> allScenes = new List<SceneInfo>();
	Vector2 sceneScrollPosition;

	[MenuItem("Tools/Platform Builder")]
	static void ShowWindow()
	{
		GetWindow<PlatformBuilder>("Platform Builder");
	}

	void OnEnable()
	{
		LoadSettings();
		RefreshSceneList();
	}

	void OnDisable()
	{
		SaveSettings();
	}

	void LoadSettings()
	{
		string json = EditorPrefs.GetString(SETTINGS_KEY, "");
		if (!string.IsNullOrEmpty(json))
		{
			try
			{
				settings = JsonUtility.FromJson<PlatformBuilderSettings>(json);
			}
			catch
			{
				settings = new PlatformBuilderSettings();
			}
		}
		else
		{
			settings = new PlatformBuilderSettings();
		}
	}

	void SaveSettings()
	{
		// Update settings with current scene info
		settings.sceneSettings = allScenes.ToList();
		
		string json = JsonUtility.ToJson(settings, true);
		EditorPrefs.SetString(SETTINGS_KEY, json);
	}

	void RefreshSceneList()
	{
		allScenes.Clear();
		
		// Get all scenes in build settings
		var buildScenes = EditorBuildSettings.scenes.ToList();
		
		// Get all scene files in the project
		string[] sceneGuids = AssetDatabase.FindAssets("t:Scene");
		
		foreach (string guid in sceneGuids)
		{
			string scenePath = AssetDatabase.GUIDToAssetPath(guid);
			string sceneName = Path.GetFileNameWithoutExtension(scenePath);
			
			// Check if we have saved settings for this scene
			var savedScene = settings.sceneSettings.FirstOrDefault(s => s.scenePath == scenePath);
			bool isSelected = savedScene?.isSelected ?? buildScenes.Any(bs => bs.path == scenePath && bs.enabled);
			int buildOrder = savedScene?.buildOrder ?? allScenes.Count;
			
			allScenes.Add(new SceneInfo(scenePath, sceneName, isSelected, buildOrder));
		}
		
		// Sort by build order
		allScenes = allScenes.OrderBy(s => s.buildOrder).ToList();
		
		// Update build orders to be sequential
		for (int i = 0; i < allScenes.Count; i++)
		{
			allScenes[i].buildOrder = i;
		}
	}

	void OnGUI()
	{
		GUILayout.Label("Platform Builder", EditorStyles.boldLabel);
		
		EditorGUILayout.Space();
		
		// Platform selection
		settings.selectedPlatform = (BuildPlatform)EditorGUILayout.EnumPopup("Target Platform", settings.selectedPlatform);
		
		EditorGUILayout.Space();
		
		// Build options
		settings.isDebugBuild = EditorGUILayout.Toggle("Debug Build", settings.isDebugBuild);
		settings.autoRunPlayer = EditorGUILayout.Toggle("Auto Run Player", settings.autoRunPlayer);
		settings.currentSceneOnly = EditorGUILayout.Toggle("Current Scene Only", settings.currentSceneOnly);
		
		EditorGUILayout.Space();
		
		// App name
		settings.appName = EditorGUILayout.TextField("App Name", settings.appName);
		
		EditorGUILayout.Space();
		
		// Scene selection section
		if (!settings.currentSceneOnly)
		{
			GUILayout.Label("Scene Selection & Build Order", EditorStyles.boldLabel);
			
			EditorGUILayout.BeginHorizontal();
			if (GUILayout.Button("Select All"))
			{
				foreach (var scene in allScenes)
				{
					scene.isSelected = true;
				}
			}
			if (GUILayout.Button("Select None"))
			{
				foreach (var scene in allScenes)
				{
					scene.isSelected = false;
				}
			}
			if (GUILayout.Button("Refresh Scenes"))
			{
				RefreshSceneList();
			}
			EditorGUILayout.EndHorizontal();
			
			EditorGUILayout.Space();
			
			// Scene list with scroll
			sceneScrollPosition = EditorGUILayout.BeginScrollView(sceneScrollPosition, GUILayout.MaxHeight(200));
			
			for (int i = 0; i < allScenes.Count; i++)
			{
				EditorGUILayout.BeginHorizontal();
				
				// Up/Down arrows for reordering
				GUI.enabled = i > 0;
				if (GUILayout.Button("↑", GUILayout.Width(25)))
				{
					var temp = allScenes[i];
					allScenes[i] = allScenes[i - 1];
					allScenes[i - 1] = temp;
					
					// Update build orders
					allScenes[i].buildOrder = i;
					allScenes[i - 1].buildOrder = i - 1;
				}
				
				GUI.enabled = i < allScenes.Count - 1;
				if (GUILayout.Button("↓", GUILayout.Width(25)))
				{
					var temp = allScenes[i];
					allScenes[i] = allScenes[i + 1];
					allScenes[i + 1] = temp;
					
					// Update build orders
					allScenes[i].buildOrder = i;
					allScenes[i + 1].buildOrder = i + 1;
				}
				
				GUI.enabled = true;
				
				// Scene selection checkbox
				bool newSelected = EditorGUILayout.Toggle(allScenes[i].isSelected, GUILayout.Width(20));
				if (newSelected != allScenes[i].isSelected)
				{
					allScenes[i].isSelected = newSelected;
				}
				
				// Scene name
				EditorGUILayout.LabelField($"{i + 1}. {allScenes[i].sceneName}", GUILayout.ExpandWidth(true));
				
				EditorGUILayout.EndHorizontal();
			}
			
			EditorGUILayout.EndScrollView();
			
			EditorGUILayout.Space();
			
			// Show selected scenes count
			int selectedCount = allScenes.Count(s => s.isSelected);
			EditorGUILayout.LabelField($"Selected Scenes: {selectedCount} / {allScenes.Count}");
		}
		
		EditorGUILayout.Space();
		
		// Build button
		GUI.backgroundColor = Color.green;
		if (GUILayout.Button("Build", GUILayout.Height(40)))
		{
			BuildProject();
		}
		GUI.backgroundColor = Color.white;
		
		// Auto-save settings when values change
		if (GUI.changed)
		{
			SaveSettings();
		}
	}

	void BuildProject()
	{
		try
		{
			string targetPath = GetBuildPath();
			if (string.IsNullOrEmpty(targetPath))
				return;

			// Get scenes to build
			string[] scenesToBuild = GetScenesToBuild();
			if (scenesToBuild.Length == 0)
			{
				EditorUtility.DisplayDialog("Error", "No scenes selected for build!", "OK");
				return;
			}

			// Set build options
			BuildOptions buildOptions = BuildOptions.None;
			if (settings.isDebugBuild)
				buildOptions |= BuildOptions.Development;
			if (settings.autoRunPlayer)
				buildOptions |= BuildOptions.AutoRunPlayer;

			// Set build target
			BuildTarget buildTarget = GetBuildTarget();
			BuildTargetGroup buildTargetGroup = GetBuildTargetGroup();

			// Perform build
			BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions
			{
				scenes = scenesToBuild,
				locationPathName = targetPath,
				target = buildTarget,
				targetGroup = buildTargetGroup,
				options = buildOptions
			};

			BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
			BuildSummary summary = report.summary;

			if (summary.result == BuildResult.Succeeded)
			{
				Debug.Log($"Build succeeded: {summary.outputPath}");
				EditorUtility.DisplayDialog("Build Complete", $"Build completed successfully!\nOutput: {summary.outputPath}", "OK");
				
				if (settings.autoRunPlayer && File.Exists(summary.outputPath))
				{
					Process.Start(summary.outputPath);
				}
			}
			else if (summary.result == BuildResult.Failed)
			{
				Debug.LogError("Build failed!");
				EditorUtility.DisplayDialog("Build Failed", "Build failed! Check console for details.", "OK");
			}
		}
		catch (Exception e)
		{
			Debug.LogError($"Build error: {e.Message}");
			EditorUtility.DisplayDialog("Build Error", $"An error occurred during build:\n{e.Message}", "OK");
		}
	}

	string[] GetScenesToBuild()
	{
		if (settings.currentSceneOnly)
		{
			var activeScene = SceneManager.GetActiveScene();
			if (!string.IsNullOrEmpty(activeScene.path))
			{
				return new string[] { activeScene.path };
			}
			else
			{
				EditorUtility.DisplayDialog("Error", "No active scene found!", "OK");
				return new string[0];
			}
		}
		else
		{
			return allScenes.Where(s => s.isSelected).OrderBy(s => s.buildOrder).Select(s => s.scenePath).ToArray();
		}
	}

	string GetBuildPath()
	{
		string defaultName = string.IsNullOrEmpty(settings.appName) ? Application.productName : settings.appName;
		string extension = GetBuildExtension();
		
		string path = EditorUtility.SaveFilePanel($"Build {settings.selectedPlatform}", "", defaultName, extension);
		return path;
	}

	string GetBuildExtension()
	{
		switch (settings.selectedPlatform)
		{
			case BuildPlatform.Windows:
				return "exe";
			case BuildPlatform.MacOS:
				return "app";
			case BuildPlatform.Android:
				return "apk";
			case BuildPlatform.WebGL:
				return "";
			default:
				return "";
		}
	}

	BuildTarget GetBuildTarget()
	{
		switch (settings.selectedPlatform)
		{
			case BuildPlatform.Windows:
				return BuildTarget.StandaloneWindows64;
			case BuildPlatform.MacOS:
				return BuildTarget.StandaloneOSX;
			case BuildPlatform.Android:
				return BuildTarget.Android;
			case BuildPlatform.WebGL:
				return BuildTarget.WebGL;
			default:
				return BuildTarget.StandaloneWindows64;
		}
	}

	BuildTargetGroup GetBuildTargetGroup()
	{
		switch (settings.selectedPlatform)
		{
			case BuildPlatform.Windows:
			case BuildPlatform.MacOS:
				return BuildTargetGroup.Standalone;
			case BuildPlatform.Android:
				return BuildTargetGroup.Android;
			case BuildPlatform.WebGL:
				return BuildTargetGroup.WebGL;
			default:
				return BuildTargetGroup.Standalone;
		}
	}
}
