using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Intercepts texture/sprite drag-and-drop onto Canvas objects in the Hierarchy and
/// creates a Image instead of Unity's default UI Image (sprite) behaviour.
/// Toggle via ColdSnap / UI / Drop Image as Image (enabled by default).
/// </summary>
[InitializeOnLoad]
public static class ImageDropHandler
{
    private const string MenuPath = "ColdSnap/UI/Drop Image as Image";
    private const string PrefKey  = "ColdSnap.ImageDropHandler.Enabled";

    static ImageDropHandler()
    {
        EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyGUI;
    }

    // ── Menu ────────────────────────────────────────────────────────────────

    [MenuItem(MenuPath, validate = true)]
    static bool ValidateToggle()
    {
        Menu.SetChecked(MenuPath, IsEnabled());
        return true;
    }

    [MenuItem(MenuPath)]
    static void Toggle() => EditorPrefs.SetBool(PrefKey, !IsEnabled());

    static bool IsEnabled() => EditorPrefs.GetBool(PrefKey, true);

    // ── Drag handler ────────────────────────────────────────────────────────

    static void OnHierarchyGUI(int instanceID, Rect selectionRect)
    {
        if (!IsEnabled()) return;

        var evt = Event.current;
        if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform) return;
        if (!selectionRect.Contains(evt.mousePosition)) return;

        var textures = CollectTextures();
        if (textures.Count == 0) return;

        var target = EditorUtility.InstanceIDToObject(instanceID) as GameObject;
        if (target == null || !IsInCanvas(target)) return;

        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

        if (evt.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();

            Undo.SetCurrentGroupName(textures.Count == 1 ? "Create Image" : "Create Images");
            int group = Undo.GetCurrentGroup();

            foreach (var tex in textures)
                SpawnImage(tex, target.transform);

            Undo.CollapseUndoOperations(group);
        }

        evt.Use();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    static List<Texture2D> CollectTextures()
    {
        var list = new List<Texture2D>();
        foreach (var obj in DragAndDrop.objectReferences)
        {
            if (obj is Texture2D tex)
                list.Add(tex);
            else if (obj is Sprite spr)
                list.Add(spr.texture);
        }
        return list;
    }

    static bool IsInCanvas(GameObject go) =>
        go.GetComponent<Canvas>() != null || go.GetComponentInParent<Canvas>() != null;

    static void SpawnImage(Texture2D texture, Transform parent)
    {
        var go = new GameObject(texture.name);
        Undo.RegisterCreatedObjectUndo(go, "Create Image");
        go.transform.SetParent(parent, false);

        var Image = go.AddComponent<Image>();
        Image.texture = texture;
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(texture.width, texture.height);

        Selection.activeGameObject = go;
    }
}
