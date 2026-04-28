#region

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System.IO.Compression;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

#endregion

public enum BuildPlatform
{
	Windows,
	MacOS,
	Android,
	AAB,
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
	public bool useCustomAppName = false;
	public bool forceLowercase = false;
	public string buildFolderPath = "";
}

public class PlatformBuilder : EditorWindow
{
	private static PlatformBuilderSettings settings;
	private const string SETTINGS_KEY = "PlatformBuilder_Settings";
	private static string SettingsFilePath => Path.Combine(Application.dataPath, "Editor/PlatformBuilderSettings.json");
	
	// Scene selection variables
	List<SceneInfo> allScenes = new List<SceneInfo>();
	Vector2 sceneScrollPosition;
	
	// Drag and drop variables
	private int draggedIndex = -1;
	private bool isDragging = false;

	// Build state
	private bool _isBuildingOrZipping = false;

	[MenuItem("ColdSnap/Platform Builder #&B")]
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
		
		if (settings.selectedPlatform == BuildPlatform.AAB)
		{
			height += 400f; // Google Play settings
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
		if (settings == null)
		{
			settings = new PlatformBuilderSettings();
		}

		// Try to load from file
		if (File.Exists(SettingsFilePath))
		{
			try
			{
				string json = File.ReadAllText(SettingsFilePath);
				var loadedSettings = JsonUtility.FromJson<PlatformBuilderSettings>(json);
				if (loadedSettings != null)
				{
					settings = loadedSettings;
				}
			}
			catch (Exception e)
			{
				Debug.LogWarning($"Failed to load PlatformBuilder settings from file: {e.Message}");
				settings = new PlatformBuilderSettings();
			}
		}
		else
		{
			// Migrate from EditorPrefs if present (legacy)
			string json = EditorPrefs.GetString(SETTINGS_KEY, "");
			if (!string.IsNullOrEmpty(json))
			{
				try
				{
					var loadedSettings = JsonUtility.FromJson<PlatformBuilderSettings>(json);
					if (loadedSettings != null)
					{
						settings = loadedSettings;
						SaveSettings(); // Save to file for future use
						EditorPrefs.DeleteKey(SETTINGS_KEY);
					}
				}
				catch (Exception e)
				{
					Debug.LogWarning($"Failed to migrate PlatformBuilder settings: {e.Message}");
					settings = new PlatformBuilderSettings();
				}
			}
		}
	}

