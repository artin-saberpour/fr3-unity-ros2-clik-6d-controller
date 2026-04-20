using UnityEngine;
using System;
using System.Net.Sockets;
using System.Text;
using System.IO;
using System.Threading.Tasks;

[Serializable]
public class OrientResult {
    public float phi, theta, delta, confidence;
    public string error;
}

public class OrthogonalScreenshotSenderOrientAnything : MonoBehaviour
{
    public string host = "127.0.0.1";
    public int port = 8000;                  // <-- match Python
    public Camera captureCam;
    public int imageSize = 512;

    private TcpClient client;
    private NetworkStream stream;

    async void Start()
    {
        // Connect
        client = new TcpClient();
        await client.ConnectAsync(host, port);
        client.NoDelay = true;               // send immediately (no Nagle)
        stream = client.GetStream();
        stream.WriteTimeout = 5000;
        stream.ReadTimeout = 5000;
        Debug.Log("Connected to Python server");

        // Send once immediately (you can keep your key-press later)
        var imgs = new byte[3][];
        imgs[0] = CapturePngFromCamera(captureCam);
        imgs[1] = CapturePngFromCamera(captureCam);
        imgs[2] = CapturePngFromCamera(captureCam);
        await SendThreeImagesAndReceiveAsync(imgs);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.O))
        {
            var imgs = new byte[3][];
            imgs[0] = CapturePngFromCamera(captureCam);
            imgs[1] = CapturePngFromCamera(captureCam);
            imgs[2] = CapturePngFromCamera(captureCam);
            _ = SendThreeImagesAndReceiveAsync(imgs);
        }
    }

    private byte[] CapturePngFromCamera(Camera cam)
    {
        if (cam == null) cam = Camera.main;
        var rt = new RenderTexture(imageSize, imageSize, 24);
        var tex = new Texture2D(imageSize, imageSize, TextureFormat.RGB24, false);

        var prevRT = RenderTexture.active;
        var prevTarget = cam.targetTexture;

        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, imageSize, imageSize), 0, 0);
        tex.Apply();

        cam.targetTexture = prevTarget;
        RenderTexture.active = prevRT;
        Destroy(rt);

        var png = tex.EncodeToPNG();
        Destroy(tex);
        return png;
    }

    private async Task SendThreeImagesAndReceiveAsync(byte[][] images)
    {
        try
        {
            if (images == null || images.Length != 3) { Debug.LogError("Need exactly 3 images."); return; }
            if (stream == null) { Debug.LogError("Stream null."); return; }

            // 1) send number of images (BE int32)
            int n = images.Length;
            byte[] numImagesBytes = BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(n));
            await stream.WriteAsync(numImagesBytes, 0, 4);
            Debug.Log($"[Unity] Sent num_images={n}");

            // 2) send each image [len (BE int32)] + [bytes]
            for (int i = 0; i < n; i++)
            {
                if (images[i] == null || images[i].Length == 0) { Debug.LogError($"Image {i} is empty"); return; }
                int len = images[i].Length;
                byte[] lenBytes = BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(len));
                await stream.WriteAsync(lenBytes, 0, 4);
                await stream.WriteAsync(images[i], 0, len);
                Debug.Log($"[Unity] Sent image {i} bytes={len}");
            }
            await stream.FlushAsync();

            // 3) read response length (BE int32) then JSON
            int respLen = ReadInt32BE(stream);
            Debug.Log($"[Unity] Expecting response bytes={respLen}");
            byte[] resp = ReadExact(stream, respLen);
            string json = Encoding.UTF8.GetString(resp);
            Debug.Log($"[Unity] JSON: {json}");

            var result = JsonUtility.FromJson<OrientResult>(json);
            if (!string.IsNullOrEmpty(result.error))
                Debug.LogError("[Unity] Server error: " + result.error);
            else
                Debug.Log($"[Unity] phi={result.phi}, theta={result.theta}, delta={result.delta}, conf={result.confidence}");
        }
        catch (Exception e)
        {
            Debug.LogError("[Unity] Send/Receive failed: " + e);
        }
    }

    private static int ReadInt32BE(NetworkStream s)
    {
        byte[] b = ReadExact(s, 4);
        return System.Net.IPAddress.NetworkToHostOrder(BitConverter.ToInt32(b, 0));
    }

    private static byte[] ReadExact(NetworkStream s, int len)
    {
        byte[] buf = new byte[len];
        int off = 0;
        while (off < len)
        {
            int r = s.Read(buf, off, len - off);
            if (r == 0) throw new EndOfStreamException("Connection closed.");
            off += r;
        }
        return buf;
    }

    private void OnApplicationQuit()
    {
        stream?.Close();
        client?.Close();
    }
}





















