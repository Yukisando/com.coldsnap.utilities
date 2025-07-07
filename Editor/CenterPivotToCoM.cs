using UnityEngine;
using UnityEditor;

public static class CenterPivotToCoM
{
    [MenuItem("ColdSnap/Tools/Center Pivot To Mesh CoM")]
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

        // Grab the mesh and work on a copy
        var original = mf.sharedMesh;
        var mesh = Object.Instantiate(original);
        mesh.name = original.name + "_Centered";
        
        var verts = mesh.vertices;
        var tris  = mesh.triangles;

        // Compute area-weighted centroid
        float totalArea = 0f;
        Vector3 centroidSum = Vector3.zero;
        for (int i = 0; i < tris.Length; i += 3)
        {
            Vector3 v0 = verts[tris[i]];
            Vector3 v1 = verts[tris[i + 1]];
            Vector3 v2 = verts[tris[i + 2]];

            // triangle centroid
            Vector3 triCentroid = (v0 + v1 + v2) / 3f;
            // triangle area
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

        // Shift vertices so CoM becomes the origin
        for (int i = 0; i < verts.Length; i++)
            verts[i] -= com;

        mesh.vertices = verts;
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();

        // Assign new mesh instance
        mf.sharedMesh = mesh;

        // Move the transform so the object visually stays in place
        Vector3 worldOffset = go.transform.TransformPoint(com) - go.transform.position;
        go.transform.position += worldOffset;

        Debug.Log($"Pivot re-centered to mesh CoM at {com:F3}");
    }
}
