using UnityEngine;
using System;
using System.Text;
using System.Net.WebSockets;
using System.Threading;
using Newtonsoft.Json;

[Serializable]
public class RosbridgeMessage<T>
{
    public string op;
    public string topic;
    public T msg;
}

[Serializable]
public class JointStateMsg
{
    public string[] name;
    public double[] position;
}

public class RosbridgeListener : MonoBehaviour
{
    public Transform[] jointTransforms;
    public string[] jointNames;

    ClientWebSocket ws;

    async void Start()
    {
        ws = new ClientWebSocket();
        Uri serverUri = new Uri("ws://localhost:9090");

        await ws.ConnectAsync(serverUri, CancellationToken.None);
        Debug.Log("Connected to rosbridge");

        // Subscribe to /chatter (ROS → Unity)
        var subscribeMsg = new
        {
            op = "subscribe",
            topic = "/chatter"
        };

        await SendJson(subscribeMsg);

        // Start listening loop
        Listen();
    }

    void Update()
    {
        // Unity → ROS: press SPACE to publish
        if (Input.GetKeyDown(KeyCode.Space))
        {
            PublishToROS("Hello from Unity at " + Time.time.ToString("F2"));
        }
    }

    async void PublishToROS(string text)
    {
        var publishMsg = new
        {
            op = "publish",
            topic = "/unity_chatter",
            msg = new
            {
                data = text
            }
        };

        await SendJson(publishMsg);
        Debug.Log("Sent to ROS: " + text);
    }

    async void Listen()
    {
        var buffer = new byte[4096];

        while (ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(
                new ArraySegment<byte>(buffer),
                CancellationToken.None
            );

            string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
            Debug.Log("ROS message: " + message);
        }
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
}