// using UnityEngine;
// using System.Net.Sockets;
// using System.IO;
// using System.Text;
// using System.Threading.Tasks;
// using System.Collections;
// using System;
// using System.Collections.Generic;



// public class OrthogonalScreenshotSenderOrientAnything : MonoBehaviour
// {
//     public GameObject targetObject, tmp;
//     public GameObject highlightCubePrefab;  // Assign a small red transparent cube prefab
//     public int imageSize = 1024;//4*256;
//     public string textPrompt = "wheel";     // You can update this dynamically
//     public float coef = 1.0f;

//     private Camera camXY, camYZ, camZX, mainCamera;
//     private TcpClient client;
//     private NetworkStream stream;
//     private GameObject lastHighlight;
//     private int time_step;
//     public List<GameObject> trackedObjects = new List<GameObject>();

//     private readonly object streamLock = new object();

//     public GameObject go_ref1;
//     public GameObject mug, hammer, knife, bowl, kitchenknife, apple, banana, bleach, mustard, plate, powerdrill, scissors, screwdrivers, skillet, spatula, spoon, sugar, wood;
//     public List<GameObject> obejcts_in_the_scene;// = new List<GameObject>();


//     public Vector3 targetObjectCentroid, bounding_box_min, bounding_box_size;

//     private Bounds targetObjectBounds;
//     async void Start()
//     {
//         obejcts_in_the_scene.Add(mug);
//         obejcts_in_the_scene.Add(hammer);
//         // obejcts_in_the_scene.Add(knife);
//         obejcts_in_the_scene.Add(bowl);
//         obejcts_in_the_scene.Add(kitchenknife);
//         obejcts_in_the_scene.Add(apple);
//         obejcts_in_the_scene.Add(banana);
//         obejcts_in_the_scene.Add(bleach);
//         obejcts_in_the_scene.Add(mustard);
//         obejcts_in_the_scene.Add(plate);
//         obejcts_in_the_scene.Add(powerdrill);
//         obejcts_in_the_scene.Add(scissors);
//         obejcts_in_the_scene.Add(screwdrivers);
//         obejcts_in_the_scene.Add(skillet);
//         obejcts_in_the_scene.Add(spatula);
//         obejcts_in_the_scene.Add(spoon);
//         obejcts_in_the_scene.Add(sugar);
//         obejcts_in_the_scene.Add(wood);
//         //go_ref1 = GameObject.Find("kitchenknife");
//         trackedObjects.Add(go_ref1);
//         time_step = 0;
//         // Find the main camera
//         mainCamera = Camera.main;
//         if (mainCamera == null)
//         {
//             Debug.LogError("Main Camera not found in the scene.");
//             return;
//         }
//         //SetupCameras();

//         try
//         {
//             client = new TcpClient();
//             await client.ConnectAsync("127.0.0.1", 8000);
//             stream = client.GetStream();
//             Debug.Log("Connected to Python server");
//         }
//         catch (System.Exception e)
//         {
//             Debug.LogError("Failed to connect to Python server: " + e.Message);
//         }
//         try
//         {
//             await Task.Run(() => ListenForRequests());
//         }
//         catch (System.Exception e)
//         {
//             Debug.LogError("Failed to start a listenere: " + e.Message);
//         }
//     }

//     void ListenForRequests()
//     {
//         try
//         {
//             while (true)
//             {
//                 //Debug.Log("p0");
//                 if (stream == null)
//                 {
//                     Debug.LogError("Stream is null. Make sure the client connected successfully.");
//                     return;
//                 }
//                 // Wait for 4-byte message length
//                 Debug.Log("Waiting for message length...");
//                 byte[] lengthBytes = ReadExact(stream, 4);
//                 Debug.Log("Length bytes received");

