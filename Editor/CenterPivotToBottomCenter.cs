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

        // Preserve MeshCollider's original mesh
        var mc = go.GetComponent<MeshCollider>();
        var originalColliderMesh = mc != null ? mc.sharedMesh : null;

        var original = mf.sharedMesh;
        var mesh = Object.Instantiate(original);
        mesh.name = original.name + "_BottomCentered";

        var verts = mesh.vertices;
        var tris = mesh.triangles;

        // Compute area-weighted CoM for X and Z
        float totalArea = 0f;
        Vector3 centroidSum = Vector3.zero;
        for (int i = 0; i < tris.Length; i += 3)
        {
            Vector3 v0 = verts[tris[i]];
            Vector3 v1 = verts[tris[i + 1]];
            Vector3 v2 = verts[tris[i + 2]];

            Vector3 triCentroid = (v0 + v1 + v2) / 3f;
            float area = Vector3.Cross(v1 - v0, v2 - v0).magnitude * 0.5f;

            centroidSum += triCentroid * area;
            totalArea += area;
        }

        if (totalArea <= 0f)
        {
            Debug.LogError("Mesh has zero total area; cannot compute centroid.");
            return;
        }

        Vector3 com = centroidSum / totalArea;

        // Find the lowest Y point
        float minY = float.MaxValue;
        foreach (var v in verts)
        {
            if (v.y < minY) minY = v.y;
        }

        // Pivot: CoM's X and Z, lowest Y
        var pivot = new Vector3(com.x, minY, com.z);

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

        Debug.Log($"Pivot moved to bottom center (CoM X/Z, lowest Y) at {pivot:F3}");
    }
}
