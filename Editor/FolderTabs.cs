using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Lets you pin folders from the Project window ("Add Folder Tab") and browse their contents
/// from a dockable "Folder Tabs" window — drag that window's tab next to the Project window's
/// tab to get a "Prefabs" tab, a "Materials" tab, etc., each showing just that folder's assets.
/// </summary>
public class FolderTabsWindow : EditorWindow
{
    [SerializeField] private int activeTabIndex = -1;
    [SerializeField] private string contentSearch = string.Empty;
    private Vector2 tabScrollPosition;
    private Vector2 contentScrollPosition;

    [MenuItem("ColdSnap/Project/Folder Tabs")]
    public static void FocusOrOpen()
    {
        var window = GetWindow<FolderTabsWindow>("Folder Tabs");
        window.Show();
    }

    private void OnEnable()
    {
        FolderTabsService.Changed += Repaint;
    }

    private void OnDisable()
    {
        FolderTabsService.Changed -= Repaint;
    }

    private void OnGUI()
    {
        IReadOnlyList<FolderTabEntry> tabs = FolderTabsService.Tabs;
        ClampActiveIndex(tabs.Count);

        if (tabs.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "No folder tabs yet. Right-click a folder in the Project window and choose \"Add Folder Tab\".",
                MessageType.Info);
            return;
        }

        DrawTabStrip(tabs);
        DrawContent(tabs[activeTabIndex]);
    }

    private void ClampActiveIndex(int count)
    {
        if (count == 0)
        {
            activeTabIndex = -1;
        }
        else if (activeTabIndex < 0 || activeTabIndex >= count)
        {
            activeTabIndex = 0;
        }
    }

    private void DrawTabStrip(IReadOnlyList<FolderTabEntry> tabs)
    {
        tabScrollPosition = EditorGUILayout.BeginScrollView(
            tabScrollPosition, GUIStyle.none, GUIStyle.none, GUILayout.Height(24f));
        EditorGUILayout.BeginHorizontal();

        for (int i = 0; i < tabs.Count; i++)
        {
            FolderTabEntry entry = tabs[i];
            string path = AssetDatabase.GUIDToAssetPath(entry.Guid);
            bool missing = string.IsNullOrEmpty(path);
            string label = string.IsNullOrEmpty(entry.Label)
                ? (missing ? "(missing)" : System.IO.Path.GetFileName(path))
                : entry.Label;
            var content = new GUIContent(label, EditorGUIUtility.IconContent("Folder Icon").image);

            using (new EditorGUI.DisabledScope(missing))
            {
                bool isActive = i == activeTabIndex;
                bool pressed = GUILayout.Toggle(
                    isActive, content, EditorStyles.toolbarButton, GUILayout.Height(22f), GUILayout.MinWidth(60f));
                if (pressed && !isActive)
                {
                    activeTabIndex = i;
                    contentScrollPosition = Vector2.zero;
                }
            }

            HandleTabContextMenu(entry, i, tabs.Count);
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndScrollView();
    }

    private void DrawContent(FolderTabEntry entry)
    {
        string folderPath = AssetDatabase.GUIDToAssetPath(entry.Guid);
        if (string.IsNullOrEmpty(folderPath))
        {
            EditorGUILayout.HelpBox("This folder no longer exists. Remove the tab or restore the folder.", MessageType.Warning);
            return;
        }

        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUI.SetNextControlName("FolderTabsSearchField");
        contentSearch = EditorGUILayout.TextField(contentSearch, EditorStyles.toolbarSearchField);
        EditorGUILayout.EndHorizontal();

        List<string> assetPaths = FolderTabsContentCache.GetContents(folderPath, contentSearch);
        if (assetPaths.Count == 0)
        {
            EditorGUILayout.HelpBox(
                string.IsNullOrEmpty(contentSearch) ? "This folder is empty." : "No assets match your search.",
                MessageType.Info);
            return;
        }

        contentScrollPosition = EditorGUILayout.BeginScrollView(contentScrollPosition);
        foreach (string assetPath in assetPaths)
        {
            DrawAssetRow(assetPath, folderPath);
        }
        EditorGUILayout.EndScrollView();
    }

    private static void DrawAssetRow(string assetPath, string tabFolderPath)
    {
        UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
        if (asset == null)
        {
            return;
        }

        Rect rowRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(20f));
        var content = new GUIContent(asset.name, AssetDatabase.GetCachedIcon(assetPath));
        GUILayout.Label(content, GUILayout.Height(18f));
        GUILayout.FlexibleSpace();

        string parentPath = System.IO.Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(parentPath) && parentPath != tabFolderPath)
        {
            string relative = parentPath.Length > tabFolderPath.Length
                ? parentPath.Substring(tabFolderPath.Length + 1)
                : parentPath;
            GUILayout.Label(relative, EditorStyles.miniLabel);
        }
        EditorGUILayout.EndHorizontal();

        Event evt = Event.current;
        if (evt.type == EventType.MouseDown && rowRect.Contains(evt.mousePosition))
        {
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);

            if (evt.clickCount == 2)
            {
                AssetDatabase.OpenAsset(asset);
            }

            evt.Use();
        }
    }

    private static void HandleTabContextMenu(FolderTabEntry entry, int index, int count)
    {
        Rect rect = GUILayoutUtility.GetLastRect();
        Event evt = Event.current;
        if (evt.type != EventType.ContextClick || !rect.Contains(evt.mousePosition))
        {
            return;
        }

        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("Rename Tab"), false, () => PopupWindow.Show(rect, new FolderTabRenamePopup(entry)));
        menu.AddItem(new GUIContent("Remove Tab"), false, () => FolderTabsService.RemoveTab(entry.Guid));

        if (index > 0)
        {
            menu.AddItem(new GUIContent("Move Left"), false, () => FolderTabsService.MoveTab(index, index - 1));
        }
        else
        {
            menu.AddDisabledItem(new GUIContent("Move Left"));
        }

        if (index < count - 1)
        {
            menu.AddItem(new GUIContent("Move Right"), false, () => FolderTabsService.MoveTab(index, index + 1));
        }
        else
        {
            menu.AddDisabledItem(new GUIContent("Move Right"));
        }

        menu.ShowAsContext();
        evt.Use();
    }
}