//                 int messageLength = BitConverter.ToInt32(lengthBytes, 0);
//                 //Debug.Log("p1: " + messageLength.ToString());
//                 // Read message body
//                 byte[] messageBytes = ReadExact(stream, messageLength);
//                 string command = Encoding.UTF8.GetString(messageBytes);
//                 //Debug.Log("p2");

//                 Debug.Log($"Received command from server: {command}");
//                 string objectName = command.Trim().ToLower();
//                 //GameObject tmp = GameObject.Find(command.Trim().ToLower());

//                 //Dispatch Unity-safe actions to the main thread
//                 MainThreadDispatcher.Enqueue(() =>
//                 {
//                     // GameObject tmp = GameObject.Find(objectName);
//                     foreach (GameObject o in obejcts_in_the_scene)
//                     {
//                         if (objectName == o.transform.name)
//                         {
//                             // Debug.LogError("objectName" + objectName.ToString());
//                             tmp = o;
//                             tmp.SetActive(true);
//                             // break;
//                         }
//                         else
//                         {
//                             o.SetActive(false);
//                         }
//                     }
//                     if (tmp != null)
//                         {

//                             //Debug.Log($"Found GameObject: {tmp.name}");

//                             targetObject = tmp;
//                             targetObjectCentroid = GetMeshCentroid(targetObject);
//                             targetObjectBounds = GetObjectBounds(targetObject);
//                             Debug.LogError(targetObjectCentroid);
//                             //Vector3 r = tmp.transform.rotation. //tmp.transform.rotation.eulerAngles;
//                             Quaternion r = tmp.transform.rotation;
//                             Vector3 p = tmp.transform.position;
//                             // string message = $"{q.x},{q.y},{q.z},{q.w}";
//                             string textPrompt = $"{r.x},{r.y},{r.z},{r.w},{p.x},{p.y},{p.z}";
//                             SetupCameras(targetObjectBounds);
//                             StartCoroutine(CaptureAndSend(textPrompt));
//                         }
//                         else
//                         {
//                             Debug.LogWarning($"GameObject '{objectName}' not found.");
//                         }
//                 });
//                 Debug.Log(time_step.ToString());
//                 Debug.Log("p3===========================================================================================");

//                 // Now wait for the final response from Python:
//                 byte[] responseLenBytes = ReadExact(stream, 4);
//                 int responseLength = BitConverter.ToInt32(responseLenBytes, 0);
//                 byte[] responseBytes = ReadExact(stream, responseLength);
//                 string response = Encoding.UTF8.GetString(responseBytes);

//                 Debug.Log("Received response from server: " + response);

//                 // Handle response (e.g., trigger subcube highlight)
//                 string[] parts = response.Split(' ');
//                 if (parts.Length >= 3 &&
//                     int.TryParse(parts[0], out int i) &&
//                     int.TryParse(parts[1], out int j) &&
//                     int.TryParse(parts[2], out int k))
//                 {
//                     MainThreadDispatcher.Enqueue(() => VisualizeSubcube(i, j, k));
//                 }
//                 Debug.Log("p4");
//                 Debug.Log(time_step.ToString());
//                 Debug.Log("p4===========================================================================================");

//             }
        
//         }
//         catch (EndOfStreamException eof)
//         {
//             Debug.Log("Server closed the connection cleanly: " + eof.Message);
//         }
//         catch (Exception e)
//         {
//             Debug.LogError("Error while listening to server: " + e.ToString());
//         }
//     }


//     Vector3 GetMeshCentroid(GameObject obj)
//     {
//         MeshFilter[] meshFilters = obj.GetComponentsInChildren<MeshFilter>();

//         if (meshFilters.Length == 0)
//             return obj.transform.position;

//         Vector3 sum = Vector3.zero;
//         int totalVertices = 0;

//         foreach (MeshFilter mf in meshFilters)
//         {
//             Mesh mesh = mf.sharedMesh;
//             if (mesh == null) continue;

//             Vector3[] vertices = mesh.vertices;
//             foreach (Vector3 vertex in vertices)
//             {
//                 sum += mf.transform.TransformPoint(vertex); // convert to world space
//                 totalVertices++;
//             }
//         }

//         return totalVertices > 0 ? sum / totalVertices : obj.transform.position;
//     }

//     /*void SetupCameras(Bounds b)
//     {
//         Vector3 size = b.size;

