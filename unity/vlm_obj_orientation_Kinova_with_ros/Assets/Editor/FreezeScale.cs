using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class FreezeScale : MonoBehaviour
{
#if UNITY_EDITOR
    [MenuItem("Tools/Freeze Scale")]
    static void ApplyScale()
    {
        var go = Selection.activeGameObject;
        if (!go) return;

        var meshFilter = go.GetComponent<MeshFilter>();
        if (!meshFilter) return;

        var mesh = Object.Instantiate(meshFilter.sharedMesh); // clone mesh
        var scale = go.transform.localScale;
        var vertices = mesh.vertices;

        for (int i = 0; i < vertices.Length; i++)
        {
            var v = vertices[i];
            v = Vector3.Scale(v, scale);
            vertices[i] = v;
        }
        mesh.vertices = vertices;
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();

        meshFilter.sharedMesh = mesh;

        go.transform.localScale = Vector3.one; // reset scale
    }
#endif
}
