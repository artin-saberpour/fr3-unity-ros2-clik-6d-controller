using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class centroid : MonoBehaviour
{
   void Start()
    {
        // Get the MeshFilter component attached to the object
        MeshFilter meshFilter = transform.GetChild(0).transform.GetComponent<MeshFilter>();
        Vector3 x = transform.GetChild(0).transform.eulerAngles;
        Debug.Log(x);
        if (meshFilter == null)
        {
            Debug.LogError("No MeshFilter found on this GameObject.");
            return;
        }

        Mesh mesh = meshFilter.mesh;

        Vector3[] vertices = mesh.vertices;
        Vector3 center = Vector3.zero;

        // Sum all vertices positions
        foreach (Vector3 vertex in vertices)
        {
            center += vertex;
        }

        // Average the sum by the number of vertices to get the centroid
        center /= vertices.Length;

        Debug.Log("Center of the object in local space: " + center);

        // If you want the center in world space:
        Vector3 worldCenter = transform.TransformPoint(center);
        Debug.Log("Center of the object in world space: " + worldCenter);
    }
}
