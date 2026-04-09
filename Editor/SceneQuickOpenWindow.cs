using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneQuickOpenWindow : EditorWindow
{
    private readonly List<SceneEntry> scenes = new List<SceneEntry>();
    private Vector2 scrollPosition;
    private string searchQuery = string.Empty;

    [MenuItem("ColdSnap/Scenes/Quick Open %#l", false, 0)]
    private static void ShowWindow()
    {
        var window = GetWindow<SceneQuickOpenWindow>("Scene Quick Open");
        window.minSize = new Vector2(520f, 320f);
        window.Show();
    }

    private void OnEnable()
    {
        RefreshScenes();
    }

    private void OnGUI()
    {
        DrawToolbar();
        EditorGUILayout.Space();

        if (scenes.Count == 0)
        {
            EditorGUILayout.HelpBox("No scenes were found under Assets.", MessageType.Info);
            return;
        }

        var filteredScenes = string.IsNullOrWhiteSpace(searchQuery)
            ? scenes
            : scenes.Where(scene =>
                scene.Name.IndexOf(searchQuery, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                scene.Path.IndexOf(searchQuery, System.StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

        if (filteredScenes.Count == 0)
        {
            EditorGUILayout.HelpBox("No scenes match the current search.", MessageType.Info);
            return;
        }

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        foreach (var scene in filteredScenes)
        {
            DrawSceneRow(scene);
        }
        EditorGUILayout.EndScrollView();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal();
        searchQuery = EditorGUILayout.TextField("Search", searchQuery);

        if (GUILayout.Button("Refresh", GUILayout.Width(80f)))
        {
            RefreshScenes();
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawSceneRow(SceneEntry scene)
    {
        bool isActiveScene = SceneManager.GetActiveScene().path == scene.Path;

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();

        var sceneLabelStyle = new GUIStyle(EditorStyles.boldLabel);
        if (isActiveScene)
        {
            sceneLabelStyle.normal.textColor = new Color(0.2f, 0.55f, 0.95f);
        }

        EditorGUILayout.LabelField(scene.Name, sceneLabelStyle);
        GUILayout.FlexibleSpace();

        using (new EditorGUI.DisabledScope(isActiveScene))
        {
            if (GUILayout.Button("Open", GUILayout.Width(70f)))
            {
                OpenScene(scene.Path);
            }
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.LabelField(scene.Path, EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();
    }

    private void RefreshScenes()
    {
        scenes.Clear();

        string[] sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });
        foreach (string guid in sceneGuids)
        {
            string scenePath = AssetDatabase.GUIDToAssetPath(guid);
            scenes.Add(new SceneEntry(scenePath, Path.GetFileNameWithoutExtension(scenePath)));
        }

        scenes.Sort((left, right) => string.Compare(left.Name, right.Name, System.StringComparison.OrdinalIgnoreCase));
    }

    private static void OpenScene(string scenePath)
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
        if (sceneAsset != null)
        {
            Selection.activeObject = sceneAsset;
            EditorGUIUtility.PingObject(sceneAsset);
        }
    }

    private sealed class SceneEntry
    {
        public SceneEntry(string path, string name)
        {
            Path = path;
            Name = name;
        }

        public string Path { get; }
        public string Name { get; }
    }
}