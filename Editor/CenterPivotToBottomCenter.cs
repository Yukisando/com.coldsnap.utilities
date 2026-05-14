using UnityEngine;
using UnityEditor;

public static class CenterPivotToBottomCenter
{
    [MenuItem("ColdSnap/Tools/Center Pivot To Bottom Center")]
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

        var original = mf.sharedMesh;
        var mesh = Object.Instantiate(original);
        mesh.name = original.name + "_BottomCentered";

        var verts = mesh.vertices;

        var bounds = mesh.bounds;
        var pivot = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

        for (int i = 0; i < verts.Length; i++)
            verts[i] -= pivot;

        mesh.vertices = verts;
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();

        mf.sharedMesh = mesh;

        Vector3 worldOffset = go.transform.TransformPoint(pivot) - go.transform.position;
        go.transform.position += worldOffset;

        Debug.Log($"Pivot re-centered to bottom center at {pivot:F3}");
    }
}
