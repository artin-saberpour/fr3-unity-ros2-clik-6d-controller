using UnityEngine;
using System.Net.Sockets;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Collections;
using System;
using System.Collections.Generic;



public class OrthogonalScreenshotSender : MonoBehaviour
{
    public GameObject targetObject, tmp;
    public GameObject highlightCubePrefab;  // Assign a small red transparent cube prefab
    public int imageSize = 1024;//4*256;
    public string textPrompt = "wheel";     // You can update this dynamically
    public float coef = 1.0f;

    private Camera camXY, camYZ, camZX;// mainCamera;
    public Camera mainCamera;
    private TcpClient client;
    private NetworkStream stream;
    private GameObject lastHighlight;
    private int time_step;
    public List<GameObject> trackedObjects = new List<GameObject>();

    private readonly object streamLock = new object();

    public GameObject go_ref1;
    public GameObject mug, hammer, knife, bowl, kitchenknife, apple, banana, bleach, mustard, plate, powerdrill, scissors, screwdrivers, skillet, spatula, spoon, sugar, wood, fork;
    public List<GameObject> obejcts_in_the_scene;


    public Vector3 targetObjectCentroid, bounding_box_min, bounding_box_size;

    private Bounds targetObjectBounds;
    async void Start()
    {
        obejcts_in_the_scene.Add(mug);
        obejcts_in_the_scene.Add(hammer);
        // obejcts_in_the_scene.Add(knife);
        obejcts_in_the_scene.Add(bowl);
        obejcts_in_the_scene.Add(kitchenknife);
        obejcts_in_the_scene.Add(apple);
        obejcts_in_the_scene.Add(banana);
        obejcts_in_the_scene.Add(bleach);
        obejcts_in_the_scene.Add(mustard);
        obejcts_in_the_scene.Add(plate);
        obejcts_in_the_scene.Add(powerdrill);
        obejcts_in_the_scene.Add(scissors);
        obejcts_in_the_scene.Add(screwdrivers);
        obejcts_in_the_scene.Add(skillet);
        obejcts_in_the_scene.Add(spatula);
        obejcts_in_the_scene.Add(spoon);
        obejcts_in_the_scene.Add(sugar);
        obejcts_in_the_scene.Add(wood);
        obejcts_in_the_scene.Add(fork);
        //go_ref1 = GameObject.Find("kitchenknife");
        trackedObjects.Add(go_ref1);
        time_step = 0;
        // Find the main camera
        // mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogError("Main Camera not found in the scene.");
            return;
        }
        //SetupCameras();

