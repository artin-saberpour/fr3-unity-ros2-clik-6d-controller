using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor; // for AssetDatabase.Refresh so files appear in Project window
#endif

public class TrajectoryBatchReplayer : MonoBehaviour
{
    [Header("Filtering (for playback only)")]
    [Tooltip("Only play .txt files whose names start with this (e.g., 'apple'). Case-insensitive.")]
    public string activeObjectFilter = "apple";

    [Header("File Source")]
    [Tooltip("Folder relative to StreamingAssets that contains txt trajectories.")]
    public string folderRelativeToStreamingAssets = "trajectories";
    [Tooltip("File extension to search for.")]
    public string fileExtension = "*.txt";

    [Header("Playback")]
    public bool autoPlay = false;
    public float frameIntervalSeconds = 0.1f;
    public bool loopFrames = true;
    public bool loopFiles = true;

    [Header("Logging / Output")]
    public bool logMissingObjects = false;
    public bool logLoadedFiles = true;
    public bool logPerFrameAverageAngle = true;

    [Header("Export")]
    [Tooltip("Exports will be written to: Application.persistentDataPath/<exportFolderName>")]
    public string exportFolderName = "avgAnglesSuccessRate";

    // Internal state for playback
    private Dictionary<string, Transform> _nameToTransform = new Dictionary<string, Transform>(StringComparer.Ordinal);
    private List<string> _filePaths = new List<string>();
    private int _fileIndex = -1;

    private List<string> _frames = new List<string>();
    private int _frameIndex = -1;
    private float _timer = 0f;

    private static readonly IFormatProvider Inv = CultureInfo.InvariantCulture;

    // ===== Constraint plumbing for PLAYBACK (uses scene Transforms) =====
    private class EvalContext
    {
        public Dictionary<string, Transform> map;
        public string activeObj;
        public string selectedHandName; // "left_hand" or "right_hand"
    }

    private struct VecPair
    {
        public Func<EvalContext, Vector3?> Source;
        public Func<EvalContext, Vector3?> Target;
    }

    private Dictionary<string, List<VecPair>> _constraints;

