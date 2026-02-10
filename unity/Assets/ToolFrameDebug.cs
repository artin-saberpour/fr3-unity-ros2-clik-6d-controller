using UnityEngine;

public class ToolFrameDebug : MonoBehaviour
{
    public Transform robotBase;
    void Update()
    {
        Vector3 p = robotBase.InverseTransformPoint(transform.position);//transform.position;
        Debug.Log($"[Unity FK] EE world pos: {p.x:F3}, {p.y:F3}, {p.z:F3}");
    }
}