        try
        {
            client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", 5000);
            stream = client.GetStream();
            Debug.Log("Connected to Python server");
        }
        catch (System.Exception e)
        {
            Debug.LogError("Failed to connect to Python server: " + e.Message);
        }
        try
        {
            await Task.Run(() => ListenForRequests());
        }
        catch (System.Exception e)
        {
            Debug.LogError("Failed to start a listenere: " + e.Message);
        }
    }

    void ListenForRequests()
    {
        try
        {
            while (true)
            {
                //Debug.Log("p0");
                if (stream == null)
                {
                    Debug.LogError("Stream is null. Make sure the client connected successfully.");
                    return;
                }
                // Wait for 4-byte message length
                Debug.Log("Waiting for message length...");
                byte[] lengthBytes = ReadExact(stream, 4);
                Debug.Log("Length bytes received");

                int messageLength = BitConverter.ToInt32(lengthBytes, 0);
                //Debug.Log("p1: " + messageLength.ToString());
                // Read message body
                byte[] messageBytes = ReadExact(stream, messageLength);
                string command = Encoding.UTF8.GetString(messageBytes);
                //Debug.Log("p2");

                Debug.Log($"Received command from server: {command}");
                string objectName = command.Trim().ToLower();
                //GameObject tmp = GameObject.Find(command.Trim().ToLower());

                //Dispatch Unity-safe actions to the main thread
                MainThreadDispatcher.Enqueue(() =>
                {
                    // GameObject tmp = GameObject.Find(objectName);
                    foreach (GameObject o in obejcts_in_the_scene)
                    {
                        if (objectName == o.transform.name)
                        {
                            // Debug.LogError("objectName" + objectName.ToString());
                            tmp = o;
                            tmp.SetActive(true);
                            // break;
                        }
                        else
                        {
                            o.SetActive(false);
                        }
                    }
                    if (tmp != null)
                        {

                            //Debug.Log($"Found GameObject: {tmp.name}");

                            targetObject = tmp;
                            targetObjectCentroid = GetMeshCentroid(targetObject);
                            targetObjectBounds = GetObjectBounds(targetObject);
                            Debug.LogError(targetObjectCentroid);
                            //Vector3 r = tmp.transform.rotation. //tmp.transform.rotation.eulerAngles;
                            Quaternion r = tmp.transform.rotation;
                            Vector3 p = tmp.transform.position;
                            // string message = $"{q.x},{q.y},{q.z},{q.w}";
                            string textPrompt = $"{r.x},{r.y},{r.z},{r.w},{p.x},{p.y},{p.z}";
                            SetupCameras(targetObjectBounds);
                            StartCoroutine(CaptureAndSend(textPrompt));
                        }
                        else
                        {
                            Debug.LogWarning($"GameObject '{objectName}' not found.");
                        }
                });
                Debug.Log(time_step.ToString());
                Debug.Log("p3===========================================================================================");

                // Now wait for the final response from Python:
                byte[] responseLenBytes = ReadExact(stream, 4);
                int responseLength = BitConverter.ToInt32(responseLenBytes, 0);
                byte[] responseBytes = ReadExact(stream, responseLength);
                string response = Encoding.UTF8.GetString(responseBytes);

                Debug.Log("Received response from server: " + response);

                // Handle response (e.g., trigger subcube highlight)
                string[] parts = response.Split(' ');
                if (parts.Length >= 3 &&
                    int.TryParse(parts[0], out int i) &&
                    int.TryParse(parts[1], out int j) &&
                    int.TryParse(parts[2], out int k))
                {
                    MainThreadDispatcher.Enqueue(() => VisualizeSubcube(i, j, k));
                }
                Debug.Log("p4");
                Debug.Log(time_step.ToString());
                Debug.Log("p4===========================================================================================");

            }
        
        }
        catch (EndOfStreamException eof)
        {
            Debug.Log("Server closed the connection cleanly: " + eof.Message);
        }
        catch (Exception e)
        {
            Debug.LogError("Error while listening to server: " + e.ToString());
        }
    }


    Vector3 GetMeshCentroid(GameObject obj)
    {
        MeshFilter[] meshFilters = obj.GetComponentsInChildren<MeshFilter>();

        if (meshFilters.Length == 0)
            return obj.transform.position;

        Vector3 sum = Vector3.zero;
        int totalVertices = 0;

        foreach (MeshFilter mf in meshFilters)
        {
            Mesh mesh = mf.sharedMesh;
            if (mesh == null) continue;

            Vector3[] vertices = mesh.vertices;
            foreach (Vector3 vertex in vertices)
            {
                sum += mf.transform.TransformPoint(vertex); // convert to world space
                totalVertices++;
            }
        }

        return totalVertices > 0 ? sum / totalVertices : obj.transform.position;
    }




    void SetupCameras(Bounds b)
    {
        camXY = CreateCamera("CamXY");
        camYZ = CreateCamera("CamYZ");
        camZX = CreateCamera("CamZX");

        //Bounds b = GetObjectBounds(targetObject);
        float size = Mathf.Max(b.extents.x, b.extents.y, b.extents.z);

        
        camXY.orthographicSize = size * coef;
        camYZ.orthographicSize = size * coef;
        camZX.orthographicSize = size * coef;

        camXY.transform.position = b.center + Vector3.back * (size * 3);
        camXY.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);

        camYZ.transform.position = b.center + Vector3.right * (size * 3);
        camYZ.transform.rotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
        Debug.LogError("position: " + camYZ.transform.position.ToString());
        Debug.LogError("rotation: " + camYZ.transform.eulerAngles.ToString());

        camZX.transform.position = b.center + Vector3.up * (size * 3);
        camZX.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
    }




    Camera CreateCamera(string name)
    {
        GameObject camObj = GameObject.Find(name);
        if (camObj == null)
        {
            camObj = new GameObject(name);
            camObj.transform.parent = transform;
        }

        Camera cam = camObj.GetComponent<Camera>();
        if (cam == null)
        {
            cam = camObj.AddComponent<Camera>();
        }

        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.clear;
        cam.cullingMask = LayerMask.GetMask("Default");
        cam.enabled = false;
        cam.targetTexture = new RenderTexture(imageSize, imageSize, 24);

        Debug.Log("cam.targetTexture is: ");
        Debug.Log(imageSize);

        return cam;
    }



    Bounds GetObjectBounds(GameObject obj)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            Debug.LogWarning("No renderers found in target object.");
            return new Bounds(Vector3.zero, Vector3.one);
        }

        Bounds bounds = renderers[0].bounds;
        foreach (var r in renderers)
            bounds.Encapsulate(r.bounds);
        return bounds;
    }

    void Update()
    {

    }
    

    public byte[] CameraView(string object_of_interest)
    {
        

        GameObject target = GameObject.Find(object_of_interest);
        if (target == null || mainCamera == null)
        {
            Debug.LogError("Target object or camera is not set/found.");
            return null;
        }

        // Point the camera at the object
        mainCamera.transform.LookAt(target.transform);

        // Create a RenderTexture and set it to the camera
        int width = 1024;
        int height = 1024;
        RenderTexture rt = new RenderTexture(width, height, 24);
        mainCamera.targetTexture = rt;

        // Render the camera's view
        mainCamera.Render();

        // Read the RenderTexture contents into a Texture2D
        RenderTexture.active = rt;
        Texture2D image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        image.Apply();

        // Reset active RenderTexture and clean up
        mainCamera.targetTexture = null;
        RenderTexture.active = null;
        Destroy(rt);

        // Encode image to PNG (can be JPEG if preferred)
        byte[] pngData = image.EncodeToPNG();  // or image.EncodeToJPG()
        Destroy(image);

        // Return the data as byte[][]
        return pngData;
    }





    IEnumerator CaptureAndSend(string r)
    {
        byte[][] images = new byte[4][];

        images[0] = CaptureCamera(camXY);
        yield return null;

        images[1] = CaptureCamera(camYZ);
        yield return null;

        images[2] = CaptureCamera(camZX);
        yield return null;

        images[3] = CameraView(targetObject.name);

        Task.Run(() => SendImagesAndPrompt(images, r, targetObjectCentroid, targetObjectBounds.min, targetObjectBounds.size));
    }

    byte[] CaptureCamera(Camera cam)
    {
        RenderTexture rt = cam.targetTexture;
        Debug.Log(cam.targetTexture);
        cam.Render();

        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;

        byte[] bytes = tex.EncodeToPNG();
        Destroy(tex);
        return bytes;
    }

    // private byte[] ReadExact(NetworkStream stream, int length)
    // {
    //     byte[] buffer = new byte[length];
    //     int offset = 0;
    //     while (offset < length)
    //     {
    //         int bytesRead = stream.Read(buffer, offset, length - offset);
    //         if (bytesRead == 0)
    //             throw new IOException("Unexpected EOF from server");
    //         offset += bytesRead;
    //     }
    //     return buffer;
    // }
    private byte[] ReadExact(NetworkStream stream, int length)
    {
        byte[] buffer = new byte[length];
        int offset = 0;
        while (offset < length)
        {
            int bytesRead = stream.Read(buffer, offset, length - offset);
            if (bytesRead == 0)
            {
                // Stream closed cleanly
                throw new EndOfStreamException("Server closed the connection.");
            }
            offset += bytesRead;
        }
        return buffer;
    }



    void SendImagesAndPrompt(byte[][] images, string rotation_position, Vector3 centroid, Vector3 box_min, Vector3 box_size)
    {
        try
        {
            // Send 4 images
            foreach (var img in images)
            {
                byte[] lengthBytes_image = BitConverter.GetBytes(img.Length);
                stream.Write(lengthBytes_image, 0, 4);
                stream.Write(img, 0, img.Length);
                stream.Flush();
            }

            // Send rotation quaternions (convert to bytes and prefix with its length)
            byte[] promptBytes = System.Text.Encoding.UTF8.GetBytes(rotation_position);
            byte[] promptLengthBytes = BitConverter.GetBytes(promptBytes.Length);
            stream.Write(promptLengthBytes, 0, 4);
            stream.Write(promptBytes, 0, promptBytes.Length);

            // Send 3 Vector3 values (each has 3 floats = 12 bytes, total = 36 bytes)
            Vector3[] vectors = new Vector3[] { centroid, box_min, box_size };
            foreach (var vec in vectors)
            {
                stream.Write(BitConverter.GetBytes(vec.x), 0, 4);
                stream.Write(BitConverter.GetBytes(vec.y), 0, 4);
                stream.Write(BitConverter.GetBytes(vec.z), 0, 4);
            }


            stream.Flush();
            Debug.Log("Sent images, vectors, and prompt to Python");
        }
        catch (System.Exception e)
        {
            Debug.LogError("SendImagesAndPrompt failed: " + e.Message);
        }
    }



    void VisualizeSubcube(int i, int j, int k)
    {
        Bounds bounds = GetObjectBounds(targetObject);
        Vector3 min = bounds.min;
        Vector3 size = bounds.size;
        Vector3 cubeSize = size / 3f;

        Vector3 center = min + new Vector3(
            (i + 0.5f) * cubeSize.x,
            (j + 0.5f) * cubeSize.y,
            (k + 0.5f) * cubeSize.z
        );

        if (lastHighlight != null) Destroy(lastHighlight);
        lastHighlight = Instantiate(highlightCubePrefab, center, Quaternion.identity);
        lastHighlight.transform.localScale = cubeSize * 0.9f;
    }

    private void OnApplicationQuit()
    {
        stream?.Close();
        client?.Close();
    }
}






















