//         // For XY view
//         SetCamera(camXY, b.center + Vector3.back * (size.z * 3), 
//                 Quaternion.LookRotation(Vector3.forward, Vector3.up),
//                 size.x, size.y);

//         // For YZ view
//         SetCamera(camYZ, b.center + Vector3.right * (size.x * 3),
//                 Quaternion.LookRotation(Vector3.left, Vector3.up),
//                 size.z, size.y);

//         // For ZX view
//         SetCamera(camZX, b.center + Vector3.up * (size.y * 3),
//                 Quaternion.LookRotation(Vector3.down, Vector3.forward),
//                 size.x, size.z);
//     }

//     void SetCamera(Camera cam, Vector3 pos, Quaternion rot, float width, float height)
//     {
//         cam.transform.position = pos;
//         cam.transform.rotation = rot;

//         float aspect = width / height;
//         int texWidth = Mathf.RoundToInt(imageSize * aspect);
//         int texHeight = imageSize;

//         cam.targetTexture = new RenderTexture(texWidth, texHeight, 24);
//         cam.orthographicSize = height / 2f;  // fit tightly on Y
//         cam.aspect = aspect;
//     }*/


//     void SetupCameras(Bounds b)
//     {
//         camXY = CreateCamera("CamXY");
//         camYZ = CreateCamera("CamYZ");
//         camZX = CreateCamera("CamZX");

//         //Bounds b = GetObjectBounds(targetObject);
//         float size = Mathf.Max(b.extents.x, b.extents.y, b.extents.z);

        
//         camXY.orthographicSize = size * coef;
//         camYZ.orthographicSize = size * coef;
//         camZX.orthographicSize = size * coef;

//         camXY.transform.position = b.center + Vector3.back * (size * 3);
//         camXY.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);

//         camYZ.transform.position = b.center + Vector3.right * (size * 3);
//         camYZ.transform.rotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
//         Debug.LogError("position: " + camYZ.transform.position.ToString());
//         Debug.LogError("rotation: " + camYZ.transform.eulerAngles.ToString());

//         camZX.transform.position = b.center + Vector3.up * (size * 3);
//         camZX.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
//     }




//     Camera CreateCamera(string name)
//     {
//         GameObject camObj = GameObject.Find(name);
//         if (camObj == null)
//         {
//             camObj = new GameObject(name);
//             camObj.transform.parent = transform;
//         }

//         Camera cam = camObj.GetComponent<Camera>();
//         if (cam == null)
//         {
//             cam = camObj.AddComponent<Camera>();
//         }

//         cam.orthographic = true;
//         cam.clearFlags = CameraClearFlags.SolidColor;
//         cam.backgroundColor = Color.clear;
//         cam.cullingMask = LayerMask.GetMask("Default");
//         cam.enabled = false;
//         cam.targetTexture = new RenderTexture(imageSize, imageSize, 24);

//         Debug.Log("cam.targetTexture is: ");
//         Debug.Log(imageSize);

//         return cam;
//     }



//     Bounds GetObjectBounds(GameObject obj)
//     {
//         Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
//         if (renderers.Length == 0)
//         {
//             Debug.LogWarning("No renderers found in target object.");
//             return new Bounds(Vector3.zero, Vector3.one);
//         }

//         Bounds bounds = renderers[0].bounds;
//         foreach (var r in renderers)
//             bounds.Encapsulate(r.bounds);
//         return bounds;
//     }

//     void Update()
//     {
//     }


//     public byte[] CameraView(string object_of_interest)
//     {
        

//         GameObject target = GameObject.Find(object_of_interest);
//         if (target == null || mainCamera == null)
//         {
//             Debug.LogError("Target object or camera is not set/found.");
//             return null;
//         }

//         // Point the camera at the object
//         mainCamera.transform.LookAt(target.transform);

//         // Create a RenderTexture and set it to the camera
//         int width = 1024;
//         int height = 1024;
//         RenderTexture rt = new RenderTexture(width, height, 24);
//         mainCamera.targetTexture = rt;

//         // Render the camera's view
//         mainCamera.Render();

//         // Read the RenderTexture contents into a Texture2D
//         RenderTexture.active = rt;
//         Texture2D image = new Texture2D(width, height, TextureFormat.RGB24, false);
//         image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
//         image.Apply();