    void Awake()
    {
        BuildSceneObjectCache();
        SceneManager.sceneLoaded += (_, __) => BuildSceneObjectCache();

        InitConstraints();
        RefreshFileList();
        LoadNextFile(initial: true);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space)) StepNextFrame();
        if (Input.GetKeyDown(KeyCode.N)) LoadNextFile();

        if (autoPlay && _frames.Count > 0)
        {
            _timer += Time.deltaTime;
            if (_timer >= frameIntervalSeconds)
            {
                _timer = 0f; // fixed typo
                StepNextFrame();
            }
        }
    }

    // ---------- Files (playback) ----------
    private void RefreshFileList()
    {
        _filePaths.Clear();

        string baseFolder = Path.Combine(Application.streamingAssetsPath, folderRelativeToStreamingAssets);
        if (!Directory.Exists(baseFolder))
        {
            Debug.LogError($"Trajectory folder not found: {baseFolder}");
            return;
        }

        var all = Directory.GetFiles(baseFolder, fileExtension, SearchOption.TopDirectoryOnly);
        string prefix = string.IsNullOrWhiteSpace(activeObjectFilter) ? "" : (activeObjectFilter + "_");

        _filePaths = all
            .Where(p =>
            {
                var name = Path.GetFileName(p);
                return string.IsNullOrEmpty(prefix)
                    ? true
                    : name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (logLoadedFiles)
        {
            Debug.Log($"Found {_filePaths.Count} files matching '{prefix}*' in {baseFolder}");
            foreach (var p in _filePaths) Debug.Log($"  {Path.GetFileName(p)}");
        }
    }

    private void LoadNextFile(bool initial = false)
    {
        if (_filePaths.Count == 0)
        {
            if (initial) Debug.LogWarning("No trajectory files found for current filter.");
            else Debug.LogWarning("No more files to play.");
            _frames.Clear();
            _fileIndex = -1;
            _frameIndex = -1;
            return;
        }

        _fileIndex = initial ? 0 : _fileIndex + 1;

        if (_fileIndex >= _filePaths.Count)
        {
            if (loopFiles) _fileIndex = 0;
            else { Debug.Log("Reached end of file list."); _fileIndex = _filePaths.Count - 1; return; }
        }

        var path = _filePaths[_fileIndex];
        LoadFramesFromFile(path);
        _frameIndex = -1;
        _timer = 0f;

        var stem = Path.GetFileName(path);
        var activeObjOfFile = ExtractActiveObjectFromFilename(stem);
        var handName = ExtractHandNameFromFilename(stem);
        Debug.Log($"Loaded file [{_fileIndex + 1}/{_filePaths.Count}]: {Path.GetFileName(path)}  (active: {activeObjOfFile}, hand: {handName ?? "unknown"})");
    }

    private void LoadFramesFromFile(string path)
    {
        _frames.Clear();
        try
        {
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (!string.IsNullOrEmpty(line))
                    _frames.Add(line);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to read '{path}': {e.Message}");
        }
    }

    // ---------- Frames (playback) ----------
    public void StepNextFrame()
    {
        if (_frames.Count == 0)
        {
            Debug.LogWarning("No frames in current file.");
            return;
        }

        _frameIndex++;
        if (_frameIndex >= _frames.Count)
        {
            if (loopFrames) _frameIndex = 0;
            else { Debug.Log("Reached last frame of current file."); _frameIndex = _frames.Count - 1; return; }
        }

        var activeObj = GetActiveObjectForCurrentFile();
        ApplyFrame(_frames[_frameIndex], activeObj);

        var result = TryComputeAverageConstraintAngleForCurrentFrame();
        if (logPerFrameAverageAngle)
        {
            string file = Path.GetFileName(_filePaths[_fileIndex]);
            if (result.ok)
                Debug.Log($"[Angles] {file}  Frame {_frameIndex + 1}/{_frames.Count}  Active='{activeObj}'  AvgAngle={result.avgDeg:F2}°");
            else
                Debug.Log($"[Angles] {file}  Frame {_frameIndex + 1}/{_frames.Count}  Active='{activeObj}'  N/A ({result.reason})");
        }
    }

    private (bool ok, float avgDeg, string reason) TryComputeAverageConstraintAngleForCurrentFrame()
    {
        string activeObj = GetActiveObjectForCurrentFile();
        if (string.IsNullOrEmpty(activeObj))
            return (false, 0f, "No active object for current file.");

        if (_constraints == null || !_constraints.ContainsKey(activeObj) || _constraints[activeObj] == null || _constraints[activeObj].Count == 0)
            return (false, 0f, $"No constraints configured for '{activeObj}'.");

        var ctx = new EvalContext
        {
            map = _nameToTransform,
            activeObj = activeObj,
            selectedHandName = GetSelectedHandNameForCurrentFile()
        };

        if (string.IsNullOrEmpty(ctx.selectedHandName))
            return (false, 0f, "Hand not parsed from filename (part 2).");

        if (!_nameToTransform.ContainsKey(ctx.selectedHandName))
            return (false, 0f, $"Hand '{ctx.selectedHandName}' not found in scene.");

        var list = _constraints[activeObj];
        var angles = new List<float>(list.Count);

        foreach (var pair in list)
        {
            var s = pair.Source(ctx);
            var t = pair.Target(ctx);
            if (!s.HasValue || !t.HasValue) continue;

            float angle = Vector3.Angle(s.Value, t.Value);
            if (!float.IsNaN(angle) && !float.IsInfinity(angle))
                angles.Add(angle);
        }

        if (angles.Count == 0) return (false, 0f, "No valid vectors produced by constraints.");
        float sum = 0f; for (int i = 0; i < angles.Count; i++) sum += angles[i];
        return (true, sum / angles.Count, "");
    }

    private string GetActiveObjectForCurrentFile()
    {
        if (_fileIndex < 0 || _fileIndex >= _filePaths.Count) return null;
        return ExtractActiveObjectFromFilename(Path.GetFileName(_filePaths[_fileIndex]));
    }
    private string GetSelectedHandNameForCurrentFile()
    {
        if (_fileIndex < 0 || _fileIndex >= _filePaths.Count) return null;
        return ExtractHandNameFromFilename(Path.GetFileName(_filePaths[_fileIndex]));
    }

    private static string ExtractActiveObjectFromFilename(string filename)
    {
        var stem = Path.GetFileNameWithoutExtension(filename);
        if (string.IsNullOrEmpty(stem)) return null;
        var parts = stem.Split('_');
        return parts.Length >= 1 ? parts[0] : null;
    }
    private static string ExtractHandNameFromFilename(string filename)
    {
        var stem = Path.GetFileNameWithoutExtension(filename);
        if (string.IsNullOrEmpty(stem)) return null;
        var parts = stem.Split('_');
        if (parts.Length < 2) return null;

        var token = parts[1].Trim().ToLowerInvariant();
        if (token.StartsWith("left")) return "left_hand";
        if (token.StartsWith("right")) return "right_hand";
        return null;
    }

    // ---------- Apply transforms to scene (playback) ----------
    private void ApplyFrame(string line, string activeObjectOfFile)
    {
        if (line.StartsWith("TRANSFORM|", StringComparison.OrdinalIgnoreCase))
            line = line.Substring("TRANSFORM|".Length);

        var segments = line.Split(';');
        bool inBodyPartSection = false;

        for (int i = 0; i < segments.Length; i++)
        {
            var seg = segments[i].Trim();
            if (string.IsNullOrEmpty(seg)) continue;

            var parts = seg.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 8)
            {
                Debug.LogWarning($"Bad segment (need 8 tokens): '{seg}'");
                continue;
            }

            string name = parts[0];
            if (string.Equals(name, "left_hand", StringComparison.Ordinal)) inBodyPartSection = true;

            if (!_nameToTransform.TryGetValue(name, out var tr))
            {
                if (logMissingObjects) Debug.LogWarning($"Object '{name}' not found in scene.");
                continue;
            }

            if (!TryF(parts[1], out float px) ||
                !TryF(parts[2], out float py) ||
                !TryF(parts[3], out float pz) ||
                !TryF(parts[4], out float rx) ||
                !TryF(parts[5], out float ry) ||
                !TryF(parts[6], out float rz) ||
                !TryF(parts[7], out float rw))
            {
                Debug.LogWarning($"Parse error for '{name}' in segment: '{seg}'");
                continue;
            }

            bool isBodyPart = inBodyPartSection;
            bool isActiveObjectForFile = !string.IsNullOrEmpty(activeObjectOfFile) &&
                                         string.Equals(name, activeObjectOfFile, StringComparison.Ordinal);

            if (isBodyPart || isActiveObjectForFile)
            {
                tr.position = new Vector3(px, py, pz);
                tr.rotation = new Quaternion(rx, ry, rz, rw);
            }
        }
    }

    private void BuildSceneObjectCache()
    {
        _nameToTransform.Clear();
        var scene = SceneManager.GetActiveScene();
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                _nameToTransform[t.gameObject.name] = t; // last wins
            }
        }
        Debug.Log($"Cached {_nameToTransform.Count} scene objects by name.");
    }
    // Factory: given an object name, return a delegate Func<EvalContext, Vector3?>
    Func<string, Func<EvalContext, Vector3?>> PosDiff =
    sourceName => ctx =>
    {
        // Look up the source transform
        var sourceGO = GameObject.Find(sourceName);
        if (sourceGO == null) return (Vector3?)null;

        var source = sourceGO.transform;
        var marker = source.Find("marker");
        if (marker == null) return (Vector3?)null;

        var v = marker.position - source.position;
        return v.sqrMagnitude > 1e-12f ? (Vector3?)v.normalized : (Vector3?)null;
    };

    private static bool TryF(string s, out float v) =>
        float.TryParse(s, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, Inv, out v);

    // ================= Constraints (playback) =================

    private void InitConstraints()
    {
        // Helpers
        Func<string, Func<EvalContext, Vector3?>> Fwd = name => ctx => Axis(ctx, name, AxisKind.Forward);
        Func<string, Func<EvalContext, Vector3?>> UpV = name => ctx => Axis(ctx, name, AxisKind.Up);
        Func<string, Func<EvalContext, Vector3?>> RightV = name => ctx => Axis(ctx, name, AxisKind.Right);
        Func<EvalContext, Vector3?> WorldUp = ctx => Vector3.up;
        Func<string, Func<EvalContext, Vector3?>> ObjToSelectedHand = name => ctx => DiffToSelectedHand(ctx, name);

        Func<Func<EvalContext, Vector3?>, Func<EvalContext, Vector3?>> Neg = f => (ctx) =>
        {
            var v = f(ctx);
            return v.HasValue ? (Vector3?)(-v.Value) : null;
        };

        _constraints = new Dictionary<string, List<VecPair>>(StringComparer.OrdinalIgnoreCase)
        {
            ["sugar"] = new List<VecPair> { new VecPair { Source = Fwd("sugar"), Target = WorldUp } },
            ["hammer"] = new List<VecPair> { new VecPair { Source = Fwd("hammer"), Target = Neg(ObjToSelectedHand("hammer")) } },
            ["kitchenknife"] = new List<VecPair> { new VecPair { Source = PosDiff("kitchenknife"), Target = (ObjToSelectedHand("kitchenknife")) } },
            ["mug"] = new List<VecPair> { new VecPair{ Source = UpV("mug"),   Target = WorldUp },
                                                   new VecPair{ Source = RightV("mug"),Target = ObjToSelectedHand("mug") } },
            ["bowl"] = new List<VecPair> { new VecPair { Source = Fwd("bowl"), Target = WorldUp } },
            ["banana"] = new List<VecPair> { new VecPair { Source = RightV("banana"), Target = WorldUp } },
            ["mustard"] = new List<VecPair> { new VecPair { Source = Fwd("mustard"), Target = WorldUp } },
            ["plate"] = new List<VecPair> { new VecPair { Source = Fwd("plate"), Target = WorldUp } },
            ["skillet"] = new List<VecPair> { new VecPair{ Source = Fwd("skillet"), Target = WorldUp },
                                                   new VecPair{ Source = UpV("skillet"), Target = ObjToSelectedHand("skillet") } },
            // ["spoon"]        = new List<VecPair> { new VecPair{ Source = Fwd("spoon"), Target = WorldUp } },
            ["spoon"] = new List<VecPair> { new VecPair { Source = UpV("spoon"), Target = Neg(ObjToSelectedHand("spoon")) } },
            ["fork"] = new List<VecPair> { new VecPair { Source = UpV("fork"), Target = Neg(ObjToSelectedHand("fork")) } },
            ["bleach"] = new List<VecPair> { new VecPair { Source = Fwd("bleach"), Target = WorldUp } },
            ["powerdrill"] = new List<VecPair> { new VecPair { Source = Fwd("powerdrill"), Target = ObjToSelectedHand("powerdrill") } },
            ["screwdriver"] = new List<VecPair> { new VecPair { Source = Fwd("screwdriver"), Target = Neg(ObjToSelectedHand("screwdriver")) } },
            // ["spatula"]      = new List<VecPair> { new VecPair{ Source = UpV("spatula"), Target = WorldUp } },
            ["spatula"] = new List<VecPair> { new VecPair { Source = UpV("spatula"), Target = Neg(ObjToSelectedHand("spatula")) } },
            ["wood"] = new List<VecPair> { new VecPair { Source = Fwd("wood"), Target = WorldUp } },
            // Optional: add "apple" if you have apple_* files and want angles:
            // ["apple"]        = new List<VecPair> { new VecPair{ Source = UpV("apple"), Target = WorldUp } },
        };
    }

    private enum AxisKind { Forward, Up, Right }
    private static Vector3? Axis(EvalContext ctx, string name, AxisKind kind)
    {
        if (!ctx.map.TryGetValue(name, out var tr)) return null;
        Vector3 v = kind switch
        {
            AxisKind.Forward => tr.forward,
            AxisKind.Up => tr.up,
            AxisKind.Right => tr.right,
            _ => tr.forward
        };
        return NormalizeOrNull(v);
    }
    private static Vector3? DiffToSelectedHand(EvalContext ctx, string objName)
    {
        if (!ctx.map.TryGetValue(objName, out var obj)) return null;
        if (string.IsNullOrEmpty(ctx.selectedHandName)) return null;
        if (!ctx.map.TryGetValue(ctx.selectedHandName, out var hand)) return null;

        var v = hand.position - obj.position;
        return NormalizeOrNull(v);
    }
    private static Vector3? NormalizeOrNull(Vector3 v)
    {
        if (v.sqrMagnitude < 1e-10f) return null;
        return v.normalized;
    }

    [ContextMenu("Reload Files (apply current filter)")]
    public void ReloadFiles()
    {
        RefreshFileList();
        LoadNextFile(initial: true);
    }

    // =====================================================================
    // =====================  BATCH EXPORT (ALL FILES)  =====================
    // =====================================================================

    [ContextMenu("Export Avg Angles (All Files)")]
    public void ExportAvgAnglesAllFiles()
    {
        string baseFolder = Path.Combine(Application.streamingAssetsPath, folderRelativeToStreamingAssets);
        if (!Directory.Exists(baseFolder))
        {
            Debug.LogError($"Trajectory folder not found: {baseFolder}");
            return;
        }

        var files = Directory.GetFiles(baseFolder, fileExtension, SearchOption.TopDirectoryOnly);
        if (files.Length == 0)
        {
            Debug.LogWarning("No trajectory files found to export.");
            return;
        }

        // Group by active object (filename part 1)
        var groups = files
            .GroupBy(p => ExtractActiveObjectFromFilename(Path.GetFileName(p)),
                    StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        // ==== WRITE TO Assets/avgAngles ====
        string outDir = Path.Combine(Application.dataPath, "avgAnglesSuccessRate");
        Directory.CreateDirectory(outDir);

        int objectCount = 0, fileCount = 0, frameCount = 0;

        foreach (var grp in groups)
        {
            string objName = grp.Key;
            if (string.IsNullOrEmpty(objName)) continue;

            // Only export for objects that have constraints
            if (_constraints == null || !_constraints.ContainsKey(objName) ||
                _constraints[objName] == null || _constraints[objName].Count == 0)
            {
                Debug.LogWarning($"Skipping object '{objName}': no constraints configured.");
                continue;
            }

            string outPath = Path.Combine(outDir, objName + ".txt");
            using (var sw = new StreamWriter(outPath, false, System.Text.Encoding.UTF8))
            {
                foreach (var path in grp.OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase))
                {
                    string fname = Path.GetFileName(path);
                    string hand = ExtractHandNameFromFilename(fname); // "left_hand"/"right_hand"/null

                    sw.WriteLine(fname);

                    var lines = File.ReadAllLines(path);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var line = lines[i].Trim();
                        if (string.IsNullOrEmpty(line)) { sw.WriteLine("NaN"); continue; }

                        var map = ParseSimFrame(line); // name -> (pos, rot)
                        float? avg = ComputeAvgAngleSim(map, objName, hand);

                        sw.WriteLine(avg.HasValue ? avg.Value.ToString("G9", System.Globalization.CultureInfo.InvariantCulture) : "NaN");
                        frameCount++;
                    }

                    sw.WriteLine(); // blank line between files
                    fileCount++;
                }
            }

            objectCount++;
            Debug.Log($"Wrote: Assets/avgAngles/{objName}.txt");
        }

