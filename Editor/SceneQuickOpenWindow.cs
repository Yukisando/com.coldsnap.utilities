using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
#if UNITY_6000_0_OR_NEWER
using UnityEditor.Toolbars;
#endif
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public class SceneQuickOpenWindow : EditorWindow
{
    private string searchQuery = string.Empty;
    private Vector2 scrollPosition;
    private bool focusSearchField = true;

    [MenuItem("ColdSnap/Scenes/Quick Open %#l", false, 0)]
    private static void ShowWindow()
    {
        var window = GetWindow<SceneQuickOpenWindow>("Scene Quick Open");
        window.minSize = new Vector2(560f, 340f);
        window.Show();
    }

    private void OnGUI()
    {
        SceneQuickOpenGui.Draw(ref searchQuery, ref scrollPosition, ref focusSearchField, true);
    }
}

internal static class SceneQuickOpenGui
{
    private const string SearchFieldControlName = "SceneQuickOpenSearchField";

    public static void Draw(ref string searchQuery, ref Vector2 scrollPosition, ref bool focusSearchField, bool showRefreshButton)
    {
        DrawToolbar(ref searchQuery, ref focusSearchField, showRefreshButton);
        EditorGUILayout.Space();

        List<SceneQuickOpenEntry> scenes = SceneQuickOpenService.GetScenes(searchQuery);
        HandleKeyboardActions(scenes);

        if (scenes.Count == 0)
        {
            string message = string.IsNullOrWhiteSpace(searchQuery)
                ? "No scenes were found under Assets."
                : "No scenes match the current search.";
            EditorGUILayout.HelpBox(message, MessageType.Info);
            return;
        }

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        foreach (SceneQuickOpenEntry scene in scenes)
        {
            DrawSceneRow(scene);
        }
        EditorGUILayout.EndScrollView();
    }

    private static void DrawToolbar(ref string searchQuery, ref bool focusSearchField, bool showRefreshButton)
    {
        EditorGUILayout.BeginHorizontal();

        GUI.SetNextControlName(SearchFieldControlName);
        searchQuery = EditorGUILayout.TextField("Search", searchQuery);

        if (focusSearchField && Event.current.type == EventType.Repaint)
        {
            GUI.FocusControl(SearchFieldControlName);
            EditorGUI.FocusTextInControl(SearchFieldControlName);
            focusSearchField = false;
        }

        if (showRefreshButton && GUILayout.Button("Refresh", GUILayout.Width(80f)))
        {
            SceneQuickOpenService.RefreshScenes();
        }

        EditorGUILayout.EndHorizontal();
    }

    private static void DrawSceneRow(SceneQuickOpenEntry scene)
    {
        bool isActiveScene = SceneManager.GetActiveScene().path == scene.Path;
        bool isLoaded = SceneManager.GetSceneByPath(scene.Path).isLoaded;
        bool canRemove = isLoaded && SceneQuickOpenService.CanRemoveScene(scene.Path);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();

        GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel);
        if (isActiveScene)
        {
            titleStyle.normal.textColor = new Color(0.2f, 0.55f, 0.95f);
        }

        EditorGUILayout.LabelField(scene.Name, titleStyle);
        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Open", GUILayout.Width(74f)))
        {
            SceneQuickOpenService.OpenSingle(scene.Path);
        }

        using (new EditorGUI.DisabledScope(isLoaded && !canRemove))
        {
            if (GUILayout.Button(isLoaded ? "-" : "+", GUILayout.Width(26f)))
            {
                if (isLoaded)
                {
                    SceneQuickOpenService.RemoveScene(scene.Path);
                }
                else
                {
                    SceneQuickOpenService.OpenAdditive(scene.Path);
                }
            }
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.LabelField(scene.Path, EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();
    }

    private static void HandleKeyboardActions(List<SceneQuickOpenEntry> scenes)
    {
        Event currentEvent = Event.current;
        if (currentEvent.type != EventType.KeyDown)
        {
            return;
        }

        if (GUI.GetNameOfFocusedControl() != SearchFieldControlName)
        {
            return;
        }

        if (currentEvent.keyCode != KeyCode.Return && currentEvent.keyCode != KeyCode.KeypadEnter)
        {
            return;
        }

        if (scenes.Count > 0)
        {
            SceneQuickOpenService.OpenSingle(scenes[0].Path);
        }

        currentEvent.Use();
    }
}

internal sealed class SceneQuickOpenPopupContent : PopupWindowContent
{
    private string searchQuery = string.Empty;
    private Vector2 scrollPosition;
    private bool focusSearchField = true;

    public override Vector2 GetWindowSize()
    {
        return new Vector2(560f, 420f);
    }

    public override void OnGUI(Rect rect)
    {
        GUILayout.Space(4f);
        SceneQuickOpenGui.Draw(ref searchQuery, ref scrollPosition, ref focusSearchField, true);
    }
}

[InitializeOnLoad]
internal static class SceneQuickOpenToolbar
{
#if UNITY_6000_0_OR_NEWER
    private const string ToolbarElementPath = "ColdSnap/Scenes/Quick Open";
#else
    private const string ToolbarTypeName = "UnityEditor.Toolbar";
    private const string RootFieldName = "m_Root";
    private const string RightZoneName = "ToolbarZoneRightAlign";
    private const string ContainerName = "ColdSnapSceneQuickOpenToolbar";

    private static IMGUIContainer toolbarContainer;
#endif