internal static class FolderTabsContentCache
{
    public static List<string> GetContents(string folderPath, string searchFilter)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
        {
            return result;
        }

        string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { folderPath });
        var seen = new HashSet<string>();

        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetDatabase.IsValidFolder(assetPath) || !seen.Add(assetPath))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(searchFilter) &&
                System.IO.Path.GetFileNameWithoutExtension(assetPath).IndexOf(searchFilter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            result.Add(assetPath);
        }

        result.Sort((a, b) => string.Compare(
            System.IO.Path.GetFileNameWithoutExtension(a),
            System.IO.Path.GetFileNameWithoutExtension(b),
            StringComparison.OrdinalIgnoreCase));
        return result;
    }
}

internal sealed class FolderTabRenamePopup : PopupWindowContent
{
    private readonly FolderTabEntry entry;
    private string input;
    private bool focused;

    public FolderTabRenamePopup(FolderTabEntry entry)
    {
        this.entry = entry;
        string path = AssetDatabase.GUIDToAssetPath(entry.Guid);
        input = string.IsNullOrEmpty(entry.Label) ? System.IO.Path.GetFileName(path) : entry.Label;
    }

    public override Vector2 GetWindowSize()
    {
        return new Vector2(220f, 60f);
    }

    public override void OnGUI(Rect rect)
    {
        GUI.SetNextControlName("FolderTabRenameField");
        input = EditorGUILayout.TextField(input);

        if (!focused && Event.current.type == EventType.Repaint)
        {
            EditorGUI.FocusTextInControl("FolderTabRenameField");
            focused = true;
        }

        Event evt = Event.current;
        bool confirmed = evt.type == EventType.KeyDown &&
                          (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Rename") || confirmed)
        {
            FolderTabsService.RenameTab(entry.Guid, input);
            editorWindow.Close();
        }
        if (GUILayout.Button("Cancel"))
        {
            editorWindow.Close();
        }
        EditorGUILayout.EndHorizontal();
    }
}

internal static class FolderTabsContextMenu
{
    private const string AddMenuPath = "Assets/Add Folder Tab";
    private const string RemoveMenuPath = "Assets/Remove Folder Tab";

