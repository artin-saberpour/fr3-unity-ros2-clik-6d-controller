using System.Net.Sockets;
using System.Threading;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using System;
using UnityEngine.UIElements;
using Unity.VisualScripting;

public class TransformSender : MonoBehaviour
{
    public List<GameObject> trackedObjects = new List<GameObject>();
    private TcpClient transformClient;
    private NetworkStream transformStream;
    private Thread sendThread;
    private bool running = false;
    private GameObject go_ref1;
    private List<string> object_names_in_the_scene;

    private List<GameObject> gameobjects_in_the_scene;
    private Vector3 latestPosition;
    private Quaternion latestRotation;
    private string latestObjectName;
    private Transform active_transform;

    private string latestMessage = "T"; // Initialize

    private Quaternion? queuedRotation = null;
    private Vector3? queuedPosition = null;
    private object rotationLock = new object();

    public Transform robot_base_transform, robot_ee_transform;

    private Transform gripper_finger_1_link1, gripper_finger_1_link2;
    private Transform gripper_finger_2_link1, gripper_finger_2_link2;

    private const int finger_link1_angle_open = 37;
    private const int finger_link1_angle_close = 90;

    private const int finger_link2_angle_open = 0;
    private const int finger_link2_angle_close = -30;


    private object lockObj = new object();

    public GameObject orthogonalShotSender;
    private bool grabbed;

    public Transform lefthand, righthand;
    // public GameObject robot_gripper;
    // public Transform robot_gripper_transform;

    void Start()
    {
        object_names_in_the_scene = new List<string>(new string[] {"fork","kitchenknife", "apple", "banana", "bleach", "hammer", "mug", "mustard", "plate", "powerdrill", "scissors", "screwdrivers", "skillet", "spatula", "spoon", "sugar", "wood", "bowl", "left_hand", "right_hand", "head", "eye", "torso", "left_shoulder", "right_shoulder"});//{"kitchenknife", "hammer", "mug", "left_hand", "right_hand", "head", "user", "left_shoulder"});
        // active_transform = GameObject.Find("kitchenknife").transform;//GameObject.Find("kitchenknife").transform;
        active_transform = orthogonalShotSender.transform.GetComponent<OrthogonalScreenshotSender>().targetObject.transform;

        gripper_finger_1_link1 = robot_ee_transform.parent.parent.GetChild(3);
        gripper_finger_1_link2 = gripper_finger_1_link1.GetChild(2);
        gripper_finger_2_link1 = robot_ee_transform.parent.parent.GetChild(2);
        gripper_finger_2_link2 = gripper_finger_2_link1.GetChild(2);
        // objects_in_the_scene = new List<GameObject>(["kitchenknife", ]);
        foreach (string o_name in object_names_in_the_scene)
        {
            GameObject tmp = GameObject.Find(o_name);
            if (tmp != null)
                trackedObjects.Add(tmp);
        }

        // robot_gripper_transform = robot_gripper.get

        // go_ref1 = GameObject.Find("kitchenknife");
        // trackedObjects.Add(go_ref1);
        transformClient = new TcpClient("127.0.0.1", 6001); // Separate port
        transformStream = transformClient.GetStream();
        running = true;
        sendThread = new Thread(SendLoop);
        sendThread.Start();
    }

    public void load_pose(float frame)
    {
        if (frame == 1.0)
        {
            print("hi");
        }
    }
    private bool robot_grabbed(Transform activeObject, bool release = false)
    {
        if (grabbed==true) return grabbed;
        if (Vector3.Distance(robot_ee_transform.position, activeObject.position) < 0.5)
        {
            // activeObject.parent = robot_ee_transform;
            // activeObject.localPosition = Vector3.zero;

            //activate the gripper
            // gripper_finger_1_link1.localEulerAngles = new Vector3(finger_link1_angle_close, 0, 0);
            // gripper_finger_2_link1.localEulerAngles = new Vector3(finger_link1_angle_close, 0, 0);
            // gripper_finger_1_link2.localEulerAngles = new Vector3(0, finger_link2_angle_close, 0);
            // gripper_finger_2_link2.localEulerAngles = new Vector3(0, finger_link2_angle_close, 0);

            return true;
        }
        else
        {
            // activeObject.parent = null;
            // gripper_finger_1_link1.localEulerAngles = new Vector3(finger_link1_angle_open, 0, 0);
            // gripper_finger_2_link1.localEulerAngles = new Vector3(finger_link1_angle_open, 0, 0);
            // gripper_finger_1_link2.localEulerAngles = new Vector3(0, finger_link2_angle_open, 0);
            // gripper_finger_2_link2.localEulerAngles = new Vector3(0, finger_link2_angle_open, 0);
            return false;
        }
    }

