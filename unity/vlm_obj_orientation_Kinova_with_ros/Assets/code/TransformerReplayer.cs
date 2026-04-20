using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;              // NEW
using UnityEngine;
using UnityEngine.SceneManagement;

public class TransformReplayer : MonoBehaviour
{
    [Header("Frame Source (choose one)")]
    [Tooltip("If provided, each line is a frame. Leave null to use File Path instead.")]
    public TextAsset framesText;

    [Tooltip("If Frames Text is null, this file path will be used. " +
             "Can be absolute or relative to Application.streamingAssetsPath.")]
    public string filePathRelativeToStreamingAssets = "trajectories/example.txt";

    [Header("Options")]
    [Tooltip("Log warnings when an object name in the frame cannot be found in the scene.")]
    public bool logMissingObjects = true;

    [Tooltip("Automatically rebuild the scene object cache on scene change.")]
    public bool autoRebuildCacheOnSceneLoaded = true;

    // ---------- NEW: Multi-file / per-object cycling ----------
    [Header("Trajectory File Index")]
    [Tooltip("Folder (under StreamingAssets) to scan for *.txt trajectory files.")]
    public string trajectoriesFolderRelativeToStreamingAssets = "trajectories";

    [Tooltip("Only files whose object key (first token before '_') matches this will be cycled with N.")]
    public string activeObjectFilter = "apple";

    [Tooltip("If true, scan subdirectories of the trajectories folder.")]
    public bool includeSubfolders = false;

    private readonly List<string> _allRelativePaths = new List<string>();   // e.g., "trajectories/apple_left_1.txt"
    private readonly List<string> _filteredRelativePaths = new List<string>();
    private int _fileIndexInFilter = -1;  // which file (within filtered list) is active
    // ----------------------------------------------------------

    private readonly Dictionary<string, Transform> _nameToTransform = new Dictionary<string, Transform>(StringComparer.Ordinal);
    private readonly List<string> _frames = new List<string>();
    private int _frameIndex = -1; // nothing applied yet

    void Awake()
    {
        BuildSceneObjectCache();
        BuildTrajectoryIndex();                  // NEW: discover all files
        ApplyFilterAndSnapToCurrentFile();       // NEW: build filtered list and align index
        LoadFrames();                            // load initial file (or TextAsset)
        if (autoRebuildCacheOnSceneLoaded)
        {
            SceneManager.sceneLoaded += (_, __) => BuildSceneObjectCache();
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            StepNextFrame();
        }
        if (Input.GetKeyDown(KeyCode.N))
        {
            LoadNextFileForActiveObject();
        }
    }

    // ---------- NEW: File scanning & filtering ----------
    private void BuildTrajectoryIndex()
    {
        _allRelativePaths.Clear();

        string baseFolder = Path.Combine(Application.streamingAssetsPath, trajectoriesFolderRelativeToStreamingAssets);
        if (!Directory.Exists(baseFolder))
        {
            Debug.LogWarning($"Trajectories folder not found: {baseFolder}");
            return;
        }

        var option = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var absolutePaths = Directory.GetFiles(baseFolder, "*.txt", option);

        foreach (var abs in absolutePaths)
        {
            // Store as relative-to-StreamingAssets so your existing loader works
            string rel = MakeStreamingAssetsRelative(abs);
            _allRelativePaths.Add(rel);
        }

        // Stable ordering for predictable cycling
        _allRelativePaths.Sort(StringComparer.OrdinalIgnoreCase);

        Debug.Log($"Indexed {_allRelativePaths.Count} trajectory files under '{baseFolder}'.");
    }