	void SaveSettings()
	{
		settings.sceneSettings = allScenes.ToList();
		try
		{
			string json = JsonUtility.ToJson(settings, true);
			File.WriteAllText(SettingsFilePath, json);
		}
		catch (Exception e)
		{
			Debug.LogWarning($"Failed to save PlatformBuilder settings: {e.Message}");
		}
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
		BuildPlatform previousPlatform = settings.selectedPlatform;
		settings.selectedPlatform = (BuildPlatform)EditorGUILayout.EnumPopup("Target Platform", settings.selectedPlatform);
		
		if (previousPlatform != settings.selectedPlatform)
		{
			UpdateWindowSize();
		}
		
		EditorGUILayout.Space();
		
		// Build options
		settings.isDebugBuild = EditorGUILayout.Toggle("Debug Build", settings.isDebugBuild);
		settings.autoRunPlayer = EditorGUILayout.Toggle("Auto Run Player", settings.autoRunPlayer);
		
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
		
		if (!string.IsNullOrEmpty(settings.buildFolderPath) && GUILayout.Button("Open", GUILayout.Width(50)))
		{
			if (Directory.Exists(settings.buildFolderPath))
			{
				Process.Start(settings.buildFolderPath);
			}
			else
			{
				EditorUtility.DisplayDialog("Folder Not Found", $"The build folder '{settings.buildFolderPath}' no longer exists.", "OK");
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
		EditorGUILayout.BeginHorizontal();
		settings.useCustomAppName = EditorGUILayout.Toggle("Use Custom App Name", settings.useCustomAppName);
		settings.forceLowercase = EditorGUILayout.Toggle("Force Lowercase", settings.forceLowercase);
		EditorGUILayout.EndHorizontal();
		
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
		
		// Current Scene Only toggle moved closer to scene section
		bool previousCurrentSceneOnly = settings.currentSceneOnly;
		settings.currentSceneOnly = EditorGUILayout.Toggle("Current Scene Only", settings.currentSceneOnly);
		
		// Update window size if scene selection mode changed
		if (previousCurrentSceneOnly != settings.currentSceneOnly)
		{
			UpdateWindowSize();
		}
		
		// Scene selection section
		if (!settings.currentSceneOnly)
		{
			EditorGUILayout.Space();
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
		
		// AAB / Google Play settings
		if (settings.selectedPlatform == BuildPlatform.AAB)
		{
			DrawAABSettings();
		}
		
		EditorGUILayout.Space();

		// Spinner while building/zipping
		if (_isBuildingOrZipping)
		{
			int spinFrame = Mathf.FloorToInt((float)(EditorApplication.timeSinceStartup * 8)) % 8;
			string[] spinFrames = { "|", "/", "-", "\\", "|", "/", "-", "\\" };
			var spinStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 11 };
			EditorGUILayout.LabelField(spinFrames[spinFrame] + "  Working...", spinStyle, GUILayout.Height(18));
			Repaint();
		}

		// Build buttons
		GUI.enabled = !_isBuildingOrZipping;
		string buildButtonLabel = settings.selectedPlatform == BuildPlatform.AAB ? "Build AAB for Google Play" : "Build";
		EditorGUILayout.BeginHorizontal();
		if (GUILayout.Button(buildButtonLabel, GUILayout.Height(30)))
		{
			BuildForPlatform(false);
		}
		if (GUILayout.Button("Build & Zip to Desktop", GUILayout.Height(30)))
		{
			BuildForPlatform(true);
		}
		EditorGUILayout.EndHorizontal();
		GUI.enabled = true;
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
	
	Vector2 aabScrollPosition;
	
	void DrawAABSettings()
	{
		EditorGUILayout.Space();
		GUILayout.Label("── Google Play Production Settings ──", EditorStyles.boldLabel);
		
		aabScrollPosition = EditorGUILayout.BeginScrollView(aabScrollPosition, GUILayout.MaxHeight(400));
		
		// Signing / Keystore
		EditorGUILayout.Space();
		GUILayout.Label("Signing Configuration", EditorStyles.boldLabel);
		
		PlayerSettings.Android.useCustomKeystore = EditorGUILayout.Toggle("Use Custom Keystore", PlayerSettings.Android.useCustomKeystore);
		
		if (PlayerSettings.Android.useCustomKeystore)
		{
			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.LabelField("Keystore Path:", GUILayout.Width(100));
			string keystorePath = PlayerSettings.Android.keystoreName;
			EditorGUILayout.LabelField(string.IsNullOrEmpty(keystorePath) ? "(Not set)" : keystorePath, EditorStyles.miniLabel);
			if (GUILayout.Button("Browse", GUILayout.Width(60)))
			{
				string path = EditorUtility.OpenFilePanel("Select Keystore", "", "keystore,jks,bks");
				if (!string.IsNullOrEmpty(path))
				{
					PlayerSettings.Android.keystoreName = path;
				}
			}
			EditorGUILayout.EndHorizontal();
			
			PlayerSettings.Android.keystorePass = EditorGUILayout.PasswordField("Keystore Password", PlayerSettings.Android.keystorePass);
			PlayerSettings.Android.keyaliasName = EditorGUILayout.TextField("Key Alias", PlayerSettings.Android.keyaliasName);
			PlayerSettings.Android.keyaliasPass = EditorGUILayout.PasswordField("Key Alias Password", PlayerSettings.Android.keyaliasPass);
			
			if (string.IsNullOrEmpty(PlayerSettings.Android.keystoreName))
			{
				EditorGUILayout.HelpBox("A signed upload key is required for Google Play uploads. Create one via keytool or Android Studio.", MessageType.Warning);
			}
		}
		else
		{
			EditorGUILayout.HelpBox("Custom keystore is required for Google Play. Debug-signed builds cannot be uploaded.", MessageType.Warning);
		}
		
		// Versioning
		EditorGUILayout.Space();
		GUILayout.Label("Versioning", EditorStyles.boldLabel);
		PlayerSettings.bundleVersion = EditorGUILayout.TextField("Version Name", PlayerSettings.bundleVersion);
		PlayerSettings.Android.bundleVersionCode = EditorGUILayout.IntField("Version Code", PlayerSettings.Android.bundleVersionCode);
		EditorGUILayout.HelpBox("Version Code must be incremented with each upload to Google Play.", MessageType.Info);
		
		// Package Identification
		EditorGUILayout.Space();
		GUILayout.Label("Package Identification", EditorStyles.boldLabel);
		string bundleId = PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android);
		string newBundleId = EditorGUILayout.TextField("Package Name", bundleId);
		if (newBundleId != bundleId)
		{
			PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, newBundleId);
		}
		EditorGUILayout.HelpBox("Must match your Google Play Console package name (e.g., com.company.app).", MessageType.Info);
		
		// Scripting Backend & Architecture
		EditorGUILayout.Space();
		GUILayout.Label("Scripting & Architecture", EditorStyles.boldLabel);
		
		var currentBackend = PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android);
		var newBackend = (ScriptingImplementation)EditorGUILayout.EnumPopup("Scripting Backend", currentBackend);
		if (newBackend != currentBackend)
		{
			PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, newBackend);
		}
		if (currentBackend != ScriptingImplementation.IL2CPP)
		{
			EditorGUILayout.HelpBox("IL2CPP is required for Google Play (64-bit support is mandatory).", MessageType.Warning);
		}
		
