using UnityEngine;

public class AxisProbe : MonoBehaviour
{
    public Vector3 axis = Vector3.up;
    private Quaternion init_rot;
    void Start()
    {
        init_rot = transform.localRotation;
    }
    void Update()
    {
        transform.localRotation =
            init_rot*Quaternion.AngleAxis(
                Mathf.Sin(Time.time) * 45f,
                axis
            );
    }
}
