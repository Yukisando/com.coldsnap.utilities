using UnityEngine;
using UnityEditor;

public static class CenterPivotToBottomLeft
{
    [MenuItem("ColdSnap/Tools/Center Pivot To Bottom Left")]
    static void Execute()
    {
        var go = Selection.activeGameObject;
        if (go == null)
        {
            Debug.LogError("No GameObject selected.");
            return;
        }

        var mf = go.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null)
        {
            Debug.LogError("Selected GameObject has no MeshFilter with a mesh.");
            return;
        }

        // Preserve MeshCollider's original mesh
        var mc = go.GetComponent<MeshCollider>();
        var originalColliderMesh = mc != null ? mc.sharedMesh : null;

        var original = mf.sharedMesh;
        var mesh = Object.Instantiate(original);
        mesh.name = original.name + "_BottomLeft";

        var verts = mesh.vertices;
        var bounds = mesh.bounds;

        // Pivot: left edge (min.x), bottom (min.y), center depth (center.z)
        // Perfect for door hinges
        var pivot = new Vector3(bounds.min.x, bounds.min.y, bounds.center.z);

        for (int i = 0; i < verts.Length; i++)
            verts[i] -= pivot;

        mesh.vertices = verts;
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();

        mf.sharedMesh = mesh;

        // Restore original mesh to collider
        if (mc != null && originalColliderMesh != null)
        {
            mc.sharedMesh = originalColliderMesh;
        }

        Vector3 worldOffset = go.transform.TransformPoint(pivot) - go.transform.position;
        go.transform.position += worldOffset;

        Debug.Log($"Pivot moved to bottom left (min X/Y, center Z) at {pivot:F3}");
    }
}