#if UNITY_EDITOR
        // Make sure new/updated files appear in the Project window
        AssetDatabase.Refresh();
#endif

        Debug.Log($"Export complete → Assets/avgAngles  (objects: {objectCount}, files: {fileCount}, frames: {frameCount})");
    }


    // ===== Minimal "sim" evaluator (no scene needed) =====
    private struct SimTrans { public Vector3 pos; public Quaternion rot; }
    private class SimCtx
    {
        public Dictionary<string, SimTrans> map;
        public string handName; // "left_hand"/"right_hand"
    }
    private struct SimPair
    {
        public Func<SimCtx, Vector3?> Source;
        public Func<SimCtx, Vector3?> Target;
    }
    private Dictionary<string, List<SimPair>> _constraintsSimCache;

    private Dictionary<string, SimTrans> ParseSimFrame(string line)
    {
        var dict = new Dictionary<string, SimTrans>(StringComparer.Ordinal);
        if (line.StartsWith("TRANSFORM|", StringComparison.OrdinalIgnoreCase))
            line = line.Substring("TRANSFORM|".Length);

        var segments = line.Split(';');
        for (int i = 0; i < segments.Length; i++)
        {
            var seg = segments[i].Trim();
            if (string.IsNullOrEmpty(seg)) continue;

            var parts = seg.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 8) continue;

            string name = parts[0];
            if (!TryF(parts[1], out float px) ||
                !TryF(parts[2], out float py) ||
                !TryF(parts[3], out float pz) ||
                !TryF(parts[4], out float rx) ||
                !TryF(parts[5], out float ry) ||
                !TryF(parts[6], out float rz) ||
                !TryF(parts[7], out float rw))
                continue;

            dict[name] = new SimTrans
            {
                pos = new Vector3(px, py, pz),
                rot = new Quaternion(rx, ry, rz, rw)
            };
        }
        return dict;
    }

    private float? ComputeAvgAngleSim(Dictionary<string, SimTrans> map, string activeObj, string handName)
    {
        if (string.IsNullOrEmpty(activeObj)) return null;

        // Lazy-build sim constraint cache once
        if (_constraintsSimCache == null)
            _constraintsSimCache = BuildSimConstraintsFromPlaybackConstraints();

        if (!_constraintsSimCache.TryGetValue(activeObj, out var list) || list == null || list.Count == 0)
            return null;

        var ctx = new SimCtx { map = map, handName = handName };

        var angles = new List<float>(list.Count);
        foreach (var pair in list)
        {
            var s = pair.Source(ctx);
            var t = pair.Target(ctx);
            if (!s.HasValue || !t.HasValue) continue;

            float angle = Vector3.Angle(s.Value, t.Value);
            if (!float.IsNaN(angle) && !float.IsInfinity(angle))
                angles.Add(angle);
        }

        if (angles.Count == 0) return null;
        float sum = 0f; for (int i = 0; i < angles.Count; i++) sum += angles[i];
        return sum / angles.Count;
    }


    // Call this once when you build the SIM constraints.
    // It precomputes the marker's local offset in the scene if available,
    // and at evaluation time reconstructs the marker's world position from SimCtx.
    Func<string, string, Func<SimCtx, Vector3?>> PosDiffSim =
        (objectName, markerChildName) =>
        {
            // Precompute local offset (scene → object local)
            Vector3? markerLocal = null;
            var objGO = GameObject.Find(objectName);
            var objTr = objGO ? objGO.transform : null;
            var markerTr = objTr ? objTr.Find(markerChildName) : null;
            if (objTr && markerTr)
            {
                markerLocal = objTr.InverseTransformPoint(markerTr.position);
            }

            // Return a SIM delegate: (SimCtx -> Vector3?)
            return ctx =>
            {
                // Get object from SIM frame map
                if (!ctx.map.TryGetValue(objectName, out var obj)) return (Vector3?)null;

                // If we precomputed local, reconstruct marker world from SIM pose
                if (markerLocal.HasValue)
                {
                    Vector3 markerWorld = obj.pos + (obj.rot * markerLocal.Value);
                    var v = markerWorld - obj.pos;
                    return NormalizeOrNull(v);
                }

                // Fallback: if marker exists as its own entry in SIM (rare), use it
                if (ctx.map.TryGetValue(markerChildName, out var markerSim))
                {
                    var v = markerSim.pos - obj.pos;
                    return NormalizeOrNull(v);
                }

                return (Vector3?)null;
            };
        };






    private Dictionary<string, List<SimPair>> BuildSimConstraintsFromPlaybackConstraints()
    {
        // Map playback delegates -> sim delegates
        Vector3? AxisSim(SimCtx ctx, string name, AxisKind kind)
        {
            if (!ctx.map.TryGetValue(name, out var tr)) return null;
            Vector3 v = kind switch
            {
                AxisKind.Forward => tr.rot * Vector3.forward,
                AxisKind.Up => tr.rot * Vector3.up,
                AxisKind.Right => tr.rot * Vector3.right,
                _ => tr.rot * Vector3.forward
            };
            return NormalizeOrNull(v);
        }
        Vector3? DiffToSelectedHandSim(SimCtx ctx, string objName)
        {
            if (string.IsNullOrEmpty(ctx.handName)) return null;
            if (!ctx.map.TryGetValue(objName, out var obj)) return null;
            if (!ctx.map.TryGetValue(ctx.handName, out var hand)) return null;
            var v = hand.pos - obj.pos;
            return NormalizeOrNull(v);
        }

        Func<string, Func<SimCtx, Vector3?>> Fwd = name => ctx => AxisSim(ctx, name, AxisKind.Forward);
        Func<string, Func<SimCtx, Vector3?>> UpV = name => ctx => AxisSim(ctx, name, AxisKind.Up);
        Func<string, Func<SimCtx, Vector3?>> RightV = name => ctx => AxisSim(ctx, name, AxisKind.Right);
        Func<SimCtx, Vector3?> WorldUp = ctx => Vector3.up;
        Func<string, Func<SimCtx, Vector3?>> ObjToSelectedHand = name => ctx => DiffToSelectedHandSim(ctx, name);
        Func<Func<SimCtx, Vector3?>, Func<SimCtx, Vector3?>> Neg = f => (ctx) =>
        {
            var v = f(ctx);
            return v.HasValue ? (Vector3?)(-v.Value) : null;
        };

        // var dict = new Dictionary<string, List<SimPair>>(StringComparer.OrdinalIgnoreCase)
        // {
        //     ["sugar"] = new List<SimPair> { new SimPair { Source = Fwd("sugar"), Target = WorldUp } },
        //     ["hammer"] = new List<SimPair> { new SimPair { Source = Fwd("hammer"), Target = Neg(ObjToSelectedHand("hammer")) } },
        //     ["kitchenknife"] = new List<SimPair> { new SimPair { Source = PosDiffSim("kitchenknife", "marker"), Target = (ObjToSelectedHand("kitchenknife")) } },
        //     ["mug"] = new List<SimPair> { new SimPair{ Source = UpV("mug"),   Target = WorldUp },
        //                                            new SimPair{ Source = RightV("mug"),Target = ObjToSelectedHand("mug") } },
        //     ["bowl"] = new List<SimPair> { new SimPair { Source = Fwd("bowl"), Target = WorldUp } },
        //     ["banana"] = new List<SimPair> { new SimPair { Source = RightV("banana"), Target = WorldUp } },
        //     ["mustard"] = new List<SimPair> { new SimPair { Source = Fwd("mustard"), Target = WorldUp } },
        //     ["plate"] = new List<SimPair> { new SimPair { Source = Fwd("plate"), Target = WorldUp } },
        //     ["skillet"] = new List<SimPair> { new SimPair{ Source = Fwd("skillet"), Target = WorldUp },
        //                                            new SimPair{ Source = UpV("skillet"), Target = ObjToSelectedHand("skillet") } },
        //     ["spoon"] = new List<SimPair> { new SimPair { Source = UpV("spoon"), Target = Neg(ObjToSelectedHand("spoon")) } },
        //     ["fork"] = new List<SimPair> { new SimPair { Source = UpV("fork"), Target = Neg(ObjToSelectedHand("fork")) } },
        //     ["bleach"] = new List<SimPair> { new SimPair { Source = Fwd("bleach"), Target = WorldUp } },
        //     ["powerdrill"] = new List<SimPair> { new SimPair { Source = Fwd("powerdrill"), Target = ObjToSelectedHand("powerdrill") } },
        //     ["screwdriver"] = new List<SimPair> { new SimPair { Source = Fwd("screwdriver"), Target = Neg(ObjToSelectedHand("screwdriver")) } },
        //     ["spatula"] = new List<SimPair> { new SimPair { Source = UpV("spatula"), Target = Neg(ObjToSelectedHand("spatula")) } },
        //     ["wood"] = new List<SimPair> { new SimPair { Source = Fwd("wood"), Target = WorldUp } },
        //     // Add more if needed (e.g., ["apple"] = new List<SimPair> {...})
        // };
        var dict = new Dictionary<string, List<SimPair>>(StringComparer.OrdinalIgnoreCase)
        {
            ["sugar"] = new List<SimPair> { new SimPair { Source = Fwd("sugar"), Target = WorldUp } },
            ["hammer"] = new List<SimPair> { new SimPair { Source = Fwd("hammer"), Target = Neg(ObjToSelectedHand("hammer")) } },
            ["kitchenknife"] = new List<SimPair> { new SimPair { Source = PosDiffSim("kitchenknife", "marker"), Target = (ObjToSelectedHand("kitchenknife")) } },
            ["mug"] = new List<SimPair> { new SimPair{ Source = UpV("mug"),   Target = WorldUp } },
            ["bowl"] = new List<SimPair> { new SimPair { Source = Fwd("bowl"), Target = WorldUp } },
            ["banana"] = new List<SimPair> { new SimPair { Source = RightV("banana"), Target = WorldUp } },
            ["mustard"] = new List<SimPair> { new SimPair { Source = Fwd("mustard"), Target = WorldUp } },
            ["plate"] = new List<SimPair> { new SimPair { Source = Fwd("plate"), Target = WorldUp } },
            ["skillet"] = new List<SimPair> { new SimPair{ Source = Fwd("skillet"), Target = WorldUp }},
            ["spoon"] = new List<SimPair> { new SimPair { Source = UpV("spoon"), Target = Neg(ObjToSelectedHand("spoon")) } },
            ["fork"] = new List<SimPair> { new SimPair { Source = UpV("fork"), Target = Neg(ObjToSelectedHand("fork")) } },
            ["bleach"] = new List<SimPair> { new SimPair { Source = Fwd("bleach"), Target = WorldUp } },
            ["powerdrill"] = new List<SimPair> { new SimPair { Source = Fwd("powerdrill"), Target = ObjToSelectedHand("powerdrill") } },
            ["screwdriver"] = new List<SimPair> { new SimPair { Source = Fwd("screwdriver"), Target = Neg(ObjToSelectedHand("screwdriver")) } },
            ["spatula"] = new List<SimPair> { new SimPair { Source = UpV("spatula"), Target = Neg(ObjToSelectedHand("spatula")) } },
            ["wood"] = new List<SimPair> { new SimPair { Source = Fwd("wood"), Target = WorldUp } },
            // Add more if needed (e.g., ["apple"] = new List<SimPair> {...})
        };

        return dict;
    }