    static SceneQuickOpenToolbar()
    {
#if UNITY_6000_0_OR_NEWER
        EditorApplication.projectChanged += RefreshToolbar;
        EditorBuildSettings.sceneListChanged += RefreshToolbar;
        EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
#else
        EditorApplication.update += TryAttachToToolbar;
#endif
    }

#if UNITY_6000_0_OR_NEWER
    [MainToolbarElement(ToolbarElementPath, defaultDockPosition = MainToolbarDockPosition.Right)]
    public static MainToolbarElement CreateMainToolbarDropdown()
    {
        Texture2D icon = EditorGUIUtility.IconContent("SceneAsset Icon").image as Texture2D;
        MainToolbarContent content = new MainToolbarContent("Scenes", icon, "Open or add scenes quickly.");
        return new MainToolbarDropdown(content, ShowMainToolbarDropdown);
    }

    private static void ShowMainToolbarDropdown(Rect dropDownRect)
    {
        UnityEditor.PopupWindow.Show(dropDownRect, new SceneQuickOpenPopupContent());
    }

    private static void RefreshToolbar()
    {
        MainToolbar.Refresh(ToolbarElementPath);
    }

    private static void OnActiveSceneChanged(Scene previousScene, Scene nextScene)
    {
        RefreshToolbar();
    }
#else

    private static void TryAttachToToolbar()
    {
        Type toolbarType = typeof(Editor).Assembly.GetType(ToolbarTypeName);
        if (toolbarType == null)
        {
            return;
        }

        UnityEngine.Object[] toolbars = Resources.FindObjectsOfTypeAll(toolbarType);
        if (toolbars.Length == 0)
        {
            return;
        }

        ScriptableObject toolbar = toolbars[0] as ScriptableObject;
        if (toolbar == null)
        {
            return;
        }

        FieldInfo rootField = toolbarType.GetField(RootFieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (rootField == null)
        {
            return;
        }

        VisualElement root = rootField.GetValue(toolbar) as VisualElement;
        if (root == null)
        {
            return;
        }

        VisualElement rightZone = root.Q(RightZoneName);
        if (rightZone == null)
        {
            return;
        }

        IMGUIContainer existingContainer = rightZone.Q<IMGUIContainer>(ContainerName);
        if (existingContainer != null)
        {
            toolbarContainer = existingContainer;
            return;
        }

        toolbarContainer = new IMGUIContainer(DrawToolbarButton)
        {
            name = ContainerName
        };
        toolbarContainer.style.flexShrink = 0;
        toolbarContainer.style.marginLeft = 6f;
        rightZone.Add(toolbarContainer);
    }

    private static void DrawToolbarButton()
    {
        GUIContent content = new GUIContent("Scenes", EditorGUIUtility.IconContent("SceneAsset Icon").image, "Open or add scenes quickly.");

        if (!GUILayout.Button(content, EditorStyles.toolbarDropDown, GUILayout.Width(92f)))
        {
            return;
        }

        Rect buttonRect = GUILayoutUtility.GetLastRect();
        UnityEditor.PopupWindow.Show(GUIUtility.GUIToScreenRect(buttonRect), new SceneQuickOpenPopupContent());
    }
#endif
}

internal static class SceneQuickOpenService
{
    private static readonly List<SceneQuickOpenEntry> CachedScenes = new List<SceneQuickOpenEntry>();
    private static bool isCacheValid;

    static SceneQuickOpenService()
    {
        EditorBuildSettings.sceneListChanged += RefreshScenes;
        EditorApplication.projectChanged += RefreshScenes;
    }

    public static List<SceneQuickOpenEntry> GetScenes(string searchQuery)
    {
        EnsureCache();

        if (string.IsNullOrWhiteSpace(searchQuery))
        {
            return CachedScenes.ToList();
        }

        return CachedScenes
            .Where(scene =>
                scene.Name.IndexOf(searchQuery, StringComparison.OrdinalIgnoreCase) >= 0 ||
                scene.Path.IndexOf(searchQuery, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();
    }

    public static void RefreshScenes()
    {
        isCacheValid = false;
    }

    public static void OpenSingle(string scenePath)
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        PingSceneAsset(scenePath);
    }

    public static void OpenAdditive(string scenePath)
    {
        if (SceneManager.GetSceneByPath(scenePath).isLoaded)
        {
            return;
        }

        EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        PingSceneAsset(scenePath);
    }

    public static bool CanRemoveScene(string scenePath)
    {
        Scene scene = SceneManager.GetSceneByPath(scenePath);
        if (!scene.isLoaded)
        {
            return false;
        }

        return SceneManager.sceneCount > 1;
    }

    public static void RemoveScene(string scenePath)
    {
        Scene scene = SceneManager.GetSceneByPath(scenePath);
        if (!scene.isLoaded || !CanRemoveScene(scenePath))
        {
            return;
        }

        EditorSceneManager.CloseScene(scene, true);
    }

    private static void EnsureCache()
    {
        if (isCacheValid)
        {
            return;
        }

        CachedScenes.Clear();

        string[] sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });
        foreach (string guid in sceneGuids)
        {
            string scenePath = AssetDatabase.GUIDToAssetPath(guid);
            CachedScenes.Add(new SceneQuickOpenEntry(scenePath, Path.GetFileNameWithoutExtension(scenePath)));
        }

        CachedScenes.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
        isCacheValid = true;
    }

    private static void PingSceneAsset(string scenePath)
    {
        SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
        if (sceneAsset == null)
        {
            return;
        }

        Selection.activeObject = sceneAsset;
        EditorGUIUtility.PingObject(sceneAsset);
    }
}

internal sealed class SceneQuickOpenEntry
{
    public SceneQuickOpenEntry(string path, string name)
    {
        Path = path;
        Name = name;
    }

    public string Path { get; }
    public string Name { get; }
}