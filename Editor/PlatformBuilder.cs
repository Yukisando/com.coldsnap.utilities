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

public enum BuildPlatform
{
	Windows,
	MacOS,
	Android,
	WebGL
}

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
	public bool useCustomAppName = false; // New toggle for custom app name
	public string buildFolderPath = ""; // Remember the base build folder location
}

public class PlatformBuilder : EditorWindow
{
	private static PlatformBuilderSettings settings;
	private const string SETTINGS_KEY = "PlatformBuilder_Settings";
	
	// Scene selection variables
	List<SceneInfo> allScenes = new List<SceneInfo>();
	Vector2 sceneScrollPosition;
	
	// Drag and drop variables
	private int draggedIndex = -1;
	private bool isDragging = false;

	[MenuItem("Tools/Platform Builder #&B")]
	static void ShowWindow()
	{
		var window = GetWindow<PlatformBuilder>("Platform Builder");
		window.Show();
		window.autoRepaintOnSceneChange = true;
	}

	void OnEnable()
	{
		// Ensure settings is initialized before doing anything else
		if (settings == null)
		{
			settings = new PlatformBuilderSettings();
		}
		
		LoadSettings();
		RefreshSceneList();
		UpdateWindowSize();
	}

	void OnDisable()
	{
		SaveSettings();
	}
	
	// Auto-size the window based on content
	void UpdateWindowSize()
	{
		float width = 400f;
		float height = 300f; // Base height
		
		if (!settings.currentSceneOnly && allScenes.Count > 0)
		{
			height += Mathf.Min(allScenes.Count * 25f, 200f) ; // Scene list height + padding
		}
		
		minSize = new Vector2(width, height);
		maxSize = new Vector2(width + 100f, height + 200f);
	}
	
	// Get automatic app name based on scene selection
	string GetAutoAppName()
	{
		if (settings.currentSceneOnly)
		{
			var currentScene = SceneManager.GetActiveScene();
			return string.IsNullOrEmpty(currentScene.name) ? PlayerSettings.productName : currentScene.name;
		}
		else
		{
			var selectedScenes = allScenes.Where(s => s.isSelected).ToList();
			if (selectedScenes.Count == 1)
			{
				return selectedScenes[0].sceneName;
			}
			else
			{
				return PlayerSettings.productName;
			}
		}
	}

	void LoadSettings()
	{
		// Ensure settings is never null
		if (settings == null)
		{
			settings = new PlatformBuilderSettings();
		}
		
		string json = EditorPrefs.GetString(SETTINGS_KEY, "");
		if (!string.IsNullOrEmpty(json))
		{
			try
			{
				var loadedSettings = JsonUtility.FromJson<PlatformBuilderSettings>(json);
				if (loadedSettings != null)
				{
					settings = loadedSettings;
				}
			}
			catch (System.Exception e)
			{
				Debug.LogWarning($"Failed to load PlatformBuilder settings: {e.Message}");
				settings = new PlatformBuilderSettings();
			}
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
		
		// Get all scene files in the Assets folder only, excluding plugins
		string[] sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });
		