    [MenuItem(AddMenuPath, false, 39)]
    private static void AddFolderTab()
    {
        foreach ((string guid, string name, string path) in GetSelectedFolders())
        {
            FolderTabsService.AddTab(guid, name);
        }

        FolderTabsWindow.FocusOrOpen();
    }

    [MenuItem(AddMenuPath, true)]
    private static bool ValidateAddFolderTab()
    {
        List<(string guid, string name, string path)> folders = GetSelectedFolders();
        return folders.Count > 0 && folders.Any(f => !FolderTabsService.IsPinned(f.guid));
    }

    [MenuItem(RemoveMenuPath, false, 40)]
    private static void RemoveFolderTab()
    {
        foreach ((string guid, string name, string path) in GetSelectedFolders())
        {
            FolderTabsService.RemoveTab(guid);
        }
    }

    [MenuItem(RemoveMenuPath, true)]
    private static bool ValidateRemoveFolderTab()
    {
        List<(string guid, string name, string path)> folders = GetSelectedFolders();
        return folders.Count > 0 && folders.Any(f => FolderTabsService.IsPinned(f.guid));
    }

    private static List<(string guid, string name, string path)> GetSelectedFolders()
    {
        var result = new List<(string, string, string)>();
        foreach (UnityEngine.Object obj in Selection.objects)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path) || !AssetDatabase.IsValidFolder(path))
            {
                continue;
            }

            string guid = AssetDatabase.AssetPathToGUID(path);
            result.Add((guid, System.IO.Path.GetFileName(path), path));
        }
        return result;
    }
}

internal static class FolderTabsService
{
    public static event Action Changed;

    public static IReadOnlyList<FolderTabEntry> Tabs => FolderTabsSettings.instance.Tabs;

    public static bool IsPinned(string guid)
    {
        return FolderTabsSettings.instance.Tabs.Any(t => t.Guid == guid);
    }

    public static void AddTab(string guid, string label)
    {
        if (IsPinned(guid))
        {
            return;
        }

        FolderTabsSettings.instance.Tabs.Add(new FolderTabEntry { Guid = guid, Label = label });
        Save();
    }

    public static void RemoveTab(string guid)
    {
        FolderTabsSettings.instance.Tabs.RemoveAll(t => t.Guid == guid);
        Save();
    }

    public static void RenameTab(string guid, string newLabel)
    {
        FolderTabEntry entry = FolderTabsSettings.instance.Tabs.FirstOrDefault(t => t.Guid == guid);
        if (entry == null)
        {
            return;
        }

        entry.Label = newLabel;
        Save();
    }

    public static void MoveTab(int fromIndex, int toIndex)
    {
        List<FolderTabEntry> tabs = FolderTabsSettings.instance.Tabs;
        if (fromIndex < 0 || fromIndex >= tabs.Count || toIndex < 0 || toIndex >= tabs.Count)
        {
            return;
        }

        FolderTabEntry entry = tabs[fromIndex];
        tabs.RemoveAt(fromIndex);
        tabs.Insert(toIndex, entry);
        Save();
    }

    private static void Save()
    {
        FolderTabsSettings.instance.Persist();
        Changed?.Invoke();
    }
}

[FilePath("ProjectSettings/ColdSnapFolderTabs.asset", FilePathAttribute.Location.ProjectFolder)]
internal sealed class FolderTabsSettings : ScriptableSingleton<FolderTabsSettings>
{
    public List<FolderTabEntry> Tabs = new List<FolderTabEntry>();

    public void Persist()
    {
        Save(true);
    }
}

[Serializable]
internal sealed class FolderTabEntry
{
    public string Guid;
    public string Label;
}
