using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class attachment : MonoBehaviour
{
    // Start is called before the first frame update
    public Transform attachment_point;
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    void LateUpdate()
    {
        transform.position = attachment_point.position;
    }
}