// === NEW: thresholds & window (near the other [Header] fields) ===
[Header("Success Criteria (last-N frames)")]
[Tooltip("How many final frames to check per file.")]
public int framesToCheck = 10;
[Tooltip("Distance threshold object->user shoulder (same side as hand).")]
public float successDistanceThreshold = 6f;
[Tooltip("Average angle threshold in degrees (from constraint system).")]
public float successAngleThreshold = 20f;

// === NEW: convenience distance from object to the relevant shoulder in SIM space ===
private float? ComputeDistanceToShoulderSim(
    Dictionary<string, SimTrans> map, string objName, string handName)
{
    if (map == null) return null;
    if (string.IsNullOrEmpty(objName)) return null;

    // pick shoulder by hand; fallback to a generic "shoulder"
    string shoulder =
        string.Equals(handName, "right_hand", StringComparison.Ordinal) ? "right_shoulder" :
        string.Equals(handName, "left_hand",  StringComparison.Ordinal) ? "left_shoulder"  :
        "shoulder";

    if (!map.TryGetValue(objName, out var obj)) return null;

    // try exact-side shoulder first
    if (map.TryGetValue(shoulder, out var sh)) return Vector3.Distance(obj.pos, sh.pos);

    // if we chose side-specific and didn't find it, try generic "shoulder"
    if (!string.Equals(shoulder, "shoulder", StringComparison.Ordinal) &&
        map.TryGetValue("shoulder", out var sh2))
        return Vector3.Distance(obj.pos, sh2.pos);

    return null;
}