    void Update()
    {
        active_transform = orthogonalShotSender.transform.GetComponent<OrthogonalScreenshotSender>().targetObject.transform;
        lock (lockObj)
        {
            StringBuilder sb = new StringBuilder("TRANSFORM|");

            foreach (GameObject obj in trackedObjects)
            {
                if (obj == null) continue;

                Vector3 pos = obj.transform.position;
                Quaternion rot = obj.transform.rotation;

                sb.AppendFormat("{0} {1} {2} {3} {4} {5} {6} {7};",
                    obj.name, pos.x, pos.y, pos.z,
                    rot.x, rot.y, rot.z, rot.w
                );
            }

            // --- ADD BASE WORLD→LOCAL MATRIX ---
            Matrix4x4 m = robot_base_transform.worldToLocalMatrix;
            Debug.LogWarning(m);

            sb.Append("BASE_MATRIX ");

            for (int r = 0; r < 4; r++)
            {
                for (int c = 0; c < 4; c++)
                {
                    sb.Append(m[r, c]);
                    sb.Append(" ");
                }
            }
            sb.Append(";");

            grabbed = robot_grabbed(activeObject: active_transform);
            sb.Append("GRAB ");
            sb.Append(grabbed.ToString().ToLower());
            sb.Append(";");


            latestMessage = sb.ToString();
        }
        // ✅ Apply received quaternion safely on the main thread
        // if (!grabbed)
        lock (rotationLock)
        {
            if (queuedRotation.HasValue)
            {
                active_transform.rotation = queuedRotation.Value;
                queuedRotation = null;
            }
            if (queuedPosition.HasValue)
            {
                    active_transform.position = queuedPosition.Value;
            }
        }
    }

    void SendLoop()
    {
        while (running)
        {
            if (transformStream != null && transformStream.CanWrite)
            {
                string msg;

                lock (lockObj)
                {
                    msg = latestMessage;
                }

                // ✅ Prevent null or empty message from being sent
                if (string.IsNullOrEmpty(msg))
                    msg = "T";

                byte[] data = Encoding.UTF8.GetBytes(msg);
                byte[] length = BitConverter.GetBytes(data.Length);
                Debug.LogWarning(msg);
                try
                {
                    transformStream.Write(length, 0, 4);
                    transformStream.Write(data, 0, data.Length);
                    transformStream.Flush();
                }
                catch (Exception e)
                {
                    Debug.LogError("SendLoop error: " + e.Message);
                    running = false;   // ✅ Stop the loop if the connection is gone
                    break;
                }
                // Quaternion? receivedQuat = ReceiveQuaternion();
                List<object> receivedQuatAndMaybePos = ReceiveQuaternion();
                if (receivedQuatAndMaybePos.Count == 1)
                {
                    queuedRotation = (Quaternion?)receivedQuatAndMaybePos[0];
                }
                else if (receivedQuatAndMaybePos.Count == 2)
                {
                    queuedRotation = (Quaternion?)receivedQuatAndMaybePos[0];
                    queuedPosition = (Vector3?)receivedQuatAndMaybePos[1];
                }
                // else if (receivedQuatAndMaybePos.Count == 3)
                // {
                //     queuedRotation = (Quaternion?)receivedQuatAndMaybePos[0];
                //     queuedPosition = (Vector3?)receivedQuatAndMaybePos[1];
                //     load_pose((Vector3?)receivedQuatAndMaybePos[2]);
                // }

            }

            Thread.Sleep(100);
        }
    }