		foreach (string guid in sceneGuids)
		{
			string scenePath = AssetDatabase.GUIDToAssetPath(guid);
			
			// Skip scenes in the plugins folder
			if (scenePath.ToLower().Contains("/plugins/") || scenePath.ToLower().Contains("\\plugins\\"))
				continue;
			
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

	void MoveScene(int fromIndex, int toIndex)
	{
		if (fromIndex < 0 || fromIndex >= allScenes.Count || toIndex < 0 || toIndex >= allScenes.Count)
			return;

		var scene = allScenes[fromIndex];
		allScenes.RemoveAt(fromIndex);
		allScenes.Insert(toIndex, scene);

		// Update build orders
		for (int i = 0; i < allScenes.Count; i++)
		{
			allScenes[i].buildOrder = i;
		}

		SaveSettings();
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
		
		bool previousCurrentSceneOnly = settings.currentSceneOnly;
		settings.currentSceneOnly = EditorGUILayout.Toggle("Current Scene Only", settings.currentSceneOnly);
		
		// Update window size if scene selection mode changed
		if (previousCurrentSceneOnly != settings.currentSceneOnly)
		{
			UpdateWindowSize();
		}
		
		EditorGUILayout.Space();
		
		// Build folder section
		GUILayout.Label("Build Settings", EditorStyles.boldLabel);
		
		EditorGUILayout.BeginHorizontal();
		EditorGUILayout.LabelField("Build Folder:", GUILayout.Width(80));
		if (string.IsNullOrEmpty(settings.buildFolderPath))
		{
			EditorGUILayout.LabelField("(Not set)", EditorStyles.miniLabel);
		}
		else
		{
			EditorGUILayout.LabelField(settings.buildFolderPath, EditorStyles.miniLabel);
		}
		
		if (GUILayout.Button("Browse", GUILayout.Width(60)))
		{
			string selectedPath = EditorUtility.OpenFolderPanel("Select Build Folder", settings.buildFolderPath, "");
			if (!string.IsNullOrEmpty(selectedPath))
			{
				settings.buildFolderPath = selectedPath;
				SaveSettings();
			}
		}
		
		if (!string.IsNullOrEmpty(settings.buildFolderPath) && GUILayout.Button("Clear", GUILayout.Width(50)))
		{
			settings.buildFolderPath = "";
			SaveSettings();
		}
		EditorGUILayout.EndHorizontal();
		
		EditorGUILayout.Space();
		
		// App name section - fixed layout
		settings.useCustomAppName = EditorGUILayout.Toggle("Use Custom App Name", settings.useCustomAppName);
		
		GUI.enabled = settings.useCustomAppName;
		if (settings.useCustomAppName)
		{
			settings.appName = EditorGUILayout.TextField("App Name", settings.appName);
		}
		else
		{
			// Show auto-generated name as read-only
			string autoName = GetAutoAppName();
			EditorGUILayout.TextField("App Name", autoName);
			settings.appName = autoName; // Update the actual app name
		}
		GUI.enabled = true;
		
		EditorGUILayout.Space();
		
		// Scene selection section
		if (!settings.currentSceneOnly)
		{
			GUILayout.Label("Scene Selection & Build Order", EditorStyles.boldLabel);
			GUILayout.Label("Click and drag the ≡ handle to reorder scenes", EditorStyles.miniLabel);
			
			EditorGUILayout.BeginHorizontal();
			if (GUILayout.Button("Select All"))
			{
				foreach (var scene in allScenes)
				{
					scene.isSelected = true;
				}
				SaveSettings();
				Repaint();
			}
			if (GUILayout.Button("Select None"))
			{
				foreach (var scene in allScenes)
				{
					scene.isSelected = false;
				}
				SaveSettings();
				Repaint();
			}
			if (GUILayout.Button("Refresh Scenes"))
			{
				RefreshSceneList();
				UpdateWindowSize();
				Repaint();
			}
			EditorGUILayout.EndHorizontal();
			
			EditorGUILayout.Space();
			
			// Scene list with scroll and drag-drop
			sceneScrollPosition = EditorGUILayout.BeginScrollView(sceneScrollPosition, GUILayout.MaxHeight(300));
			
			for (int i = 0; i < allScenes.Count; i++)
			{
				EditorGUILayout.BeginHorizontal();
				
				// Scene selection toggle
				bool wasSelected = allScenes[i].isSelected;
				allScenes[i].isSelected = EditorGUILayout.Toggle(allScenes[i].isSelected, GUILayout.Width(20));
				
				if (wasSelected != allScenes[i].isSelected)
				{
					SaveSettings();
				}
				
				// Drag handle with proper event handling
				Rect dragRect = GUILayoutUtility.GetRect(20, EditorGUIUtility.singleLineHeight);
				GUI.Label(dragRect, "≡", EditorStyles.centeredGreyMiniLabel);
				HandleDragAndDrop(dragRect, i);
				
				// Scene name with build order
				string displayName = $"{i}: {allScenes[i].sceneName}";
				if (allScenes[i].isSelected)
				{
					GUI.color = Color.green;
				}
				EditorGUILayout.LabelField(displayName);
				GUI.color = Color.white;
				
				EditorGUILayout.EndHorizontal();
			}
			
			EditorGUILayout.EndScrollView();
		}
		
		EditorGUILayout.Space();
		
		// Build button
		GUI.enabled = true;
		if (GUILayout.Button("Build", GUILayout.Height(30)))
		{
			BuildForPlatform();
		}
	}
	
	void HandleDragAndDrop(Rect dragRect, int index)
	{
		Event evt = Event.current;
		int controlID = GUIUtility.GetControlID(FocusType.Passive);
		
		switch (evt.type)
		{
			case EventType.MouseDown:
				if (dragRect.Contains(evt.mousePosition) && evt.button == 0)
				{
					GUIUtility.hotControl = controlID;
					draggedIndex = index;
					isDragging = false; // Start as false, only set to true on drag
					evt.Use();
				}
				break;
				
			case EventType.MouseDrag:
				if (GUIUtility.hotControl == controlID)
				{
					isDragging = true;
					EditorGUIUtility.AddCursorRect(dragRect, MouseCursor.MoveArrow);
					Repaint();
					evt.Use();
				}
				break;
				
			case EventType.MouseUp:
				if (GUIUtility.hotControl == controlID)
				{
					GUIUtility.hotControl = 0;
					
					if (isDragging)
					{
						// Find drop target
						Vector2 mousePos = evt.mousePosition;
						for (int i = 0; i < allScenes.Count; i++)
						{
							if (i != draggedIndex)
							{
								float lineY = i * EditorGUIUtility.singleLineHeight;
								if (mousePos.y >= lineY && mousePos.y <= lineY + EditorGUIUtility.singleLineHeight)
								{
									MoveScene(draggedIndex, i);
									break;
								}
							}
						}
					}
					
					draggedIndex = -1;
					isDragging = false;
					evt.Use();
					Repaint();
				}
				break;
		}
		
		// Visual feedback for dragging
		if (isDragging && draggedIndex == index)
		{
			EditorGUI.DrawRect(dragRect, new Color(0.5f, 0.5f, 1f, 0.3f));
		}
		
		// Change cursor when hovering over drag handle
		if (dragRect.Contains(Event.current.mousePosition))
		{
			EditorGUIUtility.AddCursorRect(dragRect, MouseCursor.MoveArrow);
		}
	}
	
	void BuildForPlatform()
	{
		// Check if build folder is set, if not, ask for it
		if (string.IsNullOrEmpty(settings.buildFolderPath))
		{
			string selectedPath = EditorUtility.OpenFolderPanel("Select Build Folder", "", "");
			if (string.IsNullOrEmpty(selectedPath))
				return;
			
			settings.buildFolderPath = selectedPath;
			SaveSettings();
		}
		
		// Verify the build folder still exists
		if (!Directory.Exists(settings.buildFolderPath))
		{
			if (EditorUtility.DisplayDialog("Build Folder Not Found", 
				$"The build folder '{settings.buildFolderPath}' no longer exists.\n\nSelect a new build folder?", 
				"Yes", "Cancel"))
			{
				string selectedPath = EditorUtility.OpenFolderPanel("Select Build Folder", "", "");
				if (string.IsNullOrEmpty(selectedPath))
					return;
				
				settings.buildFolderPath = selectedPath;
				SaveSettings();
			}
			else
			{
				return;
			}
		}
		
		// Get app name
		string appName = string.IsNullOrEmpty(settings.appName) ? PlayerSettings.productName : settings.appName;
		if (string.IsNullOrEmpty(appName))
		{
			appName = "MyApp";
		}
		
		// Store original product name to restore later
		string originalProductName = PlayerSettings.productName;
		
		// For Android, set the product name which affects the displayed app name
		if (settings.selectedPlatform == BuildPlatform.Android)
		{
			PlayerSettings.productName = appName;
		}
		
		// Create platform-specific build path
		string platformName = "";
		string buildPath = "";
		BuildTarget buildTarget = BuildTarget.StandaloneWindows64;
		
		switch (settings.selectedPlatform)
		{
			case BuildPlatform.Windows:
				buildTarget = BuildTarget.StandaloneWindows64;
				platformName = "Windows";
				// Build/Windows/AppName/AppName.exe
				string windowsFolder = Path.Combine(settings.buildFolderPath, platformName, appName);
				Directory.CreateDirectory(windowsFolder);
				buildPath = Path.Combine(windowsFolder, appName + ".exe");
				break;
				
			case BuildPlatform.MacOS:
				buildTarget = BuildTarget.StandaloneOSX;
				platformName = "MacOS";
				// Build/MacOS/AppName/AppName.app
				string macFolder = Path.Combine(settings.buildFolderPath, platformName, appName);
				Directory.CreateDirectory(macFolder);
				buildPath = Path.Combine(macFolder, appName + ".app");
				break;
				
			case BuildPlatform.Android:
				buildTarget = BuildTarget.Android;
				platformName = "Android";
				// Build/Android/AppName.apk
				string androidFolder = Path.Combine(settings.buildFolderPath, platformName);
				Directory.CreateDirectory(androidFolder);
				buildPath = Path.Combine(androidFolder, appName + ".apk");
				break;
				
			case BuildPlatform.WebGL:
				buildTarget = BuildTarget.WebGL;
				platformName = "WebGL";
				// Build/WebGL/AppName/
				string webglFolder = Path.Combine(settings.buildFolderPath, platformName, appName);
				Directory.CreateDirectory(webglFolder);
				buildPath = webglFolder;
				break;
		}
		
		// Get scenes to build
		string[] scenesToBuild;
		if (settings.currentSceneOnly)
		{
			var currentScene = SceneManager.GetActiveScene();
			scenesToBuild = new[] { currentScene.path };
		}
		else
		{
			var selectedScenes = allScenes.Where(s => s.isSelected).OrderBy(s => s.buildOrder).Select(s => s.scenePath).ToArray();
			if (selectedScenes.Length == 0)
			{
				EditorUtility.DisplayDialog("No Scenes Selected", "Please select at least one scene to build.", "OK");
				// Restore original product name before returning
				if (settings.selectedPlatform == BuildPlatform.Android)
				{
					PlayerSettings.productName = originalProductName;
				}
				return;
			}
			scenesToBuild = selectedScenes;
		}
		
		// Update EditorBuildSettings to ensure proper scene order and inclusion
		List<EditorBuildSettingsScene> buildSettingsScenes = new List<EditorBuildSettingsScene>();
		for (int i = 0; i < scenesToBuild.Length; i++)
		{
			buildSettingsScenes.Add(new EditorBuildSettingsScene(scenesToBuild[i], true));
		}
		EditorBuildSettings.scenes = buildSettingsScenes.ToArray();
		
		// Setup build player options
		BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions();
		buildPlayerOptions.locationPathName = buildPath;
		buildPlayerOptions.target = buildTarget;
		buildPlayerOptions.scenes = scenesToBuild;
		
		// Set build options
		BuildOptions buildOptions = BuildOptions.None;
		if (settings.isDebugBuild)
		{
			buildOptions |= BuildOptions.Development;
		}
		
		// For Android, add additional options to help with installation
		if (settings.selectedPlatform == BuildPlatform.Android)
		{
			// Development builds are easier to install and debug
			if (settings.isDebugBuild)
			{
				buildOptions |= BuildOptions.Development | BuildOptions.AllowDebugging;
			}
		}
		
		if (settings.autoRunPlayer)
		{
			buildOptions |= BuildOptions.AutoRunPlayer;
		}
		
		buildPlayerOptions.options = buildOptions;
		
		Debug.Log($"Building for {settings.selectedPlatform} to: {buildPath}");
		Debug.Log($"Scenes to build ({scenesToBuild.Length}): {string.Join(", ", scenesToBuild.Select(s => Path.GetFileNameWithoutExtension(s)))}");
		
		BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
		BuildSummary summary = report.summary;
		
		// Restore original product name after build
		if (settings.selectedPlatform == BuildPlatform.Android)
		{
			PlayerSettings.productName = originalProductName;
		}
		
		if (summary.result == BuildResult.Succeeded)
		{
			Debug.Log($"Build succeeded: {summary.outputPath}");
			
			// For Android, provide additional information
			if (settings.selectedPlatform == BuildPlatform.Android)
			{
				string message = $"Android build completed successfully!\n\nPath: {summary.outputPath}\n\n";
				if (settings.isDebugBuild)
				{
					message += "Note: This is a development build which should install easier on your device.\n\n";
				}
				else
				{
					message += "Note: For easier installation during development, consider using 'Debug Build' option.\n\n";
				}
				message += "Open build folder?";
				
				if (!settings.autoRunPlayer)
				{
					if (EditorUtility.DisplayDialog("Android Build Complete", message, "Yes", "No"))
					{
						string folderToOpen = Path.GetDirectoryName(summary.outputPath);
						if (Directory.Exists(folderToOpen))
						{
							Process.Start(folderToOpen);
						}
					}
				}
			}
			else if (!settings.autoRunPlayer)
			{
				if (EditorUtility.DisplayDialog("Build Complete", $"Build completed successfully!\n\nPath: {summary.outputPath}\n\nOpen build folder?", "Yes", "No"))
				{
					// For different platforms, open the appropriate folder
					string folderToOpen = "";
					switch (settings.selectedPlatform)
					{
						case BuildPlatform.WebGL:
							folderToOpen = summary.outputPath;
							break;
						default:
							folderToOpen = Path.GetDirectoryName(summary.outputPath);
							break;
					}
					
					if (Directory.Exists(folderToOpen))
					{
						Process.Start(folderToOpen);
					}
				}
			}
		}
		else if (summary.result == BuildResult.Failed)
		{
			Debug.LogError("Build failed");
		}
	}
}
