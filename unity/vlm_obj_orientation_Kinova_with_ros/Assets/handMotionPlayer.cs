using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Loads a random CSV from Resources/humanMotion and animates both hands
/// relative to the "hip" GameObject, ping-ponging at 25 fps.
/// </summary>
public class HandsMotionPlayer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform leftHand;
    [SerializeField] private Transform rightHand;
    [SerializeField] private Transform hip;

    [Header("Settings")]
    [SerializeField] private string resourcesFolder = "humanMotion"; // under Assets/Resources
    [SerializeField] private float frameRate = 25f;

    private List<Vector3> leftPositions = new();
    private List<Vector3> rightPositions = new();
    private int currentIndex;
    private int direction = 1;
    private float frameInterval;
    private float timer;
    public float magnifier;

    void Start()
    {
        frameInterval = 1f / frameRate;
        LoadRandomCSV();
        magnifier = 8f;
    }

    void Update()
    {
        if (leftPositions.Count == 0 || rightPositions.Count == 0) return;

        timer += Time.deltaTime;
        if (timer >= frameInterval)
        {
            timer -= frameInterval;
            ApplyFrame();
            AdvanceIndex();
        }
    }

    /// <summary>Loads one CSV at random from Resources/humanMotion.</summary>
    private void LoadRandomCSV_backup()
    {
        TextAsset[] csvFiles = Resources.LoadAll<TextAsset>(resourcesFolder);
        if (csvFiles.Length == 0)
        {
            Debug.LogError($"No CSV files in Resources/{resourcesFolder}");
            return;
        }

        // TextAsset chosen = csvFiles[Random.Range(0, csvFiles.Length)];
        TextAsset chosen = csvFiles[1];
        Debug.LogWarning(chosen.name);
        ParseCSV(chosen.text);
    }

    static string GetLastAnimPath()
    {
    #if UNITY_EDITOR
        // Project root (parent of Assets)
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.Combine(projectRoot, "last_anim.txt");
    #else
        // In builds, there is no writable "project directory" — fall back:
        return Path.Combine(Application.persistentDataPath, "last_anim.txt");
    #endif
    }
    private void LoadRandomCSV()
    {
        string path = GetLastAnimPath();

        int i = 0;
        try
        {
            if (!File.Exists(path))
                File.WriteAllText(path, "0"); // first run

            string txt = File.ReadAllText(path).Trim();
            int.TryParse(txt, out i);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Failed to read {path}: {e.Message}");
            i = 0;
        }

        TextAsset[] csvFiles = Resources.LoadAll<TextAsset>(resourcesFolder);
        if (csvFiles.Length == 0)
        {
            Debug.LogError($"No CSV files in Resources/{resourcesFolder}");
            return;
        }

        TextAsset chosen = csvFiles[i % csvFiles.Length];
        Debug.LogWarning($"{chosen.name} (i={i})");
        ParseCSV(chosen.text);

        // overwrite with (i+1) % 12
        int next = (i + 1) % 12;
        try
        {
            File.WriteAllText(path, next.ToString());
            Debug.Log($"last_anim.txt saved at: {path}");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Failed to write {path}: {e.Message}");
        }
    }

    private void LoadRandomCSV_1()
    {
        // 1) Read i from last_anim.txt (create it as "0" if missing)
        string path = Path.Combine(Application.persistentDataPath, "last_anim.txt");
        int i = 0;
        try
        {
            if (!File.Exists(path))
                File.WriteAllText(path, "0"); // first run

            string txt = File.ReadAllText(path).Trim();
            int.TryParse(txt, out i);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Failed to read {path}: {e.Message}");
            i = 0;
        }

        // 2) Load CSVs from Resources and use i (example: pick by index)
        TextAsset[] csvFiles = Resources.LoadAll<TextAsset>(resourcesFolder);
        if (csvFiles.Length == 0)
        {
            Debug.LogError($"No CSV files in Resources/{resourcesFolder}");
            return;
        }

        TextAsset chosen = csvFiles[i % csvFiles.Length]; // use i here as needed
        Debug.LogWarning($"{chosen.name} (i={i})");
        ParseCSV(chosen.text);

        // 3) Overwrite last_anim.txt with (i+1) % 12
        int next = (i + 1) % 12;
        try
        {
            File.WriteAllText(path, next.ToString());
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Failed to write {path}: {e.Message}");
        }
    }


    /// <summary>Parses a CSV containing both hands’ positions.</summary>
    private void ParseCSV(string csvText)
    {
        leftPositions.Clear();
        rightPositions.Clear();

        using StringReader reader = new(csvText);
        string headerLine = reader.ReadLine();
        if (string.IsNullOrEmpty(headerLine)) return;

        string[] headers = headerLine.Split(',');

        int lx = System.Array.IndexOf(headers, "unknown:LeftHandEE_pos_x");
        int ly = System.Array.IndexOf(headers, "unknown:LeftHandEE_pos_y");
        int lz = System.Array.IndexOf(headers, "unknown:LeftHandEE_pos_z");
        int rx = System.Array.IndexOf(headers, "unknown:RightHandEE_pos_x");
        int ry = System.Array.IndexOf(headers, "unknown:RightHandEE_pos_y");
        int rz = System.Array.IndexOf(headers, "unknown:RightHandEE_pos_z");

        if (lx < 0 || ly < 0 || lz < 0 || rx < 0 || ry < 0 || rz < 0)
        {
            Debug.LogError("CSV missing hand position columns.");
            return;
        }

        string line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            string[] parts = line.Split(',');
            if (parts.Length <= Mathf.Max(lx, ly, lz, rx, ry, rz)) continue;

            float lxVal = float.Parse(parts[lx]);
            float lyVal = float.Parse(parts[ly]);
            float lzVal = float.Parse(parts[lz]);
            float rxVal = float.Parse(parts[rx]);
            float ryVal = float.Parse(parts[ry]);
            float rzVal = float.Parse(parts[rz]);

            leftPositions.Add(new Vector3(lxVal, lyVal, lzVal));
            rightPositions.Add(new Vector3(rxVal, ryVal, rzVal));
        }

        currentIndex = 0;
        direction = 1;
    }

    /// <summary>Applies the current frame to both hands.</summary>
    private void ApplyFrame()
    {
        leftHand.position = hip.TransformPoint(magnifier * leftPositions[currentIndex]);
        rightHand.position = hip.TransformPoint(magnifier * rightPositions[currentIndex]);
    }

    /// <summary>Moves the playhead and flips direction at the ends.</summary>
    private void AdvanceIndex()
    {
        currentIndex += direction;
        if (currentIndex >= leftPositions.Count)
        {
            currentIndex = leftPositions.Count - 2;
            direction = -1;
        }
        else if (currentIndex < 0)
        {
            currentIndex = 1;
            direction = 1;
        }
    }
}