    private string MakeStreamingAssetsRelative(string absolutePath)
    {
        string root = Application.streamingAssetsPath;
        if (!absolutePath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return absolutePath; // already absolute / outside SA
        string rel = absolutePath.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return rel.Replace(Path.DirectorySeparatorChar, '/'); // unify separators
    }

    // Extract object key as the substring before the first underscore of the filename (without extension)
    private static string ExtractObjectKey(string relativePath)
    {
        string file = Path.GetFileNameWithoutExtension(relativePath);
        int idx = file.IndexOf('_');
        return (idx > 0) ? file.Substring(0, idx) : file; // if no underscore, whole name is the key
    }

    private void ApplyFilterAndSnapToCurrentFile()
    {
        _filteredRelativePaths.Clear();

        // Build filtered list
        foreach (var rel in _allRelativePaths)
        {
            if (string.Equals(ExtractObjectKey(rel), activeObjectFilter, StringComparison.OrdinalIgnoreCase))
                _filteredRelativePaths.Add(rel);
        }
        _filteredRelativePaths.Sort(StringComparer.OrdinalIgnoreCase);

        if (_filteredRelativePaths.Count == 0)
        {
            Debug.LogWarning($"No files match activeObjectFilter='{activeObjectFilter}'.");
            _fileIndexInFilter = -1;
            return;
        }

        // Align the filter index with the currently selected file if possible
        int idx = _filteredRelativePaths.FindIndex(rel =>
            string.Equals(rel, filePathRelativeToStreamingAssets, StringComparison.OrdinalIgnoreCase));

        _fileIndexInFilter = (idx >= 0) ? idx : 0;
        if (idx < 0)
        {
            // Snap current file to the first matching file for this object
            SwitchToFile(_filteredRelativePaths[_fileIndexInFilter], announce:false);
        }
    }

    public void LoadNextFileForActiveObject()
    {
        if (_filteredRelativePaths.Count == 0)
        {
            Debug.LogWarning($"Cannot advance: no files for activeObjectFilter='{activeObjectFilter}'.");
            return;
        }

        _fileIndexInFilter = (_fileIndexInFilter + 1) % _filteredRelativePaths.Count;
        SwitchToFile(_filteredRelativePaths[_fileIndexInFilter], announce:true);
    }

    public void SetActiveObjectFilter(string newObjectKey, bool snapToFirst = true)
    {
        if (string.IsNullOrWhiteSpace(newObjectKey)) return;
        activeObjectFilter = newObjectKey.Trim();
        ApplyFilterAndSnapToCurrentFile();
        if (snapToFirst && _filteredRelativePaths.Count > 0)
        {
            _fileIndexInFilter = 0;
            SwitchToFile(_filteredRelativePaths[_fileIndexInFilter], announce:true);
        }
    }

    private void SwitchToFile(string relativePath, bool announce)
    {
        filePathRelativeToStreamingAssets = relativePath;
        _frameIndex = -1;     // reset so Space starts from the first frame
        LoadFrames();         // (re)load frames from the new file
        if (announce)
            Debug.Log($"Switched to '{relativePath}' [{_fileIndexInFilter + 1}/{_filteredRelativePaths.Count}] for object '{activeObjectFilter}'.");
    }
    // ----------------------------------------------------

    public void StepNextFrame()
    {
        if (_frames.Count == 0)
        {
            Debug.LogWarning("No frames loaded.");
            return;
        }

        _frameIndex = (_frameIndex + 1) % _frames.Count;
        ApplyFrame(_frames[_frameIndex]);
        Debug.Log($"Applied frame {_frameIndex + 1}/{_frames.Count}");
    }

    private void LoadFrames()
    {
        _frames.Clear();

        if (framesText != null)
        {
            using (var reader = new StringReader(framesText.text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (!string.IsNullOrEmpty(line))
                        _frames.Add(line);
                }
            }
            Debug.Log($"Loaded {_frames.Count} frames from TextAsset '{framesText.name}'.");
            return;
        }

        // Fallback to file path (StreamingAssets relative or absolute)
        string path = filePathRelativeToStreamingAssets;
        if (!Path.IsPathFullyQualified(path))
        {
            path = Path.Combine(Application.streamingAssetsPath, filePathRelativeToStreamingAssets);
        }

        if (!File.Exists(path))
        {
            Debug.LogError($"Frame file not found: {path}");
            return;
        }

        var lines = File.ReadAllLines(path);
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (!string.IsNullOrEmpty(line))
                _frames.Add(line);
        }
        Debug.Log($"Loaded {_frames.Count} frames from '{path}'.");
    }