    private List<object> ReceiveQuaternion()
    {
        var result = new List<object>();

        try
        {
            if (transformStream.DataAvailable)
            {
                // Read length
                byte[] lengthBytes = new byte[4];
                int bytesRead = transformStream.Read(lengthBytes, 0, 4);
                if (bytesRead != 4) return result;

                int msgLength = BitConverter.ToInt32(lengthBytes, 0);
                if (msgLength <= 0 || msgLength > 1024) return result;

                // Read message fully
                byte[] buffer = new byte[msgLength];
                bytesRead = transformStream.Read(buffer, 0, msgLength);
                if (bytesRead != msgLength) return result;

                string response = Encoding.UTF8.GetString(buffer).Trim();
                string[] parts = response.Split(' ');

                if ((parts.Length == 4 || parts.Length == 7) &&
                    float.TryParse(parts[0], out float qx) &&
                    float.TryParse(parts[1], out float qy) &&
                    float.TryParse(parts[2], out float qz) &&
                    float.TryParse(parts[3], out float qw))
                {
                    var q = new Quaternion(qx, qy, qz, qw);
                    result.Add(q);

                    if (parts.Length == 7 &&
                        float.TryParse(parts[4], out float px) &&
                        float.TryParse(parts[5], out float py) &&
                        float.TryParse(parts[6], out float pz))
                    {
                        var p = new Vector3(px, py, pz);
                        result.Add(p);
                    }
                    if (parts.Length == 8 &&
                        float.TryParse(parts[7], out float frame))
                    {
                        var p = frame;
                        result.Add(p);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Error receiving quaternion: " + e.Message);
        }

        return result;
    }






    private Quaternion? ReceiveQuaternion_only()
    {
        try
        {
            // Only proceed if data is available
            if (transformStream.DataAvailable)
            {
                byte[] lengthBytes = new byte[4];
                int bytesRead = transformStream.Read(lengthBytes, 0, 4);
                if (bytesRead != 4)
                {
                    Debug.LogWarning("Invalid length header received.");
                    return null;
                }

                int msgLength = BitConverter.ToInt32(lengthBytes, 0);
                if (msgLength <= 0 || msgLength > 1024) // sanity check
                {
                    Debug.LogWarning($"Suspicious message length: {msgLength}");
                    return null;
                }

                byte[] buffer = new byte[msgLength];
                bytesRead = transformStream.Read(buffer, 0, msgLength);
                if (bytesRead != msgLength)
                {
                    Debug.LogWarning("Incomplete message received.");
                    return null;
                }

                string response = Encoding.UTF8.GetString(buffer).Trim();
                Debug.Log("Received raw response: " + response);

                string[] parts = response.Split(' ');
                if ((parts.Length == 4 || parts.Length == 7) &&
                    float.TryParse(parts[0], out float qx) &&
                    float.TryParse(parts[1], out float qy) &&
                    float.TryParse(parts[2], out float qz) &&
                    float.TryParse(parts[3], out float qw))
                {
                    // If it's just a quaternion
                    if (parts.Length == 4)
                    {
                        return new Quaternion(qx, qy, qz, qw);
                    }
                    // If there are 7 values, parse the extra 3 (e.g., position)
                    else
                    {
                        if (float.TryParse(parts[4], out float px) &&
                            float.TryParse(parts[5], out float py) &&
                            float.TryParse(parts[6], out float pz))
                        {
                            Debug.Log($"Received quaternion + position: Q=({qx},{qy},{qz},{qw}), P=({px},{py},{pz})");
                            return new Quaternion(qx, qy, qz, qw);
                        }
                    }
                }
                else
                {
                    Debug.LogWarning("Malformed data: " + response);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Error receiving quaternion: " + e.Message);
        }

        return null;
    }


    void OnApplicationQuit()
    {
        running = false;
        sendThread?.Join();
        transformStream?.Close();
        transformClient?.Close();
    }


    
}





/*

using System.Net.Sockets;
using System.Threading;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using System;
using UnityEngine.UIElements;

public class TransformSender : MonoBehaviour
{
    public List<GameObject> trackedObjects = new List<GameObject>();
    private TcpClient transformClient;
    private NetworkStream transformStream;
    private Thread sendThread;
    private bool running = false;
    private GameObject go_ref1;
    private List<string> object_names_in_the_scene;

    private List<GameObject> gameobjects_in_the_scene;
    private Vector3 latestPosition;
    private Quaternion latestRotation;
    private string latestObjectName;
    private Transform active_transform;

    private string latestMessage = "T"; // Initialize

    private Quaternion? queuedRotation = null;
    private Vector3? queuedPosition = null;
    private object rotationLock = new object();

    public Transform robot_base;


    private object lockObj = new object();

    public GameObject orthogonalShotSender;

    void Start()
    {
        object_names_in_the_scene = new List<string>(new string[] {"fork","kitchenknife", "apple", "banana", "bleach", "hammer", "mug", "mustard", "plate", "powerdrill", "scissors", "screwdrivers", "skillet", "spatula", "spoon", "sugar", "wood", "bowl", "left_hand", "right_hand", "head", "eye", "torso", "left_shoulder", "right_shoulder"});//{"kitchenknife", "hammer", "mug", "left_hand", "right_hand", "head", "user", "left_shoulder"});
        // active_transform = GameObject.Find("kitchenknife").transform;//GameObject.Find("kitchenknife").transform;
        active_transform = orthogonalShotSender.transform.GetComponent<OrthogonalScreenshotSender>().targetObject.transform;
        // objects_in_the_scene = new List<GameObject>(["kitchenknife", ]);
        foreach (string o_name in object_names_in_the_scene)
        {
            GameObject tmp = GameObject.Find(o_name);
            if (tmp != null)
                trackedObjects.Add(tmp);
        }

        // go_ref1 = GameObject.Find("kitchenknife");
        // trackedObjects.Add(go_ref1);
        transformClient = new TcpClient("127.0.0.1", 6001); // Separate port
        transformStream = transformClient.GetStream();
        running = true;
        sendThread = new Thread(SendLoop);
        sendThread.Start();
    }


    void Update()
    {
        active_transform = orthogonalShotSender.transform.GetComponent<OrthogonalScreenshotSender>().targetObject.transform;
        lock (lockObj)
        {
            StringBuilder sb = new StringBuilder("TRANSFORM|");

            foreach (GameObject obj in trackedObjects)
            {
                if (obj == null) continue;

                Vector3 pos = obj.transform.position;
                Quaternion rot = obj.transform.rotation;

                sb.AppendFormat("{0} {1} {2} {3} {4} {5} {6} {7};",
                    obj.name, pos.x, pos.y, pos.z,
                    rot.x, rot.y, rot.z, rot.w
                );
            }

            // Remove trailing semicolon
            if (sb.Length > 0 && sb[sb.Length - 1] == ';')
                sb.Length--;

            latestMessage = sb.ToString();
        }
        // ✅ Apply received quaternion safely on the main thread
        lock (rotationLock)
        {
            if (queuedRotation.HasValue)
            {
                active_transform.rotation = queuedRotation.Value;// * active_transform.rotation;
                Debug.Log("✔️ Applied quaternion to transform.");
                queuedRotation = null;
            }
            if (queuedPosition.HasValue)
            {
                active_transform.position = queuedPosition.Value;
            }
        }
    }

    void SendLoop()
    {
        while (running)
        {
            if (transformStream != null && transformStream.CanWrite)
            {
                string msg;

                lock (lockObj)
                {
                    msg = latestMessage;
                }

                // ✅ Prevent null or empty message from being sent
                if (string.IsNullOrEmpty(msg))
                    msg = "T";

                byte[] data = Encoding.UTF8.GetBytes(msg);
                byte[] length = BitConverter.GetBytes(data.Length);
                Debug.LogWarning(msg);
                try
                {
                    transformStream.Write(length, 0, 4);
                    transformStream.Write(data, 0, data.Length);
                    transformStream.Flush();
                }
                catch (Exception e)
                {
                    Debug.LogError("SendLoop error: " + e.Message);
                    running = false;   // ✅ Stop the loop if the connection is gone
                    break;
                }
                // Quaternion? receivedQuat = ReceiveQuaternion();
                List<object> receivedQuatAndMaybePos = ReceiveQuaternion();
                if (receivedQuatAndMaybePos.Count == 1)
                {
                    queuedRotation = (Quaternion?)receivedQuatAndMaybePos[0];
                }
                else if (receivedQuatAndMaybePos.Count == 2)
                {
                    queuedRotation = (Quaternion?)receivedQuatAndMaybePos[0];
                    queuedPosition = (Vector3?)receivedQuatAndMaybePos[1];
                }

            }

            Thread.Sleep(100);
        }
    }

    private List<object> ReceiveQuaternion()
    {
        var result = new List<object>();

        try
        {
            if (transformStream.DataAvailable)
            {
                // Read length
                byte[] lengthBytes = new byte[4];
                int bytesRead = transformStream.Read(lengthBytes, 0, 4);
                if (bytesRead != 4) return result;

                int msgLength = BitConverter.ToInt32(lengthBytes, 0);
                if (msgLength <= 0 || msgLength > 1024) return result;

                // Read message fully
                byte[] buffer = new byte[msgLength];
                bytesRead = transformStream.Read(buffer, 0, msgLength);
                if (bytesRead != msgLength) return result;

                string response = Encoding.UTF8.GetString(buffer).Trim();
                string[] parts = response.Split(' ');

                if ((parts.Length == 4 || parts.Length == 7) &&
                    float.TryParse(parts[0], out float qx) &&
                    float.TryParse(parts[1], out float qy) &&
                    float.TryParse(parts[2], out float qz) &&
                    float.TryParse(parts[3], out float qw))
                {
                    var q = new Quaternion(qx, qy, qz, qw);
                    result.Add(q);

                    if (parts.Length == 7 &&
                        float.TryParse(parts[4], out float px) &&
                        float.TryParse(parts[5], out float py) &&
                        float.TryParse(parts[6], out float pz))
                    {
                        var p = new Vector3(px, py, pz);
                        result.Add(p);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Error receiving quaternion: " + e.Message);
        }

        return result;
    }






    private Quaternion? ReceiveQuaternion_only()
    {
        try
        {
            // Only proceed if data is available
            if (transformStream.DataAvailable)
            {
                byte[] lengthBytes = new byte[4];
                int bytesRead = transformStream.Read(lengthBytes, 0, 4);
                if (bytesRead != 4)
                {
                    Debug.LogWarning("Invalid length header received.");
                    return null;
                }

                int msgLength = BitConverter.ToInt32(lengthBytes, 0);
                if (msgLength <= 0 || msgLength > 1024) // sanity check
                {
                    Debug.LogWarning($"Suspicious message length: {msgLength}");
                    return null;
                }

                byte[] buffer = new byte[msgLength];
                bytesRead = transformStream.Read(buffer, 0, msgLength);
                if (bytesRead != msgLength)
                {
                    Debug.LogWarning("Incomplete message received.");
                    return null;
                }

                string response = Encoding.UTF8.GetString(buffer).Trim();
                Debug.Log("Received raw response: " + response);

                string[] parts = response.Split(' ');
                if ((parts.Length == 4 || parts.Length == 7) &&
                    float.TryParse(parts[0], out float qx) &&
                    float.TryParse(parts[1], out float qy) &&
                    float.TryParse(parts[2], out float qz) &&
                    float.TryParse(parts[3], out float qw))
                {
                    // If it's just a quaternion
                    if (parts.Length == 4)
                    {
                        return new Quaternion(qx, qy, qz, qw);
                    }
                    // If there are 7 values, parse the extra 3 (e.g., position)
                    else
                    {
                        if (float.TryParse(parts[4], out float px) &&
                            float.TryParse(parts[5], out float py) &&
                            float.TryParse(parts[6], out float pz))
                        {
                            Debug.Log($"Received quaternion + position: Q=({qx},{qy},{qz},{qw}), P=({px},{py},{pz})");
                            return new Quaternion(qx, qy, qz, qw);
                        }
                    }
                }
                else
                {
                    Debug.LogWarning("Malformed data: " + response);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Error receiving quaternion: " + e.Message);
        }

        return null;
    }


    void OnApplicationQuit()
    {
        running = false;
        sendThread?.Join();
        transformStream?.Close();
        transformClient?.Close();
    }


    
}







*/