// using System.Collections;
// using System.Collections.Generic;
// using UnityEngine;
// using System.IO;
// using System.Linq;

// public class handMotionPlayer : MonoBehaviour
// {
//     [SerializeField] private string resourcesFolder = "humanMotion";  // under Assets/Resources
//     [SerializeField] private Transform leftHand, rightHand;
//     [SerializeField] private Transform hip;
//     [SerializeField] private float frameRate = 25f;

//     private List<Vector3> positions = new List<Vector3>();
//     private int currentIndex = 0;
//     private int direction = 1; // 1 = forward, -1 = backward
//     private float frameInterval;
//     private float timer;

//     void Start()
//     {
//         frameInterval = 1f / frameRate;
//         LoadRandomCSV();
//     }

//     void Update()
//     {
//         if (positions.Count == 0) return;

//         timer += Time.deltaTime;
//         if (timer >= frameInterval)
//         {
//             timer -= frameInterval;
//             ApplyFrame();
//             AdvanceIndex();
//         }
//     }

//     void LoadRandomCSV()
//     {
//         // Load all CSV text assets in Resources/humanMotion
//         TextAsset[] csvFiles = Resources.LoadAll<TextAsset>(resourcesFolder);
//         if (csvFiles.Length == 0)
//         {
//             Debug.LogError("No CSV files found in Resources/" + resourcesFolder);
//             return;
//         }

//         TextAsset chosen = csvFiles[Random.Range(0, csvFiles.Length)];
//         ParseCSV(chosen.text);
//     }

//     void ParseCSV(string csvText)
//     {
//         positions.Clear();
//         using (StringReader reader = new StringReader(csvText))
//         {
//             string header = reader.ReadLine();
//             if (string.IsNullOrEmpty(header)) return;

//             string[] headers = header.Split(',');
//             int xIndex = System.Array.IndexOf(headers, "unknown:LeftHandEE_pos_x");
//             int yIndex = System.Array.IndexOf(headers, "unknown:LeftHandEE_pos_y");
//             int zIndex = System.Array.IndexOf(headers, "unknown:LeftHandEE_pos_z");

//             if (xIndex < 0 || yIndex < 0 || zIndex < 0)
//             {
//                 Debug.LogError("CSV missing LeftHandEE columns");
//                 return;
//             }

//             string line;
//             while ((line = reader.ReadLine()) != null)
//             {
//                 if (string.IsNullOrWhiteSpace(line)) continue;
//                 string[] parts = line.Split(',');
//                 if (parts.Length <= Mathf.Max(xIndex, yIndex, zIndex)) continue;

//                 float x = float.Parse(parts[xIndex]);
//                 float y = float.Parse(parts[yIndex]);
//                 float z = float.Parse(parts[zIndex]);
//                 positions.Add(new Vector3(x, y, z));
//             }
//         }

//         currentIndex = 0;
//         direction = 1;
//     }

//     void ApplyFrame()
//     {
//         // local position relative to hip
//         Vector3 localPos = 9f * positions[currentIndex];
//         leftHand.position = hip.TransformPoint(localPos);
//     }

//     void AdvanceIndex()
//     {
//         currentIndex += direction;

//         if (currentIndex >= positions.Count)
//         {
//             currentIndex = positions.Count - 2; // step back in bounds
//             direction = -1;
//         }
//         else if (currentIndex < 0)
//         {
//             currentIndex = 1;
//             direction = 1;
//         }
//     }
// }
