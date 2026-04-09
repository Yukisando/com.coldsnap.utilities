using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public class SceneQuickOpenWindow : EditorWindow
{
    private string searchQuery = string.Empty;
    private Vector2 scrollPosition;

    [MenuItem("ColdSnap/Scenes/Quick Open %#l", false, 0)]
    private static void ShowWindow()
    {
        var window = GetWindow<SceneQuickOpenWindow>("Scene Quick Open");
        window.minSize = new Vector2(560f, 340f);
        window.Show();
    }

    private void OnGUI()
    {
        SceneQuickOpenGui.Draw(ref searchQuery, ref scrollPosition, true);
    }
}

internal static class SceneQuickOpenGui
{
    public static void Draw(ref string searchQuery, ref Vector2 scrollPosition, bool showRefreshButton)
    {
        DrawToolbar(ref searchQuery, showRefreshButton);
        EditorGUILayout.Space();

        List<SceneQuickOpenEntry> scenes = SceneQuickOpenService.GetScenes(searchQuery);
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

    private static void DrawToolbar(ref string searchQuery, bool showRefreshButton)
    {
        EditorGUILayout.BeginHorizontal();
        searchQuery = EditorGUILayout.TextField("Search", searchQuery);

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

        using (new EditorGUI.DisabledScope(isLoaded))
        {
            if (GUILayout.Button("+", GUILayout.Width(26f)))
            {
                SceneQuickOpenService.OpenAdditive(scene.Path);
            }
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.LabelField(scene.Path, EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();
    }
}

internal sealed class SceneQuickOpenPopupContent : PopupWindowContent
{
    private string searchQuery = string.Empty;
    private Vector2 scrollPosition;

    public override Vector2 GetWindowSize()
    {
        return new Vector2(560f, 420f);
    }

    public override void OnGUI(Rect rect)
    {
        GUILayout.Space(4f);
        SceneQuickOpenGui.Draw(ref searchQuery, ref scrollPosition, true);
    }
}

[InitializeOnLoad]
internal static class SceneQuickOpenToolbar
{
    private const string ToolbarTypeName = "UnityEditor.Toolbar";
    private const string RootFieldName = "m_Root";
    private const string RightZoneName = "ToolbarZoneRightAlign";
    private const string ContainerName = "ColdSnapSceneQuickOpenToolbar";

    private static ScriptableObject toolbarInstance;
    private static IMGUIContainer toolbarContainer;

    static SceneQuickOpenToolbar()
    {
        EditorApplication.update += TryAttachToToolbar;
    }

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
            toolbarInstance = toolbar;
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
        toolbarInstance = toolbar;
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