		var currentArch = PlayerSettings.Android.targetArchitectures;
		var newArch = (AndroidArchitecture)EditorGUILayout.EnumFlagsField("Target Architectures", currentArch);
		if (newArch != currentArch)
		{
			PlayerSettings.Android.targetArchitectures = newArch;
		}
		if ((currentArch & AndroidArchitecture.ARM64) == 0)
		{
			EditorGUILayout.HelpBox("ARM64 is required for Google Play Store.", MessageType.Warning);
		}
		
		// SDK Versions
		EditorGUILayout.Space();
		GUILayout.Label("SDK Versions", EditorStyles.boldLabel);
		
		var currentTargetSdk = PlayerSettings.Android.targetSdkVersion;
		var newTargetSdk = (AndroidSdkVersions)EditorGUILayout.EnumPopup("Target SDK", currentTargetSdk);
		if (newTargetSdk != currentTargetSdk)
		{
			PlayerSettings.Android.targetSdkVersion = newTargetSdk;
		}
		
		var currentMinSdk = PlayerSettings.Android.minSdkVersion;
		var newMinSdk = (AndroidSdkVersions)EditorGUILayout.EnumPopup("Min SDK", currentMinSdk);
		if (newMinSdk != currentMinSdk)
		{
			PlayerSettings.Android.minSdkVersion = newMinSdk;
		}
		EditorGUILayout.HelpBox("Google Play requires Target SDK to meet their current API level policy.", MessageType.Info);
		
		// Optimization
		EditorGUILayout.Space();
		GUILayout.Label("Optimization", EditorStyles.boldLabel);
		PlayerSettings.Android.minifyRelease = EditorGUILayout.Toggle("Minify Release (R8/ProGuard)", PlayerSettings.Android.minifyRelease);
		