    private void BuildSceneObjectCache()
    {
        _nameToTransform.Clear();

        var scene = SceneManager.GetActiveScene();
        var roots = scene.GetRootGameObjects();
        foreach (var root in roots)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var name = t.gameObject.name;
                _nameToTransform[name] = t;
            }
        }

        Debug.Log($"Cached {_nameToTransform.Count} scene objects by name.");
    }

    private void ApplyFrame(string line)
    {
        // Expected: "TRANSFORM|name px py pz rx ry rz rw;name2 ...;"
        if (line.StartsWith("TRANSFORM|", StringComparison.OrdinalIgnoreCase))
            line = line.Substring("TRANSFORM|".Length);

        var segments = line.Split(';');
        var inv = CultureInfo.InvariantCulture;

        for (int i = 0; i < segments.Length; i++)
        {
            var seg = segments[i].Trim();
            if (string.IsNullOrEmpty(seg)) continue;

            var parts = seg.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 8)
            {
                Debug.LogWarning($"Frame segment does not have 8 tokens (name + 7 floats): '{seg}'");
                continue;
            }

            string name = parts[0];

            if (!_nameToTransform.TryGetValue(name, out var tr))
            {
                if (logMissingObjects)
                    Debug.LogWarning($"Object '{name}' not found in scene.");
                continue;
            }

            if (!TryParseFloat(parts[1], inv, out float px) ||
                !TryParseFloat(parts[2], inv, out float py) ||
                !TryParseFloat(parts[3], inv, out float pz) ||
                !TryParseFloat(parts[4], inv, out float rx) ||
                !TryParseFloat(parts[5], inv, out float ry) ||
                !TryParseFloat(parts[6], inv, out float rz) ||
                !TryParseFloat(parts[7], inv, out float rw))
            {
                Debug.LogWarning($"Failed to parse numbers for '{name}' in segment: '{seg}'");
                continue;
            }

            tr.position = new Vector3(px, py, pz);
            tr.rotation = new Quaternion(rx, ry, rz, rw);
        }
    }

    private static bool TryParseFloat(string s, IFormatProvider provider, out float value)
    {
        return float.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, provider, out value);
    }
}












// using System;
// using System.Collections.Generic;
// using System.Globalization;
// using System.IO;
// using UnityEngine;
// using UnityEngine.SceneManagement;

// public class TransformReplayer : MonoBehaviour
// {
//     [Header("Frame Source (choose one)")]
//     [Tooltip("If provided, each line is a frame. Leave null to use File Path instead.")]
//     public TextAsset framesText;

//     [Tooltip("If Frames Text is null, this file path will be used. " +
//              "Can be absolute or relative to Application.streamingAssetsPath.")]
//     public string filePathRelativeToStreamingAssets = "trajectories/example.txt";

//     [Header("Options")]
//     [Tooltip("Log warnings when an object name in the frame cannot be found in the scene.")]
//     public bool logMissingObjects = true;

//     [Tooltip("Automatically rebuild the scene object cache on scene change.")]
//     public bool autoRebuildCacheOnSceneLoaded = true;

//     private readonly Dictionary<string, Transform> _nameToTransform = new Dictionary<string, Transform>(StringComparer.Ordinal);
//     private readonly List<string> _frames = new List<string>();
//     private int _frameIndex = -1; // nothing applied yet

//     void Awake()
//     {
//         BuildSceneObjectCache();
//         LoadFrames();
//         if (autoRebuildCacheOnSceneLoaded)
//         {
//             SceneManager.sceneLoaded += (_, __) => BuildSceneObjectCache();
//         }
//     }

//     void Update()
//     {
//         if (Input.GetKeyDown(KeyCode.Space))
//         {
//             StepNextFrame();
//         }
//     }

//     public void StepNextFrame()
//     {
//         if (_frames.Count == 0)
//         {
//             Debug.LogWarning("No frames loaded.");
//             return;
//         }

//         _frameIndex = (_frameIndex + 1) % _frames.Count;
//         ApplyFrame(_frames[_frameIndex]);
//         Debug.Log($"Applied frame {_frameIndex + 1}/{_frames.Count}");
//     }