// === NEW: Exporter that writes success_rate.txt for all files ===
[ContextMenu("Export Success Rate (last N frames)")]
public void ExportSuccessRateLastNFrames()
{
    string baseFolder = Path.Combine(Application.streamingAssetsPath, folderRelativeToStreamingAssets);
    if (!Directory.Exists(baseFolder))
    {
        Debug.LogError($"Trajectory folder not found: {baseFolder}");
        return;
    }

    var files = Directory.GetFiles(baseFolder, fileExtension, SearchOption.TopDirectoryOnly);
    if (files.Length == 0)
    {
        Debug.LogWarning("No trajectory files found to export success rate.");
        return;
    }

    // Ensure SIM constraints are ready (we reuse your avg-angle logic)
    if (_constraintsSimCache == null)
        _constraintsSimCache = BuildSimConstraintsFromPlaybackConstraints();

    // output directory (same family as your other exporters)
    string outDir = Path.Combine(Application.dataPath, exportFolderName);
    Directory.CreateDirectory(outDir);
    string outPath = Path.Combine(outDir, "success_rate.txt");

    int successCount = 0, failCount = 0;

    using (var sw = new StreamWriter(outPath, false, System.Text.Encoding.UTF8))
    {
        sw.WriteLine($"# Success rate over last {framesToCheck} frames per file");
        sw.WriteLine($"# Distance<th={successDistanceThreshold}, Angle<th={successAngleThreshold}");
        sw.WriteLine();

        // Sort for stable ordering
        foreach (var path in files.OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase))
        {
            string fname = Path.GetFileName(path);
            string objName = ExtractActiveObjectFromFilename(fname);
            string handName = ExtractHandNameFromFilename(fname); // "left_hand"/"right_hand"/null

            // If we don't have constraints for this object, we can't compute the angle → mark unsuccessful
            bool hasConstraints = _constraintsSimCache.TryGetValue(objName ?? "", out var list) &&
                                  list != null && list.Count > 0;

            var rawLines = File.ReadAllLines(path);
            var simFrames = new List<Dictionary<string, SimTrans>>(rawLines.Length);
            for (int i = 0; i < rawLines.Length; i++)
            {
                var line = rawLines[i].Trim();
                simFrames.Add(string.IsNullOrEmpty(line) ? null : ParseSimFrame(line));
            }

            // how many frames to inspect from the end
            int total = simFrames.Count;
            int n = Mathf.Min(framesToCheck, total);

            bool fileSuccess = false;

            for (int i = total - 1; i >= 0 && (total - i) <= n; i--)
            {
                var map = simFrames[i];
                if (map == null) continue;

                // distance to shoulder
                var dist = ComputeDistanceToShoulderSim(map, objName, handName);

                // average angle from your constraints per-frame
                float? avgAngle = hasConstraints ? ComputeAvgAngleSim(map, objName, handName) : null;

                if (dist.HasValue && avgAngle.HasValue)
                {
                    if (dist.Value < successDistanceThreshold &&
                        avgAngle.Value < successAngleThreshold)
                    {
                        fileSuccess = true;
                        break; // one good frame in the final window is enough
                    }
                }
            }

            if (fileSuccess) { successCount++; sw.WriteLine($"{fname}\tSuccess"); }
            else             { failCount++;    sw.WriteLine($"{fname}\tUnsuccessful"); }
        }

        sw.WriteLine();
        sw.WriteLine($"# Summary: Success={successCount}, Unsuccessful={failCount}, Total={successCount + failCount}");
    }

