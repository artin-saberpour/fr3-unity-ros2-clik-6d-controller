using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class TrajectorySceneBatchEvaluator : MonoBehaviour
{
    [Header("File Source")]
    public string folderRelativeToStreamingAssets = "trajectories";
    public string fileExtension = "*.txt";

    [Header("Output")]
    public string resultsFolderName = "results";

    [Header("Filtering")]
    public string activeObjectFilter = "";

    [Header("Logging")]
    public bool logLoadedFiles = true;
    public bool logMissingObjects = false;
    public bool logPerFileSummary = true;

    private static readonly IFormatProvider Inv = CultureInfo.InvariantCulture;

    private Dictionary<string, Transform> _nameToTransform = new Dictionary<string, Transform>(StringComparer.Ordinal);
    private Dictionary<string, List<VecPair>> _constraints;

    private class EvalContext
    {
        public Dictionary<string, Transform> map;
        public string activeObj;
        public string selectedHandName;
    }

    private struct VecPair
    {
        public Func<EvalContext, Vector3?> Source;
        public Func<EvalContext, Vector3?> Target;
    }

    private struct PoseData
    {
        public Vector3 position;
        public Quaternion rotation;

        public PoseData(Vector3 p, Quaternion r)
        {
            position = p;
            rotation = r;
        }
    }

    private enum AxisKind { Forward, Up, Right }

    private void Awake()
    {
        BuildSceneObjectCache();
        SceneManager.sceneLoaded += (_, __) => BuildSceneObjectCache();
        InitConstraints();
    }

    [ContextMenu("Export Results (Scene Batch, No Replay)")]
    public void ExportResultsSceneBatchNoReplay()
    {
        BuildSceneObjectCache();

        string baseFolder = Path.Combine(Application.streamingAssetsPath, folderRelativeToStreamingAssets);

        // New required output folders:
        string outDirOurs = Path.Combine(Application.dataPath, "trajectory", "ours", "angles");
        string outDirNonAdaptive = Path.Combine(Application.dataPath, "trajectory", "nonAdaptive", "angles");

        Debug.Log($"[BatchEval] Input folder: {baseFolder}");
        Debug.Log($"[BatchEval] Output folder (ours): {outDirOurs}");
        Debug.Log($"[BatchEval] Output folder (nonAdaptive): {outDirNonAdaptive}");

        if (!Directory.Exists(baseFolder))
        {
            Debug.LogError($"[BatchEval] Trajectory folder not found: {baseFolder}");
            return;
        }

        Directory.CreateDirectory(outDirOurs);
        Directory.CreateDirectory(outDirNonAdaptive);

        var allFiles = Directory.GetFiles(baseFolder, fileExtension, SearchOption.TopDirectoryOnly);

        string prefix = string.IsNullOrWhiteSpace(activeObjectFilter) ? "" : (activeObjectFilter + "_");

        var files = allFiles
            .Where(p =>
            {
                var name = Path.GetFileName(p);
                return string.IsNullOrEmpty(prefix) ||
                       name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Debug.Log($"[BatchEval] Matching files: {files.Length}");

        if (files.Length == 0)
        {
            Debug.LogWarning($"[BatchEval] No trajectory files found in {baseFolder} matching '{prefix}*'.");
            return;
        }

        int totalFiles = 0;
        int totalFrames = 0;

        foreach (var path in files)
        {
            string fileName = Path.GetFileName(path);
            string activeObj = ExtractActiveObjectFromFilename(fileName);
            string handName = ExtractHandNameFromFilename(fileName);

            string outPathOurs = Path.Combine(outDirOurs, fileName);
            string outPathNonAdaptive = Path.Combine(outDirNonAdaptive, fileName);

            Debug.Log($"[BatchEval] Processing: {fileName}");
            Debug.Log($"[BatchEval] -> activeObj={activeObj}, handName={handName}");
            Debug.Log($"[BatchEval] -> writing ours to {outPathOurs}");
            Debug.Log($"[BatchEval] -> writing nonAdaptive to {outPathNonAdaptive}");

            int validCountOurs = 0;
            int nanCountOurs = 0;

            int validCountNonAdaptive = 0;
            int nanCountNonAdaptive = 0;

            try
            {
                var rawLines = File.ReadAllLines(path);

                PoseData? frozenActivePose = CaptureActiveObjectPoseFromFrame(rawLines, activeObj, 2); // frame 3 => index 2

                if (!frozenActivePose.HasValue)
                {
                    Debug.LogWarning($"[BatchEval] Could not capture frame 3 pose for active object '{activeObj}' in file '{fileName}'. nonAdaptive output will be NaN.");
                }

                using (var swOurs = new StreamWriter(outPathOurs, false, System.Text.Encoding.UTF8))
                using (var swNonAdaptive = new StreamWriter(outPathNonAdaptive, false, System.Text.Encoding.UTF8))
                {
                    foreach (var raw in rawLines)
                    {
                        string line = raw.Trim();

                        if (string.IsNullOrEmpty(line))
                        {
                            swOurs.WriteLine("NaN");
                            swNonAdaptive.WriteLine("NaN");

                            nanCountOurs++;
                            nanCountNonAdaptive++;
                            totalFrames++;
                            continue;
                        }

                        // 1) Original / adaptive case
                        ApplyFrame(line, activeObj);
                        float? avgOurs = ComputeAverageConstraintAngleScene(activeObj, handName);

                        if (avgOurs.HasValue)
                        {
                            swOurs.WriteLine(avgOurs.Value.ToString("G9", Inv));
                            validCountOurs++;
                        }
                        else
                        {
                            swOurs.WriteLine("NaN");
                            nanCountOurs++;
                        }

                        // 2) Non-adaptive case: same frame content, but force active object pose from frame 3
                        if (frozenActivePose.HasValue)
                        {
                            ForceActiveObjectPose(activeObj, frozenActivePose.Value);
                            float? avgNonAdaptive = ComputeAverageConstraintAngleScene(activeObj, handName);

                            if (avgNonAdaptive.HasValue)
                            {
                                swNonAdaptive.WriteLine(avgNonAdaptive.Value.ToString("G9", Inv));
                                validCountNonAdaptive++;
                            }
                            else
                            {
                                swNonAdaptive.WriteLine("NaN");
                                nanCountNonAdaptive++;
                            }
                        }
                        else
                        {
                            swNonAdaptive.WriteLine("NaN");
                            nanCountNonAdaptive++;
                        }

                        totalFrames++;
                    }
                }

                Debug.Log($"[BatchEval] Saved ours: {outPathOurs}");
                Debug.Log($"[BatchEval] Saved nonAdaptive: {outPathNonAdaptive}");

                if (logPerFileSummary)
                {
                    Debug.Log(
                        $"[BatchEval] Summary for {fileName}: " +
                        $"ours(valid={validCountOurs}, NaN={nanCountOurs}, total={validCountOurs + nanCountOurs}) | " +
                        $"nonAdaptive(valid={validCountNonAdaptive}, NaN={nanCountNonAdaptive}, total={validCountNonAdaptive + nanCountNonAdaptive})"
                    );
                }

                totalFiles++;
            }
            catch (Exception e)
            {
                Debug.LogError($"[BatchEval] Failed processing '{fileName}': {e}");
            }
        }

#if UNITY_EDITOR
        AssetDatabase.Refresh();
#endif

        Debug.Log($"[BatchEval] Done. Files={totalFiles}, Frames={totalFrames}");
        Debug.Log($"[BatchEval] Ours Output={outDirOurs}");
        Debug.Log($"[BatchEval] NonAdaptive Output={outDirNonAdaptive}");
    }

    private PoseData? CaptureActiveObjectPoseFromFrame(string[] rawLines, string activeObj, int frameIndex)
    {
        if (string.IsNullOrEmpty(activeObj))
            return null;

        if (rawLines == null || frameIndex < 0 || frameIndex >= rawLines.Length)
            return null;

        string line = rawLines[frameIndex]?.Trim();
        if (string.IsNullOrEmpty(line))
            return null;

        ApplyFrame(line, activeObj);

        if (!_nameToTransform.TryGetValue(activeObj, out var tr) || tr == null)
            return null;

        return new PoseData(tr.position, tr.rotation);
    }

    private void ForceActiveObjectPose(string activeObj, PoseData pose)
    {
        if (string.IsNullOrEmpty(activeObj))
            return;

        if (!_nameToTransform.TryGetValue(activeObj, out var tr) || tr == null)
            return;

        tr.position = pose.position;
        tr.rotation = pose.rotation;
    }

    private float? ComputeAverageConstraintAngleScene(string activeObj, string handName)
    {
        if (string.IsNullOrEmpty(activeObj))
            return null;

        if (_constraints == null ||
            !_constraints.TryGetValue(activeObj, out var list) ||
            list == null ||
            list.Count == 0)
            return null;

        var ctx = new EvalContext
        {
            map = _nameToTransform,
            activeObj = activeObj,
            selectedHandName = handName
        };

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

        if (angles.Count == 0)
            return null;

        float sum = 0f;
        for (int i = 0; i < angles.Count; i++)
            sum += angles[i];

        return sum / angles.Count;
    }

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
            if (string.Equals(name, "left_hand", StringComparison.Ordinal))
                inBodyPartSection = true;

            if (!_nameToTransform.TryGetValue(name, out var tr))
            {
                if (logMissingObjects)
                    Debug.LogWarning($"Object '{name}' not found in scene.");
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
            bool isActiveObjectForFile =
                !string.IsNullOrEmpty(activeObjectOfFile) &&
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
                _nameToTransform[t.gameObject.name] = t;
            }
        }

        Debug.Log($"[BatchEval] Cached {_nameToTransform.Count} scene objects by name.");
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

    private static bool TryF(string s, out float v)
    {
        return float.TryParse(
            s,
            NumberStyles.Float | NumberStyles.AllowThousands,
            Inv,
            out v
        );
    }

    private static Vector3? NormalizeOrNull(Vector3 v)
    {
        if (v.sqrMagnitude < 1e-10f) return null;
        return v.normalized;
    }

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

    private static Vector3? DiffToSelectedBodyPart(EvalContext ctx, string bodyPartName, string objName)
    {
        if (!ctx.map.TryGetValue(objName, out var obj)) return null;
        if (!ctx.map.TryGetValue(bodyPartName, out var bodyPart)) return null;

        var v = bodyPart.position - obj.position;
        return NormalizeOrNull(v);
    }

    private readonly Func<string, string, Func<EvalContext, Vector3?>> PosDiff =
        (sourceName, markerName) => ctx =>
        {
            if (!ctx.map.TryGetValue(sourceName, out var source))
                return null;

            var marker = source.Find(markerName);
            if (marker == null)
                return null;

            var v = marker.position - source.position;
            return v.sqrMagnitude > 1e-12f ? v.normalized : null;
        };

    private void InitConstraints()
    {
        Func<string, Func<EvalContext, Vector3?>> Fwd = name => ctx => Axis(ctx, name, AxisKind.Forward);
        Func<string, Func<EvalContext, Vector3?>> UpV = name => ctx => Axis(ctx, name, AxisKind.Up);
        Func<string, Func<EvalContext, Vector3?>> RightV = name => ctx => Axis(ctx, name, AxisKind.Right);
        Func<EvalContext, Vector3?> WorldUp = ctx => Vector3.up;
        Func<string, Func<EvalContext, Vector3?>> ObjToSelectedHand = name => ctx => DiffToSelectedHand(ctx, name);
        Func<string, string, Func<EvalContext, Vector3?>> ObjToSelectedBodyPart = (name, bodyPart) => ctx => DiffToSelectedBodyPart(ctx, bodyPart, name);

        Func<Func<EvalContext, Vector3?>, Func<EvalContext, Vector3?>> Neg = f => ctx =>
        {
            var v = f(ctx);
            return v.HasValue ? (Vector3?)(-v.Value) : null;
        };

        _constraints = new Dictionary<string, List<VecPair>>(StringComparer.OrdinalIgnoreCase)
        {
            ["sugar"] = new List<VecPair> { new VecPair { Source = PosDiff("sugar", "upwards"), Target = WorldUp } },
            ["hammer"] = new List<VecPair> { new VecPair { Source = PosDiff("hammer", "handle"), Target = ObjToSelectedHand("hammer") } },
            ["kitchenknife"] = new List<VecPair>
            {
                new VecPair { Source = PosDiff("kitchenknife", "handle"), Target = ObjToSelectedHand("kitchenknife") },
                new VecPair { Source = PosDiff("kitchenknife", "blade"), Target = Neg(ObjToSelectedHand("kitchenknife")) }
            },
            ["mug"] = new List<VecPair>
            {
                new VecPair { Source = UpV("mug"), Target = WorldUp },
                new VecPair { Source = PosDiff("mug", "handle"), Target = ObjToSelectedHand("mug") }
            },
            ["bowl"] = new List<VecPair> { new VecPair { Source = Fwd("bowl"), Target = WorldUp } },
            ["banana"] = new List<VecPair> { new VecPair { Source = RightV("banana"), Target = WorldUp } },
            ["mustard"] = new List<VecPair> { new VecPair { Source = PosDiff("mustard", "upwards"), Target = WorldUp } },
            ["plate"] = new List<VecPair> { new VecPair { Source = Fwd("plate"), Target = WorldUp } },
            ["skillet"] = new List<VecPair>
            {
                new VecPair { Source = PosDiff("skillet", "up"), Target = WorldUp },
                new VecPair { Source = PosDiff("skillet", "handle"), Target = ObjToSelectedHand("skillet") }
            },
            ["spoon"] = new List<VecPair> { new VecPair { Source = PosDiff("spoon", "handle"), Target = ObjToSelectedHand("spoon") } },
            ["fork"] = new List<VecPair> {
                new VecPair { Source = PosDiff("fork", "handle"), Target = ObjToSelectedHand("fork") }
                // new VecPair { Source = PosDiff("fork", "tip"), Target = Neg(ObjToSelectedBodyPart("fork", "torso")) }
            },
            ["bleach"] = new List<VecPair> { new VecPair { Source = Fwd("bleach"), Target = WorldUp } },
            ["powerdrill"] = new List<VecPair> { 
                new VecPair { Source = PosDiff("powerdrill", "bit"), Target = Neg(ObjToSelectedBodyPart("powerdrill", "torso")) },
                new VecPair { Source = PosDiff("powerdrill", "handle"), Target = ObjToSelectedHand("powerdrill") }
            
            },
            ["screwdriver"] = new List<VecPair> { new VecPair { Source = Fwd("screwdriver"), Target = Neg(ObjToSelectedHand("screwdriver")) } },
            ["spatula"] = new List<VecPair> { new VecPair { Source = PosDiff("spatula", "handle"), Target = ObjToSelectedHand("spatula") } },
            ["wood"] = new List<VecPair> { new VecPair { Source = PosDiff("wood", "upwards"), Target = WorldUp } },
        };
    }
}

































































// using System;
// using System.Collections.Generic;
// using System.Globalization;
// using System.IO;
// using System.Linq;
// using UnityEngine;
// using UnityEngine.SceneManagement;

// #if UNITY_EDITOR
// using UnityEditor;
// #endif

// public class TrajectorySceneBatchEvaluator : MonoBehaviour
// {
//     [Header("File Source")]
//     public string folderRelativeToStreamingAssets = "trajectories";
//     public string fileExtension = "*.txt";

//     [Header("Output")]
//     public string resultsFolderName = "results";

//     [Header("Filtering")]
//     public string activeObjectFilter = "";

//     [Header("Logging")]
//     public bool logLoadedFiles = true;
//     public bool logMissingObjects = false;
//     public bool logPerFileSummary = true;

//     private static readonly IFormatProvider Inv = CultureInfo.InvariantCulture;

//     private Dictionary<string, Transform> _nameToTransform = new Dictionary<string, Transform>(StringComparer.Ordinal);
//     private Dictionary<string, List<VecPair>> _constraints;

//     private class EvalContext
//     {
//         public Dictionary<string, Transform> map;
//         public string activeObj;
//         public string selectedHandName;
//     }

//     private struct VecPair
//     {
//         public Func<EvalContext, Vector3?> Source;
//         public Func<EvalContext, Vector3?> Target;
//     }

//     private enum AxisKind { Forward, Up, Right }

//     private void Awake()
//     {
//         BuildSceneObjectCache();
//         SceneManager.sceneLoaded += (_, __) => BuildSceneObjectCache();
//         InitConstraints();
//     }

//     [ContextMenu("Export Results (Scene Batch, No Replay)")]
//     public void ExportResultsSceneBatchNoReplay()
//     {
//         BuildSceneObjectCache();

//         string baseFolder = Path.Combine(Application.streamingAssetsPath, folderRelativeToStreamingAssets);
//         string outDir = Path.Combine(Application.dataPath, resultsFolderName);

//         Debug.Log($"[BatchEval] Input folder: {baseFolder}");
//         Debug.Log($"[BatchEval] Output folder: {outDir}");

//         if (!Directory.Exists(baseFolder))
//         {
//             Debug.LogError($"[BatchEval] Trajectory folder not found: {baseFolder}");
//             return;
//         }

//         Directory.CreateDirectory(outDir);

//         var allFiles = Directory.GetFiles(baseFolder, fileExtension, SearchOption.TopDirectoryOnly);

//         string prefix = string.IsNullOrWhiteSpace(activeObjectFilter) ? "" : (activeObjectFilter + "_");

//         var files = allFiles
//             .Where(p =>
//             {
//                 var name = Path.GetFileName(p);
//                 return string.IsNullOrEmpty(prefix) ||
//                        name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
//             })
//             .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
//             .ToArray();

//         Debug.Log($"[BatchEval] Matching files: {files.Length}");

//         if (files.Length == 0)
//         {
//             Debug.LogWarning($"[BatchEval] No trajectory files found in {baseFolder} matching '{prefix}*'.");
//             return;
//         }

//         int totalFiles = 0;
//         int totalFrames = 0;

//         foreach (var path in files)
//         {
//             string fileName = Path.GetFileName(path);
//             string activeObj = ExtractActiveObjectFromFilename(fileName);
//             string handName = ExtractHandNameFromFilename(fileName);
//             string outPath = Path.Combine(outDir, fileName);

//             Debug.Log($"[BatchEval] Processing: {fileName}");
//             Debug.Log($"[BatchEval] -> activeObj={activeObj}, handName={handName}");
//             Debug.Log($"[BatchEval] -> writing to {outPath}");

//             int validCount = 0;
//             int nanCount = 0;

//             try
//             {
//                 var rawLines = File.ReadAllLines(path);

//                 using (var sw = new StreamWriter(outPath, false, System.Text.Encoding.UTF8))
//                 {
//                     foreach (var raw in rawLines)
//                     {
//                         string line = raw.Trim();

//                         if (string.IsNullOrEmpty(line))
//                         {
//                             sw.WriteLine("NaN");
//                             nanCount++;
//                             totalFrames++;
//                             continue;
//                         }

//                         ApplyFrame(line, activeObj);

//                         float? avg = ComputeAverageConstraintAngleScene(activeObj, handName);

//                         if (avg.HasValue)
//                         {
//                             sw.WriteLine(avg.Value.ToString("G9", Inv));
//                             validCount++;
//                         }
//                         else
//                         {
//                             sw.WriteLine("NaN");
//                             nanCount++;
//                         }

//                         totalFrames++;
//                     }
//                 }

//                 Debug.Log($"[BatchEval] Saved file: {outPath}");

//                 if (logPerFileSummary)
//                 {
//                     Debug.Log($"[BatchEval] Summary for {fileName}: valid={validCount}, NaN={nanCount}, total={validCount + nanCount}");
//                 }

//                 totalFiles++;
//             }
//             catch (Exception e)
//             {
//                 Debug.LogError($"[BatchEval] Failed processing '{fileName}': {e}");
//             }
//         }

// #if UNITY_EDITOR
//         AssetDatabase.Refresh();
// #endif

//         Debug.Log($"[BatchEval] Done. Files={totalFiles}, Frames={totalFrames}, Output={outDir}");
//     }

//     private float? ComputeAverageConstraintAngleScene(string activeObj, string handName)
//     {
//         if (string.IsNullOrEmpty(activeObj))
//             return null;

//         if (_constraints == null ||
//             !_constraints.TryGetValue(activeObj, out var list) ||
//             list == null ||
//             list.Count == 0)
//             return null;

//         var ctx = new EvalContext
//         {
//             map = _nameToTransform,
//             activeObj = activeObj,
//             selectedHandName = handName
//         };

//         var angles = new List<float>(list.Count);

//         foreach (var pair in list)
//         {
//             var s = pair.Source(ctx);
//             var t = pair.Target(ctx);
//             if (!s.HasValue || !t.HasValue) continue;

//             float angle = Vector3.Angle(s.Value, t.Value);
//             if (!float.IsNaN(angle) && !float.IsInfinity(angle))
//                 angles.Add(angle);
//         }

//         if (angles.Count == 0)
//             return null;

//         float sum = 0f;
//         for (int i = 0; i < angles.Count; i++)
//             sum += angles[i];

//         return sum / angles.Count;
//     }

//     private void ApplyFrame(string line, string activeObjectOfFile)
//     {
//         if (line.StartsWith("TRANSFORM|", StringComparison.OrdinalIgnoreCase))
//             line = line.Substring("TRANSFORM|".Length);

//         var segments = line.Split(';');
//         bool inBodyPartSection = false;

//         for (int i = 0; i < segments.Length; i++)
//         {
//             var seg = segments[i].Trim();
//             if (string.IsNullOrEmpty(seg)) continue;

//             var parts = seg.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
//             if (parts.Length != 8)
//             {
//                 Debug.LogWarning($"Bad segment (need 8 tokens): '{seg}'");
//                 continue;
//             }

//             string name = parts[0];
//             if (string.Equals(name, "left_hand", StringComparison.Ordinal))
//                 inBodyPartSection = true;

//             if (!_nameToTransform.TryGetValue(name, out var tr))
//             {
//                 if (logMissingObjects)
//                     Debug.LogWarning($"Object '{name}' not found in scene.");
//                 continue;
//             }

//             if (!TryF(parts[1], out float px) ||
//                 !TryF(parts[2], out float py) ||
//                 !TryF(parts[3], out float pz) ||
//                 !TryF(parts[4], out float rx) ||
//                 !TryF(parts[5], out float ry) ||
//                 !TryF(parts[6], out float rz) ||
//                 !TryF(parts[7], out float rw))
//             {
//                 Debug.LogWarning($"Parse error for '{name}' in segment: '{seg}'");
//                 continue;
//             }

//             bool isBodyPart = inBodyPartSection;
//             bool isActiveObjectForFile =
//                 !string.IsNullOrEmpty(activeObjectOfFile) &&
//                 string.Equals(name, activeObjectOfFile, StringComparison.Ordinal);

//             if (isBodyPart || isActiveObjectForFile)
//             {
//                 tr.position = new Vector3(px, py, pz);
//                 tr.rotation = new Quaternion(rx, ry, rz, rw);
//             }
//         }
//     }

//     private void BuildSceneObjectCache()
//     {
//         _nameToTransform.Clear();

//         var scene = SceneManager.GetActiveScene();
//         foreach (var root in scene.GetRootGameObjects())
//         {
//             foreach (var t in root.GetComponentsInChildren<Transform>(true))
//             {
//                 _nameToTransform[t.gameObject.name] = t;
//             }
//         }

//         Debug.Log($"[BatchEval] Cached {_nameToTransform.Count} scene objects by name.");
//     }

//     private static string ExtractActiveObjectFromFilename(string filename)
//     {
//         var stem = Path.GetFileNameWithoutExtension(filename);
//         if (string.IsNullOrEmpty(stem)) return null;

//         var parts = stem.Split('_');
//         return parts.Length >= 1 ? parts[0] : null;
//     }

//     private static string ExtractHandNameFromFilename(string filename)
//     {
//         var stem = Path.GetFileNameWithoutExtension(filename);
//         if (string.IsNullOrEmpty(stem)) return null;

//         var parts = stem.Split('_');
//         if (parts.Length < 2) return null;

//         var token = parts[1].Trim().ToLowerInvariant();
//         if (token.StartsWith("left")) return "left_hand";
//         if (token.StartsWith("right")) return "right_hand";
//         return null;
//     }

//     private static bool TryF(string s, out float v)
//     {
//         return float.TryParse(
//             s,
//             NumberStyles.Float | NumberStyles.AllowThousands,
//             Inv,
//             out v
//         );
//     }

//     private static Vector3? NormalizeOrNull(Vector3 v)
//     {
//         if (v.sqrMagnitude < 1e-10f) return null;
//         return v.normalized;
//     }

//     private static Vector3? Axis(EvalContext ctx, string name, AxisKind kind)
//     {
//         if (!ctx.map.TryGetValue(name, out var tr)) return null;

//         Vector3 v = kind switch
//         {
//             AxisKind.Forward => tr.forward,
//             AxisKind.Up => tr.up,
//             AxisKind.Right => tr.right,
//             _ => tr.forward
//         };

//         return NormalizeOrNull(v);
//     }

//     private static Vector3? DiffToSelectedHand(EvalContext ctx, string objName)
//     {
//         if (!ctx.map.TryGetValue(objName, out var obj)) return null;
//         if (string.IsNullOrEmpty(ctx.selectedHandName)) return null;
//         if (!ctx.map.TryGetValue(ctx.selectedHandName, out var hand)) return null;

//         var v = hand.position - obj.position;
//         return NormalizeOrNull(v);
//     }

//     private static Vector3? DiffToSelectedBodyPart(EvalContext ctx, string bodyPartName, string objName)
//     {
//         if (!ctx.map.TryGetValue(objName, out var obj)) return null;
//         if (!ctx.map.TryGetValue(bodyPartName, out var bodyPart)) return null;
//         // if (string.IsNullOrEmpty(ctx.selectedHandName)) return null;
//         // if (!ctx.map.TryGetValue(ctx.selectedHandName, out var hand)) return null;

//         var v = bodyPart.position - obj.position;
//         return NormalizeOrNull(v);
//     }

//     private readonly Func<string, string, Func<EvalContext, Vector3?>> PosDiff =
//         (sourceName, markerName) => ctx =>
//         {
//             if (!ctx.map.TryGetValue(sourceName, out var source))
//                 return null;

//             var marker = source.Find(markerName);
//             if (marker == null)
//                 return null;

//             var v = marker.position - source.position;
//             return v.sqrMagnitude > 1e-12f ? v.normalized : null;
//         };

//     private void InitConstraints()
//     {
//         Func<string, Func<EvalContext, Vector3?>> Fwd = name => ctx => Axis(ctx, name, AxisKind.Forward);
//         Func<string, Func<EvalContext, Vector3?>> UpV = name => ctx => Axis(ctx, name, AxisKind.Up);
//         Func<string, Func<EvalContext, Vector3?>> RightV = name => ctx => Axis(ctx, name, AxisKind.Right);
//         Func<EvalContext, Vector3?> WorldUp = ctx => Vector3.up;
//         Func<string, Func<EvalContext, Vector3?>> ObjToSelectedHand = name => ctx => DiffToSelectedHand(ctx, name);
//         Func<string, string, Func<EvalContext, Vector3?>> ObjToSelectedBodyPart = (name, bodyPart) => ctx => DiffToSelectedBodyPart(ctx, bodyPart, name);

//         Func<Func<EvalContext, Vector3?>, Func<EvalContext, Vector3?>> Neg = f => ctx =>
//         {
//             var v = f(ctx);
//             return v.HasValue ? (Vector3?)(-v.Value) : null;
//         };

//         _constraints = new Dictionary<string, List<VecPair>>(StringComparer.OrdinalIgnoreCase)
//         {
//             ["sugar"] = new List<VecPair> { new VecPair { Source = PosDiff("sugar", "upwards"), Target = WorldUp } },
//             ["hammer"] = new List<VecPair> { new VecPair { Source = Fwd("hammer"), Target = Neg(ObjToSelectedHand("hammer")) } },
//             ["kitchenknife"] = new List<VecPair> 
//             { 
//                 new VecPair { Source = PosDiff("kitchenknife", "handle"), Target = ObjToSelectedHand("kitchenknife") },
//                 new VecPair { Source = PosDiff("kitchenknife", "blade"), Target = Neg(ObjToSelectedHand("kitchenknife")) }
//             },
//             ["mug"] = new List<VecPair>
//             {
//                 new VecPair { Source = UpV("mug"), Target = WorldUp },
//                 // new VecPair { Source = RightV("mug"), Target = ObjToSelectedHand("mug") }
//                 new VecPair { Source = PosDiff("mug", "handle"), Target = ObjToSelectedHand("mug") }
//             },
//             ["bowl"] = new List<VecPair> { new VecPair { Source = Fwd("bowl"), Target = WorldUp } },
//             ["banana"] = new List<VecPair> { new VecPair { Source = RightV("banana"), Target = WorldUp } },
//             // ["mustard"] = new List<VecPair> { new VecPair { Source = UpV("mustard"), Target = WorldUp } },
//             ["mustard"] = new List<VecPair> { new VecPair { Source = PosDiff("mustard", "upwards"), Target = WorldUp } },
            
//             ["plate"] = new List<VecPair> { new VecPair { Source = Fwd("plate"), Target = WorldUp } },
//             ["skillet"] = new List<VecPair>
//             {
//                 new VecPair { Source = UpV("skillet"), Target = WorldUp },
//                 new VecPair { Source = RightV("skillet"), Target = ObjToSelectedHand("skillet") }
//             },
//             ["spoon"] = new List<VecPair> { new VecPair { Source = UpV("spoon"), Target = Neg(ObjToSelectedHand("spoon")) } },
//             ["fork"] = new List<VecPair> { 
//                 new VecPair { Source = PosDiff("fork", "handle"), Target = ObjToSelectedHand("fork") },
//                 new VecPair { Source = PosDiff("fork", "tip"), Target = Neg(ObjToSelectedBodyPart("fork", "torso")) }  
//             },
//             ["bleach"] = new List<VecPair> { new VecPair { Source = Fwd("bleach"), Target = WorldUp } },
//             ["powerdrill"] = new List<VecPair> { new VecPair { Source = Fwd("powerdrill"), Target = ObjToSelectedHand("powerdrill") } },
//             ["screwdriver"] = new List<VecPair> { new VecPair { Source = Fwd("screwdriver"), Target = Neg(ObjToSelectedHand("screwdriver")) } },
//             ["spatula"] = new List<VecPair> { new VecPair { Source = UpV("spatula"), Target = Neg(ObjToSelectedHand("spatula")) } },
//             ["wood"] = new List<VecPair> { new VecPair { Source = PosDiff("wood", "upwards"), Target = WorldUp } },
//         };
//     }
// }







































































































// using System;
// using System.Collections.Generic;
// using System.Globalization;
// using System.IO;
// using System.Linq;
// using UnityEngine;
// using UnityEngine.SceneManagement;

// #if UNITY_EDITOR
// using UnityEditor;
// #endif

// public class TrajectorySceneBatchEvaluator : MonoBehaviour
// {
//     [Header("File Source")]
//     [Tooltip("Folder relative to StreamingAssets that contains txt trajectories.")]
//     public string folderRelativeToStreamingAssets = "trajectories";

//     [Tooltip("File extension to search for.")]
//     public string fileExtension = "*.txt";

//     [Header("Output")]
//     [Tooltip("Output will be written to Assets/<resultsFolderName>/")]
//     public string resultsFolderName = "results";

//     [Header("Filtering")]
//     [Tooltip("Optional prefix filter, e.g. 'apple' will only process files starting with 'apple_'. Leave empty for all files.")]
//     public string activeObjectFilter = "";

//     [Header("Logging")]
//     public bool logLoadedFiles = true;
//     public bool logMissingObjects = false;
//     public bool logPerFileSummary = true;

//     private static readonly IFormatProvider Inv = CultureInfo.InvariantCulture;

//     private Dictionary<string, Transform> _nameToTransform = new Dictionary<string, Transform>(StringComparer.Ordinal);
//     private Dictionary<string, List<VecPair>> _constraints;

//     private class EvalContext
//     {
//         public Dictionary<string, Transform> map;
//         public string activeObj;
//         public string selectedHandName;
//     }

//     private struct VecPair
//     {
//         public Func<EvalContext, Vector3?> Source;
//         public Func<EvalContext, Vector3?> Target;
//     }

//     private enum AxisKind { Forward, Up, Right }

//     private void Awake()
//     {
//         BuildSceneObjectCache();
//         SceneManager.sceneLoaded += (_, __) => BuildSceneObjectCache();
//         InitConstraints();
//     }

//     [ContextMenu("Export Results (Scene Batch, No Replay)")]
//     public void ExportResultsSceneBatchNoReplay()
//     {
//         BuildSceneObjectCache();

//         string baseFolder = Path.Combine(Application.streamingAssetsPath, folderRelativeToStreamingAssets);
//         if (!Directory.Exists(baseFolder))
//         {
//             Debug.LogError($"Trajectory folder not found: {baseFolder}");
//             return;
//         }

//         var allFiles = Directory.GetFiles(baseFolder, fileExtension, SearchOption.TopDirectoryOnly);

//         string prefix = string.IsNullOrWhiteSpace(activeObjectFilter) ? "" : (activeObjectFilter + "_");

//         var files = allFiles
//             .Where(p =>
//             {
//                 var name = Path.GetFileName(p);
//                 return string.IsNullOrEmpty(prefix) ||
//                        name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
//             })
//             .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
//             .ToArray();

//         if (files.Length == 0)
//         {
//             Debug.LogWarning($"No trajectory files found in {baseFolder} matching '{prefix}*'.");
//             return;
//         }

//         if (logLoadedFiles)
//         {
//             Debug.Log($"Found {files.Length} files in {baseFolder}");
//             foreach (var p in files)
//                 Debug.Log($"  {Path.GetFileName(p)}");
//         }

//         string outDir = Path.Combine(Application.dataPath, resultsFolderName);
//         Directory.CreateDirectory(outDir);

//         int totalFiles = 0;
//         int totalFrames = 0;

//         foreach (var path in files)
//         {
//             string fileName = Path.GetFileName(path);
//             string activeObj = ExtractActiveObjectFromFilename(fileName);
//             string handName = ExtractHandNameFromFilename(fileName);
//             string outPath = Path.Combine(outDir, fileName);

//             int validCount = 0;
//             int nanCount = 0;

//             try
//             {
//                 var rawLines = File.ReadAllLines(path);

//                 using (var sw = new StreamWriter(outPath, false, System.Text.Encoding.UTF8))
//                 {
//                     foreach (var raw in rawLines)
//                     {
//                         string line = raw.Trim();

//                         if (string.IsNullOrEmpty(line))
//                         {
//                             sw.WriteLine("NaN");
//                             nanCount++;
//                             totalFrames++;
//                             continue;
//                         }

//                         ApplyFrame(line, activeObj);

//                         float? avg = ComputeAverageConstraintAngleScene(activeObj, handName);

//                         if (avg.HasValue)
//                         {
//                             sw.WriteLine(avg.Value.ToString("G9", Inv));
//                             validCount++;
//                         }
//                         else
//                         {
//                             sw.WriteLine("NaN");
//                             nanCount++;
//                         }

//                         totalFrames++;
//                     }
//                 }

//                 if (logPerFileSummary)
//                 {
//                     Debug.Log($"Wrote {outPath} | valid={validCount}, NaN={nanCount}, total={validCount + nanCount}");
//                 }

//                 totalFiles++;
//             }
//             catch (Exception e)
//             {
//                 Debug.LogError($"Failed processing '{fileName}': {e.Message}");
//             }
//         }

// #if UNITY_EDITOR
//         AssetDatabase.Refresh();
// #endif

//         Debug.Log($"Scene batch export complete -> Assets/{resultsFolderName} | files={totalFiles}, frames={totalFrames}");
//     }

//     private float? ComputeAverageConstraintAngleScene(string activeObj, string handName)
//     {
//         if (string.IsNullOrEmpty(activeObj))
//             return null;

//         if (_constraints == null ||
//             !_constraints.TryGetValue(activeObj, out var list) ||
//             list == null ||
//             list.Count == 0)
//             return null;

//         var ctx = new EvalContext
//         {
//             map = _nameToTransform,
//             activeObj = activeObj,
//             selectedHandName = handName
//         };

//         if (string.IsNullOrEmpty(ctx.selectedHandName))
//             return null;

//         if (!_nameToTransform.ContainsKey(ctx.selectedHandName))
//             return null;

//         var angles = new List<float>(list.Count);

//         foreach (var pair in list)
//         {
//             var s = pair.Source(ctx);
//             var t = pair.Target(ctx);
//             if (!s.HasValue || !t.HasValue) continue;

//             float angle = Vector3.Angle(s.Value, t.Value);
//             if (!float.IsNaN(angle) && !float.IsInfinity(angle))
//                 angles.Add(angle);
//         }

//         if (angles.Count == 0)
//             return null;

//         float sum = 0f;
//         for (int i = 0; i < angles.Count; i++)
//             sum += angles[i];

//         return sum / angles.Count;
//     }

//     private void ApplyFrame(string line, string activeObjectOfFile)
//     {
//         if (line.StartsWith("TRANSFORM|", StringComparison.OrdinalIgnoreCase))
//             line = line.Substring("TRANSFORM|".Length);

//         var segments = line.Split(';');
//         bool inBodyPartSection = false;

//         for (int i = 0; i < segments.Length; i++)
//         {
//             var seg = segments[i].Trim();
//             if (string.IsNullOrEmpty(seg)) continue;

//             var parts = seg.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
//             if (parts.Length != 8)
//             {
//                 Debug.LogWarning($"Bad segment (need 8 tokens): '{seg}'");
//                 continue;
//             }

//             string name = parts[0];
//             if (string.Equals(name, "left_hand", StringComparison.Ordinal))
//                 inBodyPartSection = true;

//             if (!_nameToTransform.TryGetValue(name, out var tr))
//             {
//                 if (logMissingObjects)
//                     Debug.LogWarning($"Object '{name}' not found in scene.");
//                 continue;
//             }

//             if (!TryF(parts[1], out float px) ||
//                 !TryF(parts[2], out float py) ||
//                 !TryF(parts[3], out float pz) ||
//                 !TryF(parts[4], out float rx) ||
//                 !TryF(parts[5], out float ry) ||
//                 !TryF(parts[6], out float rz) ||
//                 !TryF(parts[7], out float rw))
//             {
//                 Debug.LogWarning($"Parse error for '{name}' in segment: '{seg}'");
//                 continue;
//             }

//             bool isBodyPart = inBodyPartSection;
//             bool isActiveObjectForFile =
//                 !string.IsNullOrEmpty(activeObjectOfFile) &&
//                 string.Equals(name, activeObjectOfFile, StringComparison.Ordinal);

//             if (isBodyPart || isActiveObjectForFile)
//             {
//                 tr.position = new Vector3(px, py, pz);
//                 tr.rotation = new Quaternion(rx, ry, rz, rw);
//             }
//         }
//     }

//     private void BuildSceneObjectCache()
//     {
//         _nameToTransform.Clear();

//         var scene = SceneManager.GetActiveScene();
//         foreach (var root in scene.GetRootGameObjects())
//         {
//             foreach (var t in root.GetComponentsInChildren<Transform>(true))
//             {
//                 _nameToTransform[t.gameObject.name] = t;
//             }
//         }

//         Debug.Log($"Cached {_nameToTransform.Count} scene objects by name.");
//     }

//     private static string ExtractActiveObjectFromFilename(string filename)
//     {
//         var stem = Path.GetFileNameWithoutExtension(filename);
//         if (string.IsNullOrEmpty(stem)) return null;

//         var parts = stem.Split('_');
//         return parts.Length >= 1 ? parts[0] : null;
//     }

//     private static string ExtractHandNameFromFilename(string filename)
//     {
//         var stem = Path.GetFileNameWithoutExtension(filename);
//         if (string.IsNullOrEmpty(stem)) return null;

//         var parts = stem.Split('_');
//         if (parts.Length < 2) return null;

//         var token = parts[1].Trim().ToLowerInvariant();
//         if (token.StartsWith("left")) return "left_hand";
//         if (token.StartsWith("right")) return "right_hand";
//         return null;
//     }

//     private static bool TryF(string s, out float v)
//     {
//         return float.TryParse(
//             s,
//             NumberStyles.Float | NumberStyles.AllowThousands,
//             Inv,
//             out v
//         );
//     }

//     private static Vector3? NormalizeOrNull(Vector3 v)
//     {
//         if (v.sqrMagnitude < 1e-10f) return null;
//         return v.normalized;
//     }

//     private static Vector3? Axis(EvalContext ctx, string name, AxisKind kind)
//     {
//         if (!ctx.map.TryGetValue(name, out var tr)) return null;

//         Vector3 v = kind switch
//         {
//             AxisKind.Forward => tr.forward,
//             AxisKind.Up => tr.up,
//             AxisKind.Right => tr.right,
//             _ => tr.forward
//         };

//         return NormalizeOrNull(v);
//     }

//     private static Vector3? DiffToSelectedHand(EvalContext ctx, string objName)
//     {
//         if (!ctx.map.TryGetValue(objName, out var obj)) return null;
//         if (string.IsNullOrEmpty(ctx.selectedHandName)) return null;
//         if (!ctx.map.TryGetValue(ctx.selectedHandName, out var hand)) return null;

//         var v = hand.position - obj.position;
//         return NormalizeOrNull(v);
//     }

//     // private readonly Func<string, Func<EvalContext, Vector3?>> PosDiff =
//     //     sourceName => ctx =>
//     //     {
//     //         var sourceGO = GameObject.Find(sourceName);
//     //         if (sourceGO == null) return null;

//     //         var source = sourceGO.transform;
//     //         var marker = source.Find("marker");
//     //         if (marker == null) return null;

//     //         var v = marker.position - source.position;
//     //         return v.sqrMagnitude > 1e-12f ? v.normalized : null;
//     //     };
//     private readonly Func<string, string, Func<EvalContext, Vector3?>> PosDiff =
//     (sourceName, markerName) => ctx =>
//     {
//         var sourceGO = GameObject.Find(sourceName);
//         if (sourceGO == null) return (Vector3?)null;

//         var source = sourceGO.transform;
//         var marker = source.Find(markerName);
//         if (marker == null) return (Vector3?)null;

//         var v = marker.position - source.position;
//         return v.sqrMagnitude > 1e-12f ? (Vector3?)v.normalized : null;
//     };

//     private void InitConstraints()
//     {
//         Func<string, Func<EvalContext, Vector3?>> Fwd = name => ctx => Axis(ctx, name, AxisKind.Forward);
//         Func<string, Func<EvalContext, Vector3?>> UpV = name => ctx => Axis(ctx, name, AxisKind.Up);
//         Func<string, Func<EvalContext, Vector3?>> RightV = name => ctx => Axis(ctx, name, AxisKind.Right);
//         Func<EvalContext, Vector3?> WorldUp = ctx => Vector3.up;
//         Func<string, Func<EvalContext, Vector3?>> ObjToSelectedHand = name => ctx => DiffToSelectedHand(ctx, name);

//         Func<Func<EvalContext, Vector3?>, Func<EvalContext, Vector3?>> Neg = f => ctx =>
//         {
//             var v = f(ctx);
//             return v.HasValue ? (Vector3?)(-v.Value) : null;
//         };

//         _constraints = new Dictionary<string, List<VecPair>>(StringComparer.OrdinalIgnoreCase)
//         {
//             ["sugar"] = new List<VecPair>
//             {
//                 new VecPair { Source = Fwd("sugar"), Target = WorldUp }
//             },
//             ["hammer"] = new List<VecPair>
//             {
//                 new VecPair { Source = Fwd("hammer"), Target = Neg(ObjToSelectedHand("hammer")) }
//             },
//             ["kitchenknife"] = new List<VecPair>
//             {
//                 new VecPair { Source = PosDiff("kitchenknife", "handle"), Target = ObjToSelectedHand("kitchenknife") },
//                 new VecPair { Source = PosDiff("kitchenknife", "blade"), Target = Neg(ObjToSelectedHand("kitchenknife")) },
//             },
//             ["mug"] = new List<VecPair>
//             {
//                 new VecPair { Source = UpV("mug"), Target = WorldUp },
//                 new VecPair { Source = RightV("mug"), Target = ObjToSelectedHand("mug") }
//             },
//             ["bowl"] = new List<VecPair>
//             {
//                 new VecPair { Source = Fwd("bowl"), Target = WorldUp }
//             },
//             ["banana"] = new List<VecPair>
//             {
//                 new VecPair { Source = RightV("banana"), Target = WorldUp }
//             },
//             ["mustard"] = new List<VecPair>
//             {
//                 new VecPair { Source = Fwd("mustard"), Target = WorldUp }
//             },
//             ["plate"] = new List<VecPair>
//             {
//                 new VecPair { Source = Fwd("plate"), Target = WorldUp }
//             },
//             ["skillet"] = new List<VecPair>
//             {
//                 new VecPair { Source = Fwd("skillet"), Target = WorldUp },
//                 new VecPair { Source = UpV("skillet"), Target = ObjToSelectedHand("skillet") }
//             },
//             ["spoon"] = new List<VecPair>
//             {
//                 new VecPair { Source = UpV("spoon"), Target = Neg(ObjToSelectedHand("spoon")) }
//             },
//             ["fork"] = new List<VecPair>
//             {
//                 new VecPair { Source = UpV("fork"), Target = Neg(ObjToSelectedHand("fork")) }
//             },
//             ["bleach"] = new List<VecPair>
//             {
//                 new VecPair { Source = Fwd("bleach"), Target = WorldUp }
//             },
//             ["powerdrill"] = new List<VecPair>
//             {
//                 new VecPair { Source = Fwd("powerdrill"), Target = ObjToSelectedHand("powerdrill") }
//             },
//             ["screwdriver"] = new List<VecPair>
//             {
//                 new VecPair { Source = Fwd("screwdriver"), Target = Neg(ObjToSelectedHand("screwdriver")) }
//             },
//             ["spatula"] = new List<VecPair>
//             {
//                 new VecPair { Source = UpV("spatula"), Target = Neg(ObjToSelectedHand("spatula")) }
//             },
//             ["wood"] = new List<VecPair>
//             {
//                 new VecPair { Source = Fwd("wood"), Target = WorldUp }
//             }
//         };
//     }
// }