//     private void LoadFrames()
//     {
//         _frames.Clear();

//         if (framesText != null)
//         {
//             using (var reader = new StringReader(framesText.text))
//             {
//                 string line;
//                 while ((line = reader.ReadLine()) != null)
//                 {
//                     line = line.Trim();
//                     if (!string.IsNullOrEmpty(line))
//                         _frames.Add(line);
//                 }
//             }
//             Debug.Log($"Loaded {_frames.Count} frames from TextAsset '{framesText.name}'.");
//             return;
//         }

//         // Fallback to file path (StreamingAssets relative or absolute)
//         string path = filePathRelativeToStreamingAssets;
//         if (!Path.IsPathFullyQualified(path))
//         {
//             path = Path.Combine(Application.streamingAssetsPath, filePathRelativeToStreamingAssets);
//         }

//         if (!File.Exists(path))
//         {
//             Debug.LogError($"Frame file not found: {path}");
//             return;
//         }

//         var lines = File.ReadAllLines(path);
//         foreach (var raw in lines)
//         {
//             var line = raw.Trim();
//             if (!string.IsNullOrEmpty(line))
//                 _frames.Add(line);
//         }
//         Debug.Log($"Loaded {_frames.Count} frames from '{path}'.");
//     }

//     private void BuildSceneObjectCache()
//     {
//         _nameToTransform.Clear();

//         // Traverse all root objects in the active scene to collect active hierarchy
//         var scene = SceneManager.GetActiveScene();
//         var roots = scene.GetRootGameObjects();
//         foreach (var root in roots)
//         {
//             foreach (var t in root.GetComponentsInChildren<Transform>(true))
//             {
//                 var name = t.gameObject.name;
//                 // Last one wins if duplicates; you can customize this behavior
//                 _nameToTransform[name] = t;
//             }
//         }

//         Debug.Log($"Cached {_nameToTransform.Count} scene objects by name.");
//     }

//     private void ApplyFrame(string line)
//     {
//         // Expected: "TRANSFORM|name px py pz rx ry rz rw;name2 ...;"
//         if (line.StartsWith("TRANSFORM|", StringComparison.OrdinalIgnoreCase))
//             line = line.Substring("TRANSFORM|".Length);

//         var segments = line.Split(';');
//         var inv = CultureInfo.InvariantCulture;

//         for (int i = 0; i < segments.Length; i++)
//         {
//             var seg = segments[i].Trim();
//             if (string.IsNullOrEmpty(seg)) continue; // skip trailing empty after last ';'

//             // name + 7 floats
//             // We split by whitespace; the first token is the name (no spaces per your format),
//             // the next 7 are px py pz rx ry rz rw
//             var parts = seg.Split((char[])null, StringSplitOptions.RemoveEmptyEntries); // split on whitespace
//             if (parts.Length != 8)
//             {
//                 Debug.LogWarning($"Frame segment does not have 8 tokens (name + 7 floats): '{seg}'");
//                 continue;
//             }

//             string name = parts[0];

//             if (!_nameToTransform.TryGetValue(name, out var tr))
//             {
//                 if (logMissingObjects)
//                     Debug.LogWarning($"Object '{name}' not found in scene.");
//                 continue;
//             }

//             if (!TryParseFloat(parts[1], inv, out float px) ||
//                 !TryParseFloat(parts[2], inv, out float py) ||
//                 !TryParseFloat(parts[3], inv, out float pz) ||
//                 !TryParseFloat(parts[4], inv, out float rx) ||
//                 !TryParseFloat(parts[5], inv, out float ry) ||
//                 !TryParseFloat(parts[6], inv, out float rz) ||
//                 !TryParseFloat(parts[7], inv, out float rw))
//             {
//                 Debug.LogWarning($"Failed to parse numbers for '{name}' in segment: '{seg}'");
//                 continue;
//             }

//             // Apply
//             tr.position = new Vector3(px, py, pz);
//             tr.rotation = new Quaternion(rx, ry, rz, rw);
//         }
//     }

//     private static bool TryParseFloat(string s, IFormatProvider provider, out float value)
//     {
//         return float.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, provider, out value);
//     }
// }
