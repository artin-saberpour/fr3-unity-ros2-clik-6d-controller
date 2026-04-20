using UnityEngine;
using UnityEngine.iOS;

public class VertexVisualizerRuntime : MonoBehaviour
{
    void Update()
    {
        MeshFilter mf = GetComponent<MeshFilter>();
        if (mf == null || mf.mesh == null) return;

        Vector3[] vertices = mf.mesh.vertices;
        // Debug.LogError(vertices);
        foreach (Vector3 v in vertices)
        {
            Debug.DrawLine(transform.position, transform.TransformPoint(v), Color.green);
        }
    }
}
