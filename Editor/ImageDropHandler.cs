using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Unity 6.4 replaced 32-bit instance IDs with EntityId in the hierarchy callbacks.
#if UNITY_6000_4_OR_NEWER
using HierarchyItemId = UnityEngine.EntityId;
#else
using HierarchyItemId = System.Int32;
#endif

/// <summary>
/// Intercepts texture/sprite drag-and-drop onto Canvas objects in the Hierarchy and
/// creates an Image instead of Unity's default UI Image (sprite) behaviour.
/// Toggle via ColdSnap / UI / Drop Image as Image (enabled by default).
/// </summary>
[InitializeOnLoad]
public static class ImageDropHandler
{
    private const string MenuPath = "ColdSnap/UI/Drop Image as Image";
    private const string PrefKey  = "ColdSnap.ImageDropHandler.Enabled";

    static ImageDropHandler()
    {
#if UNITY_6000_4_OR_NEWER
        EditorApplication.hierarchyWindowItemByEntityIdOnGUI += OnHierarchyGUI;
#else
        EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyGUI;
#endif
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

    static void OnHierarchyGUI(HierarchyItemId itemId, Rect selectionRect)
    {
        if (!IsEnabled()) return;

        var evt = Event.current;
        if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform) return;
        if (!selectionRect.Contains(evt.mousePosition)) return;

        var sprites = CollectSprites();
        if (sprites.Count == 0) return;

#if UNITY_6000_4_OR_NEWER
        var target = EditorUtility.EntityIdToObject(itemId) as GameObject;
#else
        var target = EditorUtility.InstanceIDToObject(itemId) as GameObject;
#endif
        if (target == null || !IsInCanvas(target)) return;

        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

        if (evt.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();

            Undo.SetCurrentGroupName(sprites.Count == 1 ? "Create Image" : "Create Images");
            int group = Undo.GetCurrentGroup();

            foreach (var spr in sprites)
                SpawnImage(spr, target.transform);

            Undo.CollapseUndoOperations(group);
        }

        evt.Use();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    static List<Sprite> CollectSprites()
    {
        var list = new List<Sprite>();
        foreach (var obj in DragAndDrop.objectReferences)
        {
            if (obj is Sprite spr)
            {
                list.Add(spr);
            }
            else if (obj is Texture2D tex)
            {
                string path = AssetDatabase.GetAssetPath(tex);
                var loaded = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (loaded != null)
                    list.Add(loaded);
                else
                    list.Add(Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f)));
            }
        }
        return list;
    }

    static bool IsInCanvas(GameObject go) =>
        go.GetComponent<Canvas>() != null || go.GetComponentInParent<Canvas>() != null;

    static void SpawnImage(Sprite sprite, Transform parent)
    {
        var go = new GameObject(sprite.name);
        Undo.RegisterCreatedObjectUndo(go, "Create Image");
        go.transform.SetParent(parent, false);

        var image = go.AddComponent<Image>();
        image.sprite = sprite;
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(sprite.rect.width, sprite.rect.height);

        Selection.activeGameObject = go;
    }
}
