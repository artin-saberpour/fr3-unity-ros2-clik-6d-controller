using UnityEditor;
using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

[InitializeOnLoad]
public class PythonPlayController
{
    private static TcpListener listener;
    private static Thread listenerThread;

    static PythonPlayController()
    {
        listenerThread = new Thread(StartServer);
        listenerThread.IsBackground = true;  // Kills thread when Unity closes
        listenerThread.Start();
    }

    private static void StartServer()
    {
        listener = new TcpListener(IPAddress.Loopback, 5557);
        listener.Start();

        while (true)
        {
            try
            {
                using (var client = listener.AcceptTcpClient())
                using (var stream = client.GetStream())
                {
                    byte[] buffer = new byte[1024];
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

                    if (message == "start_play")
                    {
                        // Must run on Unity's main thread
                        EditorApplication.delayCall += () =>
                        {
                            if (!EditorApplication.isPlaying)
                                EditorApplication.EnterPlaymode();
                        };
                    }
                    else if (message == "stop_play")
                    {
                        EditorApplication.delayCall += () =>
                        {
                            if (EditorApplication.isPlaying)
                                EditorApplication.ExitPlaymode();
                        };
                    }
                }
            }
            catch (SocketException ex)
            {
                Debug.LogError("Socket exception: " + ex.Message);
                break;
            }
        }
    }
}