//         // Reset active RenderTexture and clean up
//         mainCamera.targetTexture = null;
//         RenderTexture.active = null;
//         Destroy(rt);

//         // Encode image to PNG (can be JPEG if preferred)
//         byte[] pngData = image.EncodeToPNG();  // or image.EncodeToJPG()
//         Destroy(image);

//         // Return the data as byte[][]
//         return pngData;
//     }


//     IEnumerator CaptureAndSend(string r)
//     {
//         byte[][] images = new byte[4][];

//         images[0] = CaptureCamera(camXY);
//         yield return null;

//         images[1] = CaptureCamera(camYZ);
//         yield return null;

//         images[2] = CaptureCamera(camZX);
//         yield return null;

//         images[3] = CameraView(targetObject.name);

//         Task.Run(() => SendImagesAndPrompt(images, r, targetObjectCentroid, targetObjectBounds.min, targetObjectBounds.size));
//     }

//     byte[] CaptureCamera(Camera cam)
//     {
//         RenderTexture rt = cam.targetTexture;
//         Debug.Log(cam.targetTexture);
//         cam.Render();

//         RenderTexture.active = rt;
//         Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
//         tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
//         tex.Apply();
//         RenderTexture.active = null;

//         byte[] bytes = tex.EncodeToPNG();
//         Destroy(tex);
//         return bytes;
//     }

//     private byte[] ReadExact(NetworkStream stream, int length)
//     {
//         byte[] buffer = new byte[length];
//         int offset = 0;
//         while (offset < length)
//         {
//             int bytesRead = stream.Read(buffer, offset, length - offset);
//             if (bytesRead == 0)
//             {
//                 // Stream closed cleanly
//                 throw new EndOfStreamException("Server closed the connection.");
//             }
//             offset += bytesRead;
//         }
//         return buffer;
//     }




//     void SendImagesAndPrompt(byte[][] images, string rotation_position, Vector3 centroid, Vector3 box_min, Vector3 box_size)
//     {
//         try
//         {
//             // Send 4 images
//             foreach (var img in images)
//             {
//                 byte[] lengthBytes_image = BitConverter.GetBytes(img.Length);
//                 stream.Write(lengthBytes_image, 0, 4);
//                 stream.Write(img, 0, img.Length);
//                 stream.Flush();
//             }

//             // Send rotation quaternions (convert to bytes and prefix with its length)
//             byte[] promptBytes = System.Text.Encoding.UTF8.GetBytes(rotation_position);
//             byte[] promptLengthBytes = BitConverter.GetBytes(promptBytes.Length);
//             stream.Write(promptLengthBytes, 0, 4);
//             stream.Write(promptBytes, 0, promptBytes.Length);

//             // Send 3 Vector3 values (each has 3 floats = 12 bytes, total = 36 bytes)
//             Vector3[] vectors = new Vector3[] { centroid, box_min, box_size };
//             foreach (var vec in vectors)
//             {
//                 stream.Write(BitConverter.GetBytes(vec.x), 0, 4);
//                 stream.Write(BitConverter.GetBytes(vec.y), 0, 4);
//                 stream.Write(BitConverter.GetBytes(vec.z), 0, 4);
//             }


//             stream.Flush();
//             Debug.Log("Sent images, vectors, and prompt to Python");
//         }
//         catch (System.Exception e)
//         {
//             Debug.LogError("SendImagesAndPrompt failed: " + e.Message);
//         }
//     }



//     void VisualizeSubcube(int i, int j, int k)
//     {
//         Bounds bounds = GetObjectBounds(targetObject);
//         Vector3 min = bounds.min;
//         Vector3 size = bounds.size;
//         Vector3 cubeSize = size / 3f;

//         Vector3 center = min + new Vector3(
//             (i + 0.5f) * cubeSize.x,
//             (j + 0.5f) * cubeSize.y,
//             (k + 0.5f) * cubeSize.z
//         );

//         if (lastHighlight != null) Destroy(lastHighlight);
//         lastHighlight = Instantiate(highlightCubePrefab, center, Quaternion.identity);
//         lastHighlight.transform.localScale = cubeSize * 0.9f;
//     }

//     private void OnApplicationQuit()
//     {
//         stream?.Close();
//         client?.Close();
//     }
// }