#if UNITY_EDITOR
    UnityEditor.AssetDatabase.Refresh();
#endif

    Debug.Log($"Wrote success report → {outPath}");
}






























    // ===== Baseline1: freeze source at last frame's object orientation, targets remain per-frame =====

    [ContextMenu("Export Avg Angles (Baseline1: frozen source at last frame)")]
    public void ExportAvgAnglesBaseline1()
    {
        string baseFolder = Path.Combine(Application.streamingAssetsPath, folderRelativeToStreamingAssets);
        if (!Directory.Exists(baseFolder))
        {
            Debug.LogError($"Trajectory folder not found: {baseFolder}");
            return;
        }

        var files = Directory.GetFiles(baseFolder, fileExtension, SearchOption.TopDirectoryOnly);
        if (files.Length == 0)
        {
            Debug.LogWarning("No trajectory files found to export.");
            return;
        }

        // Group by active object (filename part 1), just like the existing exporter
        var groups = files
            .GroupBy(p => ExtractActiveObjectFromFilename(Path.GetFileName(p)),
                    StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        // ==== WRITE TO Assets/avgAngles_baseline1 ====
        string outDir = Path.Combine(Application.dataPath, "avgAngles_baseline1");
        Directory.CreateDirectory(outDir);

        // Build the normal "sim" constraint cache once (we'll reuse target definitions)
        if (_constraintsSimCache == null)
            _constraintsSimCache = BuildSimConstraintsFromPlaybackConstraints();

        int objectCount = 0, fileCount = 0, frameCount = 0;

        foreach (var grp in groups)
        {
            string objName = grp.Key;
            if (string.IsNullOrEmpty(objName)) continue;

            // Only export for objects we have constraints for
            if (_constraintsSimCache == null ||
                !_constraintsSimCache.ContainsKey(objName) ||
                _constraintsSimCache[objName] == null ||
                _constraintsSimCache[objName].Count == 0)
            {
                Debug.LogWarning($"[Baseline1] Skipping object '{objName}': no constraints configured.");
                continue;
            }

            string outPath = Path.Combine(outDir, objName + ".txt");
            using (var sw = new StreamWriter(outPath, false, System.Text.Encoding.UTF8))
            {
                foreach (var path in grp.OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase))
                {
                    string fname = Path.GetFileName(path);
                    string hand = ExtractHandNameFromFilename(fname); // "left_hand"/"right_hand"/null
                    sw.WriteLine(fname);

                    var rawLines = File.ReadAllLines(path);

                    // Parse all frames to sim maps
                    var simFrames = new List<Dictionary<string, SimTrans>>(rawLines.Length);
                    for (int i = 0; i < rawLines.Length; i++)
                    {
                        var line = rawLines[i].Trim();
                        if (string.IsNullOrEmpty(line)) { simFrames.Add(null); continue; }
                        simFrames.Add(ParseSimFrame(line));
                    }

                    // Determine frozen (last-frame) rotation for active object
                    Quaternion? frozenRot = null;
                    for (int i = simFrames.Count - 1; i >= 0 && !frozenRot.HasValue; i--)
                    {
                        var map = simFrames[i];
                        if (map == null) continue;
                        if (map.TryGetValue(objName, out var tr))
                            frozenRot = tr.rot;
                    }

                    // If we can't find a last rotation, all frames are NaN for this file
                    if (!frozenRot.HasValue)
                    {
                        for (int i = 0; i < simFrames.Count; i++)
                        {
                            sw.WriteLine("NaN");
                            frameCount++;
                        }
                        sw.WriteLine();
                        fileCount++;
                        continue;
                    }

                    // Build a per-object baseline1 constraint list that:
                    // - SOURCE uses the frozen object rotation's axes
                    // - TARGET uses the same definition as the normal sim constraints (per-frame)
                    var baselinePairs = BuildBaseline1Pairs(objName, frozenRot.Value);

                    // Now evaluate per frame using frozen sources + live targets
                    for (int i = 0; i < simFrames.Count; i++)
                    {
                        var map = simFrames[i];
                        float? avg = ComputeAvgAngleBaseline1(map, objName, hand, baselinePairs);
                        sw.WriteLine(avg.HasValue
                            ? avg.Value.ToString("G9", System.Globalization.CultureInfo.InvariantCulture)
                            : "NaN");
                        frameCount++;
                    }

                    sw.WriteLine(); // blank line between files
                    fileCount++;
                }
            }

    #if UNITY_EDITOR
            UnityEditor.AssetDatabase.Refresh();
    #endif

            Debug.Log($"[Baseline1] Wrote: Assets/avgAngles_baseline1/{objName}.txt");
            objectCount++;
        }

        Debug.Log($"[Baseline1] Export complete → Assets/avgAngles_baseline1  (objects: {objectCount}, files: {fileCount}, frames: {frameCount})");
    }

    // ----- Baseline1 helpers -----

    private struct Baseline1Pair
    {
        public Func<Dictionary<string, SimTrans>, string, Vector3?> FrozenSource; // (frame map, objName) -> vector
        public Func<Dictionary<string, SimTrans>, string, string, Vector3?> Target; // (frame map, objName, handName) -> vector
    }

    private List<Baseline1Pair> BuildBaseline1Pairs(string objName, Quaternion frozenRot)
    {
        // Build target side from the normal sim constraints for this object
        // and replace sources with functions that read from 'frozenRot'.
        if (_constraintsSimCache == null || !_constraintsSimCache.TryGetValue(objName, out var normalList) || normalList == null)
            return new List<Baseline1Pair>();

        // axis from frozen rotation
        Vector3? AxisFromFrozen(AxisKind kind)
        {
            Vector3 v = kind switch
            {
                AxisKind.Forward => frozenRot * Vector3.forward,
                AxisKind.Up      => frozenRot * Vector3.up,
                AxisKind.Right   => frozenRot * Vector3.right,
                _ => frozenRot * Vector3.forward
            };
            return NormalizeOrNull(v);
        }

        // Recreate the same constraint *shape* as in BuildSimConstraintsFromPlaybackConstraints,
        // but we only need to know which axis was used on the source for each object.
        // We mirror the definitions you have there: Source is always an object axis (Fwd/Up/Right).
        var list = new List<Baseline1Pair>();

        void AddAxisVsTarget(AxisKind sourceAxisKind, Func<Dictionary<string, SimTrans>, string, string, Vector3?> targetFn)
        {
            list.Add(new Baseline1Pair
            {
                FrozenSource = (map, obj) => AxisFromFrozen(sourceAxisKind),
                Target = targetFn
            });
        }

        // Targets (per-frame), shared helpers:
        Vector3? WorldUpTarget(Dictionary<string, SimTrans> _, string __, string ___) => Vector3.up;

        Vector3? ObjToSelectedHandTarget(Dictionary<string, SimTrans> map, string objectName, string handName)
        {
            if (string.IsNullOrEmpty(handName)) return null;
            if (!map.TryGetValue(objectName, out var obj)) return null;
            if (!map.TryGetValue(handName, out var hand)) return null;
            var v = hand.pos - obj.pos;
            return NormalizeOrNull(v);
        }

        Vector3? NegObjToSelectedHandTarget(Dictionary<string, SimTrans> map, string objectName, string handName)
        {
            var v = ObjToSelectedHandTarget(map, objectName, handName);
            return v.HasValue ? (Vector3?)(-v.Value) : null;
        }

        // Map per object, mirroring your constraint table (sources are all axes):
        switch (objName.ToLowerInvariant())
        {
            case "sugar":        AddAxisVsTarget(AxisKind.Forward, WorldUpTarget); break;
            case "hammer":       AddAxisVsTarget(AxisKind.Forward, NegObjToSelectedHandTarget); break;
            case "kitchenknife": AddAxisVsTarget(AxisKind.Forward, NegObjToSelectedHandTarget); break;
            case "mug":
                AddAxisVsTarget(AxisKind.Up,    WorldUpTarget);
                AddAxisVsTarget(AxisKind.Right, ObjToSelectedHandTarget);
                break;
            case "bowl":         AddAxisVsTarget(AxisKind.Forward, WorldUpTarget); break;
            case "banana":       AddAxisVsTarget(AxisKind.Right,   WorldUpTarget); break;
            case "mustard":      AddAxisVsTarget(AxisKind.Forward, WorldUpTarget); break;
            case "plate":        AddAxisVsTarget(AxisKind.Forward, WorldUpTarget); break;
            case "skillet":
                AddAxisVsTarget(AxisKind.Forward, WorldUpTarget);
                AddAxisVsTarget(AxisKind.Up,      ObjToSelectedHandTarget);
                break;
            case "spoon":        AddAxisVsTarget(AxisKind.Up,      NegObjToSelectedHandTarget); break;
            case "fork":         AddAxisVsTarget(AxisKind.Up,      NegObjToSelectedHandTarget); break;
            case "bleach":       AddAxisVsTarget(AxisKind.Forward, WorldUpTarget); break;
            case "powerdrill":   AddAxisVsTarget(AxisKind.Forward, ObjToSelectedHandTarget); break;
            case "screwdriver":  AddAxisVsTarget(AxisKind.Forward, NegObjToSelectedHandTarget); break;
            case "spatula":      AddAxisVsTarget(AxisKind.Up,      NegObjToSelectedHandTarget); break;
            case "wood":         AddAxisVsTarget(AxisKind.Forward, WorldUpTarget); break;
            // Uncomment if you also want apple:
            // case "apple":       AddAxisVsTarget(AxisKind.Up,      WorldUpTarget); break;
            default:
                // No baseline rules: leave empty
                break;
        }

        return list;
    }

    private float? ComputeAvgAngleBaseline1(
        Dictionary<string, SimTrans> map,
        string activeObj,
        string handName,
        List<Baseline1Pair> pairs)
    {
        if (pairs == null || pairs.Count == 0) return null;
        if (map == null) return null;

        var angles = new List<float>(pairs.Count);
        foreach (var p in pairs)
        {
            var s = p.FrozenSource(map, activeObj);
            var t = p.Target(map, activeObj, handName);
            if (!s.HasValue || !t.HasValue) continue;

            float angle = Vector3.Angle(s.Value, t.Value);
            if (!float.IsNaN(angle) && !float.IsInfinity(angle))
                angles.Add(angle);
        }

        if (angles.Count == 0) return null;
        float sum = 0f; for (int i = 0; i < angles.Count; i++) sum += angles[i];
        return sum / angles.Count;
    }

}



