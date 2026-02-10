using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;

public class RosChatterSubscriber : MonoBehaviour
{
    void Start()
    {
        ROSConnection.GetOrCreateInstance()
            .Subscribe<StringMsg>("/chatter", ChatterCallback);
    }

    void ChatterCallback(StringMsg msg)
    {
        Debug.Log("ROS says: " + msg.data);
    }
}
