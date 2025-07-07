using System.Linq;
using UnityEditor;
using UnityEngine;

public class AutoGroup : EditorWindow
{
    [MenuItem("GameObject/Auto Group %g", false, 0)]
    static void GroupSelectedObjects()
    {
        var allSelected = Selection.gameObjects;
        if (allSelected.Length == 0)
        {
            Debug.LogWarning("No GameObjects selected to group.");
            return;
        }

        // Only keep those whose parent is NOT also selected
        var topLevel = allSelected
            .Where(go => go.transform.parent == null 
                         || !allSelected.Contains(go.transform.parent.gameObject))
            .ToArray();

        // Compute center from top-level only
        Vector3 center = topLevel.Aggregate(Vector3.zero, (sum, go) => sum + go.transform.position) 
                         / topLevel.Length;

        GameObject group = new GameObject("Group");
        Undo.RegisterCreatedObjectUndo(group, "Create Group");
        group.transform.position = center;

        foreach (GameObject go in topLevel)
            Undo.SetTransformParent(go.transform, group.transform, "Group Objects");

        Selection.activeGameObject = group;
    }
}