		EditorGUILayout.EndScrollView();
	}
	
	bool ValidateAABBuild()
	{
		List<string> warnings = new List<string>();
		List<string> errors = new List<string>();
		
		// Check IL2CPP
		var backend = PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android);
		if (backend != ScriptingImplementation.IL2CPP)
		{
			errors.Add("Scripting Backend must be IL2CPP (required for 64-bit Google Play builds).");
		}
		
		// Check ARM64
		var arch = PlayerSettings.Android.targetArchitectures;
		if ((arch & AndroidArchitecture.ARM64) == 0)
		{
			errors.Add("ARM64 architecture is required for Google Play.");
		}
		
		// Check keystore
		if (!PlayerSettings.Android.useCustomKeystore)
		{
			warnings.Add("No custom keystore configured. Build will use debug signing and cannot be uploaded to Google Play.");
		}
		else if (string.IsNullOrEmpty(PlayerSettings.Android.keystoreName))
		{
			warnings.Add("Custom keystore is enabled but no keystore file is set.");
		}
		
		// Check package name
		string packageName = PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android);
		if (string.IsNullOrEmpty(packageName) || packageName == "com.Company.ProductName")
		{
			warnings.Add("Package name appears to be default. Update it to match your Google Play Console listing.");
		}
		
		// Check version code
		if (PlayerSettings.Android.bundleVersionCode <= 0)
		{
			warnings.Add("Bundle Version Code should be greater than 0.");
		}
		
		if (errors.Count > 0 || warnings.Count > 0)
		{
			string message = "";
			if (errors.Count > 0)
			{
				message += "ERRORS (must fix before building):\n\n";
				foreach (var error in errors)
					message += "\u2022 " + error + "\n";
				message += "\n";
			}
			if (warnings.Count > 0)
			{
				message += "WARNINGS:\n\n";
				foreach (var warning in warnings)
					message += "\u2022 " + warning + "\n";
				message += "\n";
			}
			
			if (errors.Count > 0)
			{
				EditorUtility.DisplayDialog("AAB Build Validation Failed", message, "OK");
				return false;
			}
			else
			{
				return EditorUtility.DisplayDialog("AAB Build Warnings", message + "Continue with build?", "Build Anyway", "Cancel");
			}
		}
		
		return true;
	}
	
	void BuildForPlatform(bool zipToDesktop = false)
	{
		_isBuildingOrZipping = true;
		Repaint();
		string zipSource = "";
		bool zipIsDirectory = false;
		try
		{
		DoBuild(zipToDesktop, ref zipSource, ref zipIsDirectory);
		}
		finally
		{
			EditorUtility.ClearProgressBar();
			_isBuildingOrZipping = false;
			Repaint();
		}
	}

	void DoBuild(bool zipToDesktop, ref string zipSource, ref bool zipIsDirectory)
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
		
		// Apply force lowercase if enabled
		if (settings.forceLowercase)
		{
			appName = appName.ToLower();
		}
		
		// Store original product name to restore later
		string originalProductName = PlayerSettings.productName;
		
		// For Android/AAB, set the product name which affects the displayed app name
		if (settings.selectedPlatform == BuildPlatform.Android || settings.selectedPlatform == BuildPlatform.AAB)
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
				zipSource = windowsFolder;
				zipIsDirectory = true;
				break;
				
			case BuildPlatform.MacOS:
				buildTarget = BuildTarget.StandaloneOSX;
				platformName = "MacOS";
				// Build/MacOS/AppName/AppName.app
				string macFolder = Path.Combine(settings.buildFolderPath, platformName, appName);
				Directory.CreateDirectory(macFolder);
				buildPath = Path.Combine(macFolder, appName + ".app");
				zipSource = macFolder;
				zipIsDirectory = true;
				break;
				
			case BuildPlatform.Android:
				buildTarget = BuildTarget.Android;
				platformName = "Android";
				EditorUserBuildSettings.buildAppBundle = false;
				// Build/Android/AppName.apk
				string androidFolder = Path.Combine(settings.buildFolderPath, platformName);
				Directory.CreateDirectory(androidFolder);
				buildPath = Path.Combine(androidFolder, appName + ".apk");
				zipSource = buildPath;
				zipIsDirectory = false;
				break;
				
			case BuildPlatform.AAB:
				buildTarget = BuildTarget.Android;
				platformName = "AAB";
				EditorUserBuildSettings.buildAppBundle = true;
				// Build/AAB/AppName.aab
				string aabFolder = Path.Combine(settings.buildFolderPath, platformName);
				Directory.CreateDirectory(aabFolder);
				buildPath = Path.Combine(aabFolder, appName + ".aab");
				zipSource = buildPath;
				zipIsDirectory = false;
				break;
				
			case BuildPlatform.WebGL:
				buildTarget = BuildTarget.WebGL;
				platformName = "WebGL";
				// Build/WebGL/AppName/
				string webglFolder = Path.Combine(settings.buildFolderPath, platformName, appName);
				Directory.CreateDirectory(webglFolder);
				buildPath = webglFolder;
				zipSource = webglFolder;
				zipIsDirectory = true;
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
				if (settings.selectedPlatform == BuildPlatform.Android || settings.selectedPlatform == BuildPlatform.AAB)
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
		
		// For Android/AAB, add additional options to help with installation
		if (settings.selectedPlatform == BuildPlatform.Android || settings.selectedPlatform == BuildPlatform.AAB)
		{
			// Development builds are easier to install and debug
			if (settings.isDebugBuild)
			{
				buildOptions |= BuildOptions.Development | BuildOptions.AllowDebugging;
			}
		}
		
		// Validate AAB settings before building
		if (settings.selectedPlatform == BuildPlatform.AAB)
		{
			if (!ValidateAABBuild())
			{
				PlayerSettings.productName = originalProductName;
				return;
			}
		}
		
		if (settings.autoRunPlayer)
		{
			buildOptions |= BuildOptions.AutoRunPlayer;
		}
		
		buildPlayerOptions.options = buildOptions;
		
		Debug.Log($"Building for {settings.selectedPlatform} to: {buildPath}");
		Debug.Log($"Scenes to build ({scenesToBuild.Length}): {string.Join(", ", scenesToBuild.Select(s => Path.GetFileNameWithoutExtension(s)))}");

		if (zipToDesktop)
			EditorUtility.DisplayProgressBar("Build & Zip to Desktop", "Building project...", 0.15f);
		
		BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
		BuildSummary summary = report.summary;
		
		// Restore original product name after build
		if (settings.selectedPlatform == BuildPlatform.Android || settings.selectedPlatform == BuildPlatform.AAB)
		{
			PlayerSettings.productName = originalProductName;
		}
		
		if (summary.result == BuildResult.Succeeded)
		{
			Debug.Log($"Build succeeded: {summary.outputPath}");

			if (zipToDesktop)
			{
				EditorUtility.DisplayProgressBar("Build & Zip to Desktop", "Creating ZIP archive...", 0.85f);
				try
				{
					string zipPath = ZipBuildToDesktop(appName, zipSource, zipIsDirectory);
					EditorUtility.ClearProgressBar();
					if (EditorUtility.DisplayDialog("Build & Zip Complete",
						$"Build and zip completed!\n\nZIP saved to:\n{zipPath}",
						"Open Desktop", "Close"))
					{
						string desktopPath = Path.GetDirectoryName(zipPath);
						if (Directory.Exists(desktopPath))
							Process.Start(desktopPath);
					}
				}
				catch (Exception zipEx)
				{
					EditorUtility.ClearProgressBar();
					EditorUtility.DisplayDialog("Zip Failed",
						$"Build succeeded but zipping failed:\n\n{zipEx.Message}", "OK");
				}
			}
			else
			{
			// For AAB, provide Google Play specific information
			if (settings.selectedPlatform == BuildPlatform.AAB)
			{
				string message = $"AAB build completed successfully!\n\nPath: {summary.outputPath}\n";
				message += $"Version: {PlayerSettings.bundleVersion} (Code: {PlayerSettings.Android.bundleVersionCode})\n\n";
				if (settings.isDebugBuild)
				{
					message += "WARNING: This is a development/debug build. Google Play requires release builds for production uploads.\n\n";
				}
				else
				{
					message += "This build is ready for upload to Google Play Console.\n\n";
				}
				message += "Open build folder?";
				
				if (!settings.autoRunPlayer)
				{
					if (EditorUtility.DisplayDialog("AAB Build Complete", message, "Yes", "No"))
					{
						string folderToOpen = Path.GetDirectoryName(summary.outputPath);
						if (Directory.Exists(folderToOpen))
						{
							Process.Start(folderToOpen);
						}
					}
				}
			}
			// For Android APK, provide additional information
			else if (settings.selectedPlatform == BuildPlatform.Android)
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
			} // end else (!zipToDesktop)
		}
		else if (summary.result == BuildResult.Failed)
		{
			Debug.LogError("Build failed");
		}
	}

	string ZipBuildToDesktop(string appName, string source, bool isDirectory)
	{
		string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
		string zipFileName = $"{appName}_{settings.selectedPlatform}.zip";
		string zipFilePath = Path.Combine(desktopPath, zipFileName);

		if (File.Exists(zipFilePath))
			File.Delete(zipFilePath);

		if (isDirectory)
		{
			ZipFile.CreateFromDirectory(source, zipFilePath, System.IO.Compression.CompressionLevel.Optimal, false);
		}
		else
		{
			using (var archive = ZipFile.Open(zipFilePath, ZipArchiveMode.Create))
			{
				archive.CreateEntryFromFile(source, Path.GetFileName(source), System.IO.Compression.CompressionLevel.Optimal);
			}
		}

		return zipFilePath;
	}
}
