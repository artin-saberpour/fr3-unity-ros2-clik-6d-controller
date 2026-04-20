using UnityEngine;
using System;
using System.Collections.Generic;
using System.Text;
using System.Net.WebSockets;
using System.Threading;
using Newtonsoft.Json;
using Unity.Robotics.UrdfImporter;

public class RosJointStateController : MonoBehaviour
{
    ClientWebSocket ws;

    Dictionary<string, ArticulationBody> jointMap =
        new Dictionary<string, ArticulationBody>();

    async void Start()
    {
        // 1️⃣ Find all articulated joints and map by URDF joint name
        foreach (var joint in GetComponentsInChildren<ArticulationBody>())
        {
            var urdfJoint = joint.GetComponent<UrdfJoint>();
            if (urdfJoint == null)
                continue;

            // Set drive strength ONCE
            var drive = joint.xDrive;
            drive.stiffness = 10000f;
            drive.damping = 100f;
            drive.forceLimit = 1000f;
            joint.xDrive = drive;

            jointMap[urdfJoint.jointName] = joint;
            Debug.Log($"Mapped {urdfJoint.jointName}");
        }

        Debug.Log($"Total joints mapped: {jointMap.Count}");

        // 2️⃣ Connect to rosbridge
        ws = new ClientWebSocket();
        await ws.ConnectAsync(
            new Uri("ws://localhost:9090"),
            CancellationToken.None
        );

        // 3️⃣ Subscribe to /joint_states
        await SendJson(new
        {
            op = "subscribe",
            topic = "/joint_states"
        });

        Listen();
    }

    async void Listen()
    {
        var buffer = new byte[8192];

        while (ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(
                new ArraySegment<byte>(buffer),
                CancellationToken.None
            );

            string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
            HandleJointState(json);
        }
    }

    void HandleJointState(string json)
    {
        try
        {
            var msg = JsonConvert.DeserializeObject<JointStateMsg>(json);

            if (msg.op != "publish" || msg.topic != "/joint_states")
                return;

            for (int i = 0; i < msg.msg.name.Length; i++)
            {
                string jointName = msg.msg.name[i];
                double rad = msg.msg.position[i];

                if (!jointMap.TryGetValue(jointName, out var joint))
                    continue;

                var drive = joint.xDrive;
                drive.target = (float)(rad * Mathf.Rad2Deg);
                joint.xDrive = drive;
            }
        }
        catch { }
    }

    async System.Threading.Tasks.Task SendJson(object obj)
    {
        string json = JsonConvert.SerializeObject(obj);
        byte[] bytes = Encoding.UTF8.GetBytes(json);

        await ws.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            true,
            CancellationToken.None
        );
    }

    [Serializable]
    class JointStateMsg
    {
        public string op;
        public string topic;
        public JointStateData msg;
    }

    [Serializable]
    class JointStateData
    {
        public string[] name;
        public double[] position;
    }
}
