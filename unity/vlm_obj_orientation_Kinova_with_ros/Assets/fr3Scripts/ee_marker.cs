using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ee_marker : MonoBehaviour
{
    public Transform baseLink;
    public bool grabbed;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        var p = baseLink.InverseTransformPoint(transform.position);
        Debug.Log($"EE in base frame (Unity): {p.x:F3}, {p.y:F3}, {p.z:F3}");
        Debug.LogWarning(transform.name + " --> " +transform.position.ToString());
    }
}
