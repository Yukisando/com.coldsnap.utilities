using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class TMPFontSceneReplacerWindow : EditorWindow
{
    private const string WindowTitle = "TMP Font Replacer";

    private readonly List<SceneSelectionItem> scenes = new List<SceneSelectionItem>();
    private readonly List<PrefabSelectionItem> prefabs = new List<PrefabSelectionItem>();
    private Vector2 scrollPosition;
    private string searchQuery = string.Empty;
    private TMP_FontAsset replacementFont;
    private int previewCount;
    private int replacedCount;
    private int processedSceneCount;
    private int processedPrefabCount;
    private string lastResultMessage = string.Empty;
    private bool previewIsStale = true;

    [MenuItem("ColdSnap/Tools/TMP Font Replacer")]
    private static void ShowWindow()
    {
        TMPFontSceneReplacerWindow window = GetWindow<TMPFontSceneReplacerWindow>(WindowTitle);
        window.minSize = new Vector2(640f, 420f);
        window.Show();
    }

    private void OnEnable()
    {
        RefreshAssetLists();
    }

    private void OnGUI()
    {
        DrawHeader();
        EditorGUILayout.Space();
        DrawSceneToolbar();
        EditorGUILayout.Space();
        DrawAssetLists();
        EditorGUILayout.Space();
        DrawFooter();
    }

    private void DrawHeader()
    {
        EditorGUILayout.LabelField(WindowTitle, EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Select one replacement TMP font asset, then choose the scenes and prefabs to update.", MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginChangeCheck();
        replacementFont = (TMP_FontAsset)EditorGUILayout.ObjectField("Replacement Font", replacementFont, typeof(TMP_FontAsset), false);
        if (EditorGUI.EndChangeCheck())
        {
            previewIsStale = true;
        }
        if (GUILayout.Button("Refresh Assets", GUILayout.Width(120f)))
        {
            RefreshAssetLists();
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawSceneToolbar()
    {
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Select All", GUILayout.Width(100f)))
        {
            SetAllSceneSelections(true);
        }

        if (GUILayout.Button("Select None", GUILayout.Width(100f)))
        {
            SetAllSceneSelections(false);
        }

        GUILayout.Space(10f);
        GUILayout.Label("Search", GUILayout.Width(48f));
        searchQuery = EditorGUILayout.TextField(searchQuery);

        EditorGUILayout.EndHorizontal();
    }

    private void DrawAssetLists()
    {
        List<SceneSelectionItem> visibleScenes = GetVisibleScenes();
        List<PrefabSelectionItem> visiblePrefabs = GetVisiblePrefabs();

        if (visibleScenes.Count == 0 && visiblePrefabs.Count == 0)
        {
            EditorGUILayout.HelpBox("No scenes or prefabs were found for the current filter.", MessageType.None);
            return;
        }

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        EditorGUILayout.LabelField("Scenes", EditorStyles.boldLabel);
        if (visibleScenes.Count == 0)
        {
            EditorGUILayout.HelpBox("No scenes match the current filter.", MessageType.None);
        }
        else
        {
            foreach (SceneSelectionItem scene in visibleScenes)
            {
                DrawSceneRow(scene);
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Prefabs", EditorStyles.boldLabel);
        if (visiblePrefabs.Count == 0)
        {
            EditorGUILayout.HelpBox("No prefabs match the current filter.", MessageType.None);
        }
        else
        {
            foreach (PrefabSelectionItem prefab in visiblePrefabs)
            {
                DrawPrefabRow(prefab);
            }
        }
        EditorGUILayout.EndScrollView();
    }

    private void DrawSceneRow(SceneSelectionItem scene)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();

        EditorGUI.BeginChangeCheck();
        scene.IsSelected = EditorGUILayout.Toggle(scene.IsSelected, GUILayout.Width(18f));
        if (EditorGUI.EndChangeCheck())
        {
            previewIsStale = true;
        }
        EditorGUILayout.BeginVertical();
        EditorGUILayout.LabelField(scene.Name, EditorStyles.boldLabel);
        EditorGUILayout.LabelField(scene.ScenePath, EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();

        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField(scene.IsAlreadyOpen ? "Open" : string.Empty, GUILayout.Width(40f));

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    private void DrawPrefabRow(PrefabSelectionItem prefab)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();

        EditorGUI.BeginChangeCheck();
        prefab.IsSelected = EditorGUILayout.Toggle(prefab.IsSelected, GUILayout.Width(18f));
        if (EditorGUI.EndChangeCheck())
        {
            previewIsStale = true;
        }
        EditorGUILayout.BeginVertical();
        EditorGUILayout.LabelField(prefab.Name, EditorStyles.boldLabel);
        EditorGUILayout.LabelField(prefab.AssetPath, EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();

        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField(prefab.HasTMPComponents ? "TMP" : string.Empty, GUILayout.Width(40f));

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    private void DrawFooter()
    {
        int selectedScenes = scenes.Count(scene => scene.IsSelected);
        int selectedPrefabs = prefabs.Count(prefab => prefab.IsSelected);
        bool canPreview = replacementFont != null && (selectedScenes > 0 || selectedPrefabs > 0);
        bool canRun = canPreview && previewCount > 0 && !previewIsStale;

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"{selectedScenes} scene(s), {selectedPrefabs} prefab(s) selected", EditorStyles.miniLabel);
        GUILayout.FlexibleSpace();

        using (new EditorGUI.DisabledScope(!canPreview))
        {
            if (GUILayout.Button("Preview Changes", GUILayout.Height(28f), GUILayout.Width(140f)))
            {
                PreviewReplacement();
            }
        }

        using (new EditorGUI.DisabledScope(!canRun))
        {
            string buttonLabel = previewIsStale ? "Replace Fonts" : $"Replace Fonts ({previewCount})";
            if (GUILayout.Button(buttonLabel, GUILayout.Height(28f), GUILayout.Width(220f)))
            {
                RunReplacement();
            }
        }

        EditorGUILayout.EndHorizontal();

        if (!string.IsNullOrWhiteSpace(lastResultMessage))
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(lastResultMessage, MessageType.None);
        }
    }

    private void PreviewReplacement()
    {
        if (!TryBuildSelectionPreview(out int totalCount, out int sceneCount, out int prefabCount))
        {
            return;
        }

        previewCount = totalCount;
        processedSceneCount = sceneCount;
        processedPrefabCount = prefabCount;
        previewIsStale = false;
        lastResultMessage = $"Preview: {previewCount} TMP component(s) will be updated across {processedSceneCount} scene(s) and {processedPrefabCount} prefab(s).";
    }

    private void RunReplacement()
    {
        if (replacementFont == null)
        {
            lastResultMessage = "Select a replacement font first.";
            return;
        }

        List<SceneSelectionItem> selectedScenes = scenes.Where(scene => scene.IsSelected).ToList();
        if (selectedScenes.Count == 0)
        {
            lastResultMessage = "Select at least one scene.";
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            lastResultMessage = "Operation canceled.";
            return;
        }

        if (previewIsStale && !TryBuildSelectionPreview(out previewCount, out processedSceneCount, out processedPrefabCount))
        {
            return;
        }

        int totalReplaced = 0;
        int scenesProcessed = 0;
        int prefabsProcessed = 0;

        try
        {
            foreach (SceneSelectionItem item in selectedScenes)
            {
                totalReplaced += ReplaceFontsInScene(item.ScenePath, out bool processedScene);
                if (processedScene)
                {
                    scenesProcessed++;
                }
            }

            foreach (PrefabSelectionItem item in prefabs.Where(prefab => prefab.IsSelected).ToList())
            {
                totalReplaced += ReplaceFontsInPrefab(item.AssetPath, out bool processedPrefab);
                if (processedPrefab)
                {
                    prefabsProcessed++;
                }
            }

            replacedCount = totalReplaced;
            processedSceneCount = scenesProcessed;
            processedPrefabCount = prefabsProcessed;
            previewCount = replacedCount;
            previewIsStale = false;
            lastResultMessage = $"Updated {replacedCount} TMP component(s) across {processedSceneCount} scene(s) and {processedPrefabCount} prefab(s).";
        }
        catch (Exception exception)
        {
            lastResultMessage = $"Replacement failed: {exception.Message}";
            Debug.LogException(exception);
        }
    }

    private int ReplaceFontsInScene(string scenePath, out bool processedScene)
    {
        processedScene = false;
        if (string.IsNullOrWhiteSpace(scenePath) || AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
        {
            return 0;
        }

        Scene scene = EditorSceneManager.GetSceneByPath(scenePath);
        bool wasAlreadyOpen = scene.isLoaded;

        if (!wasAlreadyOpen)
        {
            scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        }

        if (!scene.IsValid())
        {
            if (!wasAlreadyOpen && scene.isLoaded)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
            return 0;
        }

        int replaced = 0;
        GameObject[] roots = scene.GetRootGameObjects();
        foreach (GameObject root in roots)
        {
            TMP_Text[] tmpTexts = root.GetComponentsInChildren<TMP_Text>(true);
            foreach (TMP_Text tmpText in tmpTexts)
            {
                if (tmpText == null || tmpText.font == replacementFont)
                {
                    continue;
                }

                Undo.RecordObject(tmpText, "Replace TMP Font");
                tmpText.font = replacementFont;
                EditorUtility.SetDirty(tmpText);
                replaced++;
            }
        }

        if (replaced > 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            processedScene = true;
        }

        if (!wasAlreadyOpen)
        {
            EditorSceneManager.CloseScene(scene, true);
        }

        return replaced;
    }

    private int ReplaceFontsInPrefab(string prefabPath, out bool processedPrefab)
    {
        processedPrefab = false;
        if (string.IsNullOrWhiteSpace(prefabPath) || AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
        {
            return 0;
        }

        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
        int replaced = 0;

        try
        {
            TMP_Text[] tmpTexts = prefabRoot.GetComponentsInChildren<TMP_Text>(true);
            foreach (TMP_Text tmpText in tmpTexts)
            {
                if (tmpText == null || tmpText.font == replacementFont)
                {
                    continue;
                }

                Undo.RecordObject(tmpText, "Replace TMP Font");
                tmpText.font = replacementFont;
                EditorUtility.SetDirty(tmpText);
                replaced++;
            }

            if (replaced > 0)
            {
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                processedPrefab = true;
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }

        return replaced;
    }

    private void RefreshAssetLists()
    {
        string[] sceneGuids = AssetDatabase.FindAssets("t:Scene");
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");
        scenes.Clear();
        prefabs.Clear();

        foreach (string guid in sceneGuids)
        {
            string scenePath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrWhiteSpace(scenePath))
            {
                continue;
            }

            scenes.Add(new SceneSelectionItem(scenePath));
        }

        foreach (string guid in prefabGuids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                continue;
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null || prefab.GetComponentInChildren<TMP_Text>(true) == null)
            {
                continue;
            }

            prefabs.Add(new PrefabSelectionItem(assetPath));
        }

        scenes.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
        prefabs.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
        previewIsStale = true;
        Repaint();
    }

    private List<SceneSelectionItem> GetVisibleScenes()
    {
        if (string.IsNullOrWhiteSpace(searchQuery))
        {
            return scenes;
        }

        return scenes
            .Where(scene =>
                scene.Name.IndexOf(searchQuery, StringComparison.OrdinalIgnoreCase) >= 0 ||
                scene.ScenePath.IndexOf(searchQuery, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();
    }

    private void SetAllSceneSelections(bool isSelected)
    {
        foreach (SceneSelectionItem scene in scenes)
        {
            scene.IsSelected = isSelected;
        }

        foreach (PrefabSelectionItem prefab in prefabs)
        {
            prefab.IsSelected = isSelected;
        }

        previewIsStale = true;
    }

    private void SetAllPrefabSelections(bool isSelected)
    {
        foreach (PrefabSelectionItem prefab in prefabs)
        {
            prefab.IsSelected = isSelected;
        }

        previewIsStale = true;
    }

    private bool TryBuildSelectionPreview(out int totalCount, out int sceneCount, out int prefabCount)
    {
        totalCount = 0;
        sceneCount = 0;
        prefabCount = 0;

        if (replacementFont == null)
        {
            lastResultMessage = "Select a replacement font first.";
            return false;
        }

        List<SceneSelectionItem> selectedScenes = scenes.Where(scene => scene.IsSelected).ToList();
        List<PrefabSelectionItem> selectedPrefabs = prefabs.Where(prefab => prefab.IsSelected).ToList();

        if (selectedScenes.Count == 0 && selectedPrefabs.Count == 0)
        {
            lastResultMessage = "Select at least one scene or prefab.";
            return false;
        }

        int counted = 0;
        foreach (SceneSelectionItem item in selectedScenes)
        {
            counted += CountFontsInScene(item.ScenePath);
        }

        foreach (PrefabSelectionItem item in selectedPrefabs)
        {
            counted += CountFontsInPrefab(item.AssetPath);
        }

        totalCount = counted;
        sceneCount = selectedScenes.Count;
        prefabCount = selectedPrefabs.Count;
        return true;
    }

    private int CountFontsInScene(string scenePath)
    {
        if (string.IsNullOrWhiteSpace(scenePath) || AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
        {
            return 0;
        }

        Scene scene = EditorSceneManager.GetSceneByPath(scenePath);
        bool wasAlreadyOpen = scene.isLoaded;

        if (!wasAlreadyOpen)
        {
            scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        }

        if (!scene.IsValid())
        {
            if (!wasAlreadyOpen && scene.isLoaded)
            {
                EditorSceneManager.CloseScene(scene, true);
            }

            return 0;
        }

        int count = 0;
        try
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                count += root.GetComponentsInChildren<TMP_Text>(true).Count(tmpText => tmpText != null && tmpText.font != replacementFont);
            }
        }
        finally
        {
            if (!wasAlreadyOpen)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        return count;
    }

    private int CountFontsInPrefab(string prefabPath)
    {
        if (string.IsNullOrWhiteSpace(prefabPath) || AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
        {
            return 0;
        }

        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            return prefabRoot.GetComponentsInChildren<TMP_Text>(true).Count(tmpText => tmpText != null && tmpText.font != replacementFont);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private sealed class SceneSelectionItem
    {
        public SceneSelectionItem(string path)
        {
            ScenePath = path;
            Name = System.IO.Path.GetFileNameWithoutExtension(path);
        }

        public string ScenePath { get; }
        public string Name { get; }
        public bool IsSelected { get; set; }
        public bool IsAlreadyOpen => EditorSceneManager.GetSceneByPath(ScenePath).isLoaded;
    }

    private sealed class PrefabSelectionItem
    {
        public PrefabSelectionItem(string path)
        {
            AssetPath = path;
            Name = System.IO.Path.GetFileNameWithoutExtension(path);
        }

        public string AssetPath { get; }
        public string Name { get; }
        public bool IsSelected { get; set; }
        public bool HasTMPComponents => AssetDatabase.LoadAssetAtPath<GameObject>(AssetPath)?.GetComponentInChildren<TMP_Text>(true) != null;
    }
}
