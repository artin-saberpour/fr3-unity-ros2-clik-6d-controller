using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Sensor;
using System.Collections.Generic;
using TMPro;

public class RosJointStateController_TCP : MonoBehaviour
{
    public Transform[] jointTransforms;
    public string[] jointNames;

    public Vector3[] jointAxesUnity;

    public Quaternion[] initialRotations;


    // One entry per joint, in joint order
    public Vector3[] jointAxes = new Vector3[]
    {
        Vector3.up,    // joint 1
        Vector3.up,    // joint 2
        Vector3.up,    // joint 3
        Vector3.up,    // joint 4
        Vector3.up,    // joint 5
        Vector3.up,    // joint 6
        Vector3.up     // joint 7
    };

    // +1 or -1 per joint
    public float[] jointSigns = new float[]
    {
        -1f, // joint 1 (sign mismatch)
        1f, // joint 2 (works fine)
        -1f, // joint 3
        -1f, // joint 4
        1f, // joint 5 (works fine)
        -1f, // joint 6
        -1f  // joint 7
    };

    ROSConnection ros;

    Dictionary<string, float> targetAngles = new Dictionary<string, float>();
    bool hasReceived = false;

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.Subscribe<RosMessageTypes.Sensor.JointStateMsg>("/joint_states", JointStateCallback);
        Debug.Log("subscribed to tcp");

        initialRotations = new Quaternion[jointTransforms.Length];
    for (int i = 0; i < jointTransforms.Length; i++)
        initialRotations[i] = jointTransforms[i].localRotation;

    // for (int i = 0; i< jointTransforms.Length; i++)
    //     jointAxes[i] = jointTransforms[i].transform.up;
    }

    void JointStateCallback(RosMessageTypes.Sensor.JointStateMsg msg)
    {
        foreach (var n in msg.name)
            // Debug.Log("[TCP] ROS joint: " + n);

        // Debug.Log($"[TCP] JointState received with {msg.name.Length} joints");
        for (int i = 0; i < msg.name.Length; i++)
        {
            targetAngles[msg.name[i]] = (float)msg.position[i];
            // Debug.Log(targetAngles[msg.name[i]] + "    <---");
        }
        hasReceived = true;
    }
// void FixedUpdate()
// {
//     jointTransforms[0].localRotation =
//         initialRotations[0] *
//         Quaternion.AngleAxis(
//             Mathf.Sin(Time.time) * 30f,
//             Vector3.up
//         );
// }

    void FixedUpdate()
    {
        // Debug.Log($"[TCP] FixedUpdate hasReceived={hasReceived}");
        if (!hasReceived) return;

        for (int i = 0; i < jointNames.Length; i++)
        {
            if (!targetAngles.ContainsKey(jointNames[i])) continue;

            float angleRad = targetAngles[jointNames[i]];
            float angleDeg = angleRad * Mathf.Rad2Deg;
            Debug.LogError("angleRad " + i.ToString() + " ->  " + angleRad.ToString());


            jointTransforms[i].localRotation =
                initialRotations[i] *
                Quaternion.AngleAxis(-jointSigns[i]*angleDeg, Vector3.up);
        }
    }
}


