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

public class TrajectoryComfortBatchEvaluator : MonoBehaviour
{
    [Header("File Source")]
    public string folderRelativeToStreamingAssets = "trajectories";
    public string fileExtension = "*.txt";

    [Header("Filtering")]
    public string activeObjectFilter = "";

    [Header("Output")]
    public string oursComfortRelativePath = "trajectory/ours/comfort";
    public string nonAdaptiveComfortRelativePath = "trajectory/nonAdaptive/comfort";

    [Header("NonAdaptive Source")]
    public string nonAdaptiveHandoverRelativeToStreamingAssets = "handover_pos_nonAdaptive";

    [Header("Rig")]
    public string leftUpperArmName = "mixamorig4:LeftArm";
    public string leftForeArmName = "mixamorig4:LeftForeArm";
    public string leftHandName = "mixamorig4:LeftHand";

    public string rightUpperArmName = "mixamorig4:RightArm";
    public string rightForeArmName = "mixamorig4:RightForeArm";
    public string rightHandName = "mixamorig4:RightHand";

    [Header("Targeting")]
    public bool useObjectPositionForAfter = true;
    public Vector3 afterTargetOffset = Vector3.zero;

    [Header("Logging")]
    public bool disableAnimatorsDuringEvaluation = true;
    public bool logMissingObjects = false;
    public bool logPerFileSummary = true;
    public bool logIKDiagnostics = true;

    [Header("Debug")]
    public string onlyProcessThisExactFile = "";

    private static readonly IFormatProvider Inv = CultureInfo.InvariantCulture;

    private Dictionary<string, Transform> _nameToTransform =
        new Dictionary<string, Transform>(StringComparer.Ordinal);

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

    private struct JointSnapshot
    {
        public Quaternion upperArmLocal;
        public Quaternion foreArmLocal;
        public Quaternion handLocal;
    }

    private struct ComfortResult
    {
        public float shoulder;
        public float arm;
        public float foreArm;
        public float hand;

        public float Average => (shoulder + arm + foreArm + hand) / 4f;

        public string ToLine()
        {
            return
                $"Shoulder={shoulder.ToString("G9", Inv)};" +
                $"Arm={arm.ToString("G9", Inv)};" +
                $"ForeArm={foreArm.ToString("G9", Inv)};" +
                $"Hand={hand.ToString("G9", Inv)};" +
                $"Average={Average.ToString("G9", Inv)}";
        }
    }

    private struct AnimatorState
    {
        public Animator animator;
        public bool wasEnabled;
    }

    private void Awake()
    {
        BuildSceneObjectCache();
        SceneManager.sceneLoaded += (_, __) => BuildSceneObjectCache();
    }

    [ContextMenu("Export Comfort (HandTarget vs ObjectTarget)")]
    public void ExportComfortBatch()
    {
        BuildSceneObjectCache();

        string baseFolder = Path.Combine(Application.streamingAssetsPath, folderRelativeToStreamingAssets);
        string nonAdaptiveHandoverFolder = Path.Combine(Application.streamingAssetsPath, nonAdaptiveHandoverRelativeToStreamingAssets);

        string outDirOurs = Path.Combine(Application.dataPath, oursComfortRelativePath);
        string outDirNonAdaptive = Path.Combine(Application.dataPath, nonAdaptiveComfortRelativePath);

        Debug.Log($"[ComfortEval] Input folder: {baseFolder}");
        Debug.Log($"[ComfortEval] NonAdaptive handover folder: {nonAdaptiveHandoverFolder}");
        Debug.Log($"[ComfortEval] Output folder (ours): {outDirOurs}");
        Debug.Log($"[ComfortEval] Output folder (nonAdaptive): {outDirNonAdaptive}");

        if (!Directory.Exists(baseFolder))
        {
            Debug.LogError($"[ComfortEval] Trajectory folder not found: {baseFolder}");
            return;
        }

        if (!Directory.Exists(nonAdaptiveHandoverFolder))
        {
            Debug.LogError($"[ComfortEval] NonAdaptive handover folder not found: {nonAdaptiveHandoverFolder}");
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
                bool prefixOk =
                    string.IsNullOrEmpty(prefix) ||
                    name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

                bool exactOk =
                    string.IsNullOrWhiteSpace(onlyProcessThisExactFile) ||
                    name.Equals(onlyProcessThisExactFile, StringComparison.OrdinalIgnoreCase);

                return prefixOk && exactOk;
            })
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Debug.Log($"[ComfortEval] Matching files: {files.Length}");

        if (files.Length == 0)
        {
            Debug.LogWarning($"[ComfortEval] No trajectory files found in {baseFolder} matching current filter.");
            return;
        }

        List<AnimatorState> animatorStates = null;

        var oursAverages = new List<float>();
        var nonAdaptiveAverages = new List<float>();

        try
        {
            if (disableAnimatorsDuringEvaluation)
                animatorStates = SetAnimatorsEnabled(false);

            int totalFiles = 0;
            int skippedMissingNonAdaptive = 0;

            foreach (var path in files)
            {
                string fileName = Path.GetFileName(path);
                string activeObj = ExtractActiveObjectFromFilename(fileName);
                string handToken = ExtractHandNameFromFilename(fileName);

                string outPathOurs = Path.Combine(outDirOurs, fileName);
                string outPathNonAdaptive = Path.Combine(outDirNonAdaptive, fileName);

                try
                {
                    string nonAdaptivePath = Path.Combine(nonAdaptiveHandoverFolder, fileName);
                    if (!File.Exists(nonAdaptivePath))
                    {
                        skippedMissingNonAdaptive++;
                        Debug.LogWarning($"[ComfortEval] Skipping '{fileName}' because matching nonAdaptive handover file was not found: {nonAdaptivePath}");
                        continue;
                    }

                    string[] rawLines = File.ReadAllLines(path);
                    string lastFrame = GetLastNonEmptyFrame(rawLines);

                    if (string.IsNullOrEmpty(lastFrame))
                    {
                        Debug.LogWarning($"[ComfortEval] No valid frame in '{fileName}'. Skipping.");
                        continue;
                    }

                    if (!TryExtractPoseFromFrame(lastFrame, activeObj, out PoseData activeObjPose))
                    {
                        Debug.LogWarning($"[ComfortEval] Active object '{activeObj}' not found in last frame of '{fileName}'. Skipping.");
                        continue;
                    }

                    string txtHandName = handToken;
                    if (!TryExtractPoseFromFrame(lastFrame, txtHandName, out PoseData txtHandPose))
                    {
                        Debug.LogWarning($"[ComfortEval] Hand token '{txtHandName}' not found in last frame of '{fileName}'. Skipping.");
                        continue;
                    }

                    if (!TryReadNonAdaptiveHandoverPosition(nonAdaptivePath, out Vector3 nonAdaptiveAfterTarget))
                    {
                        Debug.LogWarning($"[ComfortEval] Could not parse nonAdaptive handover position from '{nonAdaptivePath}'. Skipping.");
                        continue;
                    }

                    var oursResult = EvaluateComfort(
                        sourceFileName: fileName,
                        handName: handToken,
                        beforeTargetWorld: txtHandPose.position,
                        afterTargetWorld: activeObjPose.position + afterTargetOffset);

                    var nonAdaptiveResult = EvaluateComfort(
                        sourceFileName: fileName + " [nonAdaptive]",
                        handName: handToken,
                        beforeTargetWorld: txtHandPose.position,
                        afterTargetWorld: nonAdaptiveAfterTarget + afterTargetOffset);

                    string oursText = oursResult.HasValue ? oursResult.Value.ToLine() : "NaN";
                    string nonAdaptiveText = nonAdaptiveResult.HasValue ? nonAdaptiveResult.Value.ToLine() : "NaN";

                    File.WriteAllText(outPathOurs, oursText);
                    File.WriteAllText(outPathNonAdaptive, nonAdaptiveText);

                    if (oursResult.HasValue)
                        oursAverages.Add(oursResult.Value.Average);

                    if (nonAdaptiveResult.HasValue)
                        nonAdaptiveAverages.Add(nonAdaptiveResult.Value.Average);

                    if (logPerFileSummary)
                    {
                        Debug.Log($"[ComfortEval] {fileName}");
                        Debug.Log($"[ComfortEval] ours -> handTarget={txtHandPose.position} objectTarget={activeObjPose.position + afterTargetOffset}");
                        Debug.Log($"[ComfortEval] nonAdaptive -> handTarget={txtHandPose.position} handoverTarget={nonAdaptiveAfterTarget + afterTargetOffset}");
                        Debug.Log($"[ComfortEval] ours result -> {oursText}");
                        Debug.Log($"[ComfortEval] nonAdaptive result -> {nonAdaptiveText}");
                    }

                    totalFiles++;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ComfortEval] Failed processing '{fileName}': {e}");
                }
            }

            WriteOverallSummary(Path.Combine(outDirOurs, "_overall_stats.txt"), "ours", oursAverages);
            WriteOverallSummary(Path.Combine(outDirNonAdaptive, "_overall_stats.txt"), "nonAdaptive", nonAdaptiveAverages);

#if UNITY_EDITOR
            AssetDatabase.Refresh();
#endif

            Debug.Log($"[ComfortEval] Done. Files={totalFiles}, skippedMissingNonAdaptive={skippedMissingNonAdaptive}");
            Debug.Log($"[ComfortEval] Ours summary: count={oursAverages.Count}, mean={ComputeMean(oursAverages).ToString("G9", Inv)}, sd={ComputePopulationSd(oursAverages).ToString("G9", Inv)}");
            Debug.Log($"[ComfortEval] NonAdaptive summary: count={nonAdaptiveAverages.Count}, mean={ComputeMean(nonAdaptiveAverages).ToString("G9", Inv)}, sd={ComputePopulationSd(nonAdaptiveAverages).ToString("G9", Inv)}");
        }
        finally
        {
            if (disableAnimatorsDuringEvaluation && animatorStates != null)
                RestoreAnimatorStates(animatorStates);
        }
    }

    private ComfortResult? EvaluateComfort(
        string sourceFileName,
        string handName,
        Vector3 beforeTargetWorld,
        Vector3 afterTargetWorld)
    {
        if (!TryGetRelevantChain(handName, out var upperArm, out var foreArm, out var hand))
        {
            Debug.LogWarning($"[ComfortEval] Relevant chain not found for hand '{handName}'.");
            return null;
        }

        Transform shoulder = upperArm.parent;
        if (shoulder == null)
        {
            Debug.LogWarning($"[ComfortEval] Upper arm parent missing for hand '{handName}'.");
            return null;
        }

        PoseData shoulderPose = new PoseData(shoulder.position, shoulder.rotation);
        PoseData upperArmPose = new PoseData(upperArm.position, upperArm.rotation);
        PoseData foreArmPose = new PoseData(foreArm.position, foreArm.rotation);
        PoseData handPose = new PoseData(hand.position, hand.rotation);

        bool beforeSolved = SolveTwoBoneIK(upperArm, foreArm, hand, beforeTargetWorld);
        JointSnapshot before = CaptureJointSnapshot(upperArm, foreArm, hand);

        RestoreWorldPose(shoulder, shoulderPose);
        RestoreWorldPose(upperArm, upperArmPose);
        RestoreWorldPose(foreArm, foreArmPose);
        RestoreWorldPose(hand, handPose);

        bool afterSolved = SolveTwoBoneIK(upperArm, foreArm, hand, afterTargetWorld);
        JointSnapshot after = CaptureJointSnapshot(upperArm, foreArm, hand);

        if (logIKDiagnostics)
        {
            Debug.Log(
                $"[ComfortEval][IK] file={sourceFileName} handName={handName} " +
                $"beforeTarget={beforeTargetWorld} afterTarget={afterTargetWorld} " +
                $"beforeSolved={beforeSolved} afterSolved={afterSolved}");
        }

        RestoreWorldPose(shoulder, shoulderPose);
        RestoreWorldPose(upperArm, upperArmPose);
        RestoreWorldPose(foreArm, foreArmPose);
        RestoreWorldPose(hand, handPose);

        return new ComfortResult
        {
            shoulder = 0f,
            arm = Quaternion.Angle(before.upperArmLocal, after.upperArmLocal),
            foreArm = Quaternion.Angle(before.foreArmLocal, after.foreArmLocal),
            hand = Quaternion.Angle(before.handLocal, after.handLocal)
        };
    }

    private bool SolveTwoBoneIK(Transform upperArm, Transform foreArm, Transform hand, Vector3 targetWorld)
    {
        if (upperArm == null || foreArm == null || hand == null)
            return false;

        Vector3 shoulderPos = upperArm.position;
        Vector3 elbowPos = foreArm.position;
        Vector3 wristPos = hand.position;

        float upperLen = Vector3.Distance(shoulderPos, elbowPos);
        float foreLen = Vector3.Distance(elbowPos, wristPos);

        if (upperLen < 1e-6f || foreLen < 1e-6f)
            return false;

        Vector3 toTarget = targetWorld - shoulderPos;
        float distToTarget = toTarget.magnitude;

        if (distToTarget < 1e-6f)
            return false;

        Vector3 targetDir = toTarget / distToTarget;

        float clampedDist = Mathf.Clamp(distToTarget, Mathf.Abs(upperLen - foreLen) + 1e-5f, upperLen + foreLen - 1e-5f);

        Vector3 currentShoulderToWrist = wristPos - shoulderPos;
        if (currentShoulderToWrist.sqrMagnitude < 1e-10f)
            return false;

        Vector3 bendNormal = Vector3.Cross(elbowPos - shoulderPos, wristPos - elbowPos);
        if (bendNormal.sqrMagnitude < 1e-10f)
        {
            bendNormal = Vector3.Cross(targetDir, upperArm.up);
            if (bendNormal.sqrMagnitude < 1e-10f)
                bendNormal = Vector3.Cross(targetDir, upperArm.right);
        }
        bendNormal.Normalize();

        float cosShoulder =
            (upperLen * upperLen + clampedDist * clampedDist - foreLen * foreLen) /
            (2f * upperLen * clampedDist);
        cosShoulder = Mathf.Clamp(cosShoulder, -1f, 1f);
        float shoulderAngle = Mathf.Acos(cosShoulder);

        Vector3 axisInPlane = Vector3.Cross(bendNormal, targetDir).normalized;
        Vector3 desiredUpperDir = (targetDir * Mathf.Cos(shoulderAngle) + axisInPlane * Mathf.Sin(shoulderAngle)).normalized;
        Vector3 desiredElbowPos = shoulderPos + desiredUpperDir * upperLen;
        Vector3 desiredForeDir = (targetWorld - desiredElbowPos).normalized;

        Quaternion deltaUpper = Quaternion.FromToRotation(elbowPos - shoulderPos, desiredElbowPos - shoulderPos);
        upperArm.rotation = deltaUpper * upperArm.rotation;

        elbowPos = foreArm.position;
        wristPos = hand.position;

        Quaternion deltaFore = Quaternion.FromToRotation(wristPos - elbowPos, desiredForeDir * foreLen);
        foreArm.rotation = deltaFore * foreArm.rotation;

        return true;
    }

    private JointSnapshot CaptureJointSnapshot(
        Transform upperArm,
        Transform foreArm,
        Transform hand)
    {
        return new JointSnapshot
        {
            upperArmLocal = upperArm.localRotation,
            foreArmLocal = foreArm.localRotation,
            handLocal = hand.localRotation
        };
    }

    private void RestoreWorldPose(Transform tr, PoseData pose)
    {
        if (tr == null) return;
        tr.position = pose.position;
        tr.rotation = pose.rotation;
    }

    private bool TryGetRelevantChain(
        string handName,
        out Transform upperArm,
        out Transform foreArm,
        out Transform hand)
    {
        upperArm = null;
        foreArm = null;
        hand = null;

        bool isLeft = string.Equals(handName, "left_hand", StringComparison.OrdinalIgnoreCase);

        string upperArmName = isLeft ? leftUpperArmName : rightUpperArmName;
        string foreArmName = isLeft ? leftForeArmName : rightForeArmName;
        string handBoneName = isLeft ? leftHandName : rightHandName;

        bool ok =
            _nameToTransform.TryGetValue(upperArmName, out upperArm) &&
            _nameToTransform.TryGetValue(foreArmName, out foreArm) &&
            _nameToTransform.TryGetValue(handBoneName, out hand);

        if (!ok && logMissingObjects)
        {
            if (upperArm == null) Debug.LogWarning($"[ComfortEval] Missing bone: {upperArmName}");
            if (foreArm == null) Debug.LogWarning($"[ComfortEval] Missing bone: {foreArmName}");
            if (hand == null) Debug.LogWarning($"[ComfortEval] Missing bone: {handBoneName}");
        }

        return ok;
    }

    private string GetLastNonEmptyFrame(string[] rawLines)
    {
        if (rawLines == null || rawLines.Length == 0)
            return null;

        for (int i = rawLines.Length - 1; i >= 0; i--)
        {
            string line = rawLines[i]?.Trim();
            if (!string.IsNullOrEmpty(line))
                return line;
        }

        return null;
    }

    private bool TryExtractPoseFromFrame(string line, string targetName, out PoseData pose)
    {
        pose = default;

        if (string.IsNullOrWhiteSpace(line) || string.IsNullOrWhiteSpace(targetName))
            return false;

        if (line.StartsWith("TRANSFORM|", StringComparison.OrdinalIgnoreCase))
            line = line.Substring("TRANSFORM|".Length);

        var segments = line.Split(';');

        for (int i = 0; i < segments.Length; i++)
        {
            var seg = segments[i].Trim();
            if (string.IsNullOrEmpty(seg))
                continue;

            var parts = seg.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 8)
                continue;

            string name = parts[0];
            if (!string.Equals(name, targetName, StringComparison.Ordinal))
                continue;

            if (!TryF(parts[1], out float px) ||
                !TryF(parts[2], out float py) ||
                !TryF(parts[3], out float pz) ||
                !TryF(parts[4], out float rx) ||
                !TryF(parts[5], out float ry) ||
                !TryF(parts[6], out float rz) ||
                !TryF(parts[7], out float rw))
            {
                return false;
            }

            pose = new PoseData(
                new Vector3(px, py, pz),
                new Quaternion(rx, ry, rz, rw));

            return true;
        }

        return false;
    }

    private bool TryReadNonAdaptiveHandoverPosition(string path, out Vector3 position)
    {
        position = default;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        string line = File.ReadAllText(path).Trim();
        if (string.IsNullOrWhiteSpace(line))
            return false;

        line = line.Trim();
        if (line.StartsWith("["))
            line = line.Substring(1);
        if (line.EndsWith("]"))
            line = line.Substring(0, line.Length - 1);

        string[] parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
            return false;

        if (!TryF(parts[0], out float x) ||
            !TryF(parts[1], out float y) ||
            !TryF(parts[2], out float z))
        {
            return false;
        }

        position = new Vector3(x, y, z);
        return true;
    }

    private void WriteOverallSummary(string outputPath, string label, List<float> values)
    {
        float mean = ComputeMean(values);
        float sd = ComputePopulationSd(values);

        string text =
            $"Label={label}\n" +
            $"Count={values.Count}\n" +
            $"MeanAverage={mean.ToString("G9", Inv)}\n" +
            $"SDAverage={sd.ToString("G9", Inv)}\n";

        File.WriteAllText(outputPath, text);
    }

    private float ComputeMean(List<float> values)
    {
        if (values == null || values.Count == 0)
            return float.NaN;

        float sum = 0f;
        for (int i = 0; i < values.Count; i++)
            sum += values[i];

        return sum / values.Count;
    }

    private float ComputePopulationSd(List<float> values)
    {
        if (values == null || values.Count == 0)
            return float.NaN;

        float mean = ComputeMean(values);
        float sumSq = 0f;

        for (int i = 0; i < values.Count; i++)
        {
            float d = values[i] - mean;
            sumSq += d * d;
        }

        return Mathf.Sqrt(sumSq / values.Count);
    }

    private void BuildSceneObjectCache()
    {
        _nameToTransform.Clear();

        var scene = SceneManager.GetActiveScene();
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                _nameToTransform[t.gameObject.name] = t;
        }

        Debug.Log($"[ComfortEval] Cached {_nameToTransform.Count} scene objects by name.");
    }

    private List<AnimatorState> SetAnimatorsEnabled(bool enabled)
    {
        var animators = FindObjectsOfType<Animator>(true);
        var states = new List<AnimatorState>(animators.Length);

        for (int i = 0; i < animators.Length; i++)
        {
            var a = animators[i];
            states.Add(new AnimatorState
            {
                animator = a,
                wasEnabled = a.enabled
            });
            a.enabled = enabled;
        }

        Debug.Log($"[ComfortEval] Animator override: set {animators.Length} animator(s) enabled={enabled}");
        return states;
    }

    private void RestoreAnimatorStates(List<AnimatorState> states)
    {
        for (int i = 0; i < states.Count; i++)
        {
            if (states[i].animator != null)
                states[i].animator.enabled = states[i].wasEnabled;
        }

        Debug.Log($"[ComfortEval] Restored {states.Count} animator state(s).");
    }

    private static string ExtractActiveObjectFromFilename(string filename)
    {
        var stem = Path.GetFileNameWithoutExtension(filename);
        if (string.IsNullOrEmpty(stem))
            return null;

        var parts = stem.Split('_');
        return parts.Length >= 1 ? parts[0] : null;
    }

    private static string ExtractHandNameFromFilename(string filename)
    {
        var stem = Path.GetFileNameWithoutExtension(filename);
        if (string.IsNullOrEmpty(stem))
            return null;

        var parts = stem.Split('_');
        if (parts.Length < 2)
            return null;

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

// public class TrajectoryComfortBatchEvaluator : MonoBehaviour
// {
//     [Header("File Source")]
//     public string folderRelativeToStreamingAssets = "trajectories";
//     public string fileExtension = "*.txt";

//     [Header("Filtering")]
//     public string activeObjectFilter = "";

//     [Header("Output")]
//     public string oursComfortRelativePath = "trajectory/ours/comfort";
//     public string nonAdaptiveComfortRelativePath = "trajectory/nonAdaptive/comfort";

//     [Header("Rig")]
//     public string leftUpperArmName = "mixamorig4:LeftArm";
//     public string leftForeArmName = "mixamorig4:LeftForeArm";
//     public string leftHandName = "mixamorig4:LeftHand";

//     public string rightUpperArmName = "mixamorig4:RightArm";
//     public string rightForeArmName = "mixamorig4:RightForeArm";
//     public string rightHandName = "mixamorig4:RightHand";

//     [Header("Targeting")]
//     public bool useObjectPositionForAfter = true;
//     public Vector3 afterTargetOffset = Vector3.zero;

//     [Header("Logging")]
//     public bool disableAnimatorsDuringEvaluation = true;
//     public bool logMissingObjects = false;
//     public bool logPerFileSummary = true;
//     public bool logIKDiagnostics = true;

//     [Header("Debug")]
//     public string onlyProcessThisExactFile = "";

//     private static readonly IFormatProvider Inv = CultureInfo.InvariantCulture;

//     private Dictionary<string, Transform> _nameToTransform =
//         new Dictionary<string, Transform>(StringComparer.Ordinal);

//     private struct PoseData
//     {
//         public Vector3 position;
//         public Quaternion rotation;

//         public PoseData(Vector3 p, Quaternion r)
//         {
//             position = p;
//             rotation = r;
//         }
//     }

//     private struct JointSnapshot
//     {
//         public Quaternion upperArmLocal;
//         public Quaternion foreArmLocal;
//         public Quaternion handLocal;
//     }

//     private struct ComfortResult
//     {
//         public float shoulder;
//         public float arm;
//         public float foreArm;
//         public float hand;

//         public float Average => (shoulder + arm + foreArm + hand) / 4f;

//         public string ToLine()
//         {
//             return
//                 $"Shoulder={shoulder.ToString("G9", Inv)};" +
//                 $"Arm={arm.ToString("G9", Inv)};" +
//                 $"ForeArm={foreArm.ToString("G9", Inv)};" +
//                 $"Hand={hand.ToString("G9", Inv)};" +
//                 $"Average={Average.ToString("G9", Inv)}";
//         }
//     }

//     private struct AnimatorState
//     {
//         public Animator animator;
//         public bool wasEnabled;
//     }

//     private void Awake()
//     {
//         BuildSceneObjectCache();
//         SceneManager.sceneLoaded += (_, __) => BuildSceneObjectCache();
//     }

//     [ContextMenu("Export Comfort (HandTarget vs ObjectTarget)")]
//     public void ExportComfortBatch()
//     {
//         BuildSceneObjectCache();

//         string baseFolder = Path.Combine(Application.streamingAssetsPath, folderRelativeToStreamingAssets);
//         string outDirOurs = Path.Combine(Application.dataPath, oursComfortRelativePath);
//         string outDirNonAdaptive = Path.Combine(Application.dataPath, nonAdaptiveComfortRelativePath);

//         Debug.Log($"[ComfortEval] Input folder: {baseFolder}");
//         Debug.Log($"[ComfortEval] Output folder (ours): {outDirOurs}");
//         Debug.Log($"[ComfortEval] Output folder (nonAdaptive): {outDirNonAdaptive}");

//         if (!Directory.Exists(baseFolder))
//         {
//             Debug.LogError($"[ComfortEval] Trajectory folder not found: {baseFolder}");
//             return;
//         }

//         Directory.CreateDirectory(outDirOurs);
//         Directory.CreateDirectory(outDirNonAdaptive);

//         var allFiles = Directory.GetFiles(baseFolder, fileExtension, SearchOption.TopDirectoryOnly);

//         string prefix = string.IsNullOrWhiteSpace(activeObjectFilter) ? "" : (activeObjectFilter + "_");

//         var files = allFiles
//             .Where(p =>
//             {
//                 var name = Path.GetFileName(p);
//                 bool prefixOk =
//                     string.IsNullOrEmpty(prefix) ||
//                     name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

//                 bool exactOk =
//                     string.IsNullOrWhiteSpace(onlyProcessThisExactFile) ||
//                     name.Equals(onlyProcessThisExactFile, StringComparison.OrdinalIgnoreCase);

//                 return prefixOk && exactOk;
//             })
//             .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
//             .ToArray();

//         Debug.Log($"[ComfortEval] Matching files: {files.Length}");

//         if (files.Length == 0)
//         {
//             Debug.LogWarning($"[ComfortEval] No trajectory files found in {baseFolder} matching current filter.");
//             return;
//         }

//         List<AnimatorState> animatorStates = null;

//         try
//         {
//             if (disableAnimatorsDuringEvaluation)
//                 animatorStates = SetAnimatorsEnabled(false);

//             int totalFiles = 0;

//             foreach (var path in files)
//             {
//                 string fileName = Path.GetFileName(path);
//                 string activeObj = ExtractActiveObjectFromFilename(fileName);
//                 string handToken = ExtractHandNameFromFilename(fileName);

//                 string outPathOurs = Path.Combine(outDirOurs, fileName);
//                 string outPathNonAdaptive = Path.Combine(outDirNonAdaptive, fileName);

//                 try
//                 {
//                     string[] rawLines = File.ReadAllLines(path);
//                     string lastFrame = GetLastNonEmptyFrame(rawLines);

//                     if (string.IsNullOrEmpty(lastFrame))
//                     {
//                         File.WriteAllText(outPathOurs, "NaN");
//                         File.WriteAllText(outPathNonAdaptive, "NaN");
//                         Debug.LogWarning($"[ComfortEval] No valid frame in '{fileName}'.");
//                         continue;
//                     }

//                     if (!TryExtractPoseFromFrame(lastFrame, activeObj, out PoseData activeObjPose))
//                     {
//                         File.WriteAllText(outPathOurs, "NaN");
//                         File.WriteAllText(outPathNonAdaptive, "NaN");
//                         Debug.LogWarning($"[ComfortEval] Active object '{activeObj}' not found in last frame of '{fileName}'.");
//                         continue;
//                     }

//                     string txtHandName = handToken;
//                     if (!TryExtractPoseFromFrame(lastFrame, txtHandName, out PoseData txtHandPose))
//                     {
//                         File.WriteAllText(outPathOurs, "NaN");
//                         File.WriteAllText(outPathNonAdaptive, "NaN");
//                         Debug.LogWarning($"[ComfortEval] Hand token '{txtHandName}' not found in last frame of '{fileName}'.");
//                         continue;
//                     }

//                     var result = EvaluateComfort(
//                         sourceFileName: fileName,
//                         handName: handToken,
//                         beforeTargetWorld: txtHandPose.position,
//                         afterTargetWorld: activeObjPose.position + afterTargetOffset);

//                     string text = result.HasValue ? result.Value.ToLine() : "NaN";

//                     // Same result written to both outputs so your downstream folder structure still works.
//                     File.WriteAllText(outPathOurs, text);
//                     File.WriteAllText(outPathNonAdaptive, text);

//                     if (logPerFileSummary)
//                     {
//                         Debug.Log($"[ComfortEval] {fileName}");
//                         Debug.Log($"[ComfortEval] handTarget={txtHandPose.position} objectTarget={activeObjPose.position + afterTargetOffset}");
//                         Debug.Log($"[ComfortEval] result -> {text}");
//                     }

//                     totalFiles++;
//                 }
//                 catch (Exception e)
//                 {
//                     Debug.LogError($"[ComfortEval] Failed processing '{fileName}': {e}");
//                 }
//             }

// #if UNITY_EDITOR
//             AssetDatabase.Refresh();
// #endif

//             Debug.Log($"[ComfortEval] Done. Files={totalFiles}");
//         }
//         finally
//         {
//             if (disableAnimatorsDuringEvaluation && animatorStates != null)
//                 RestoreAnimatorStates(animatorStates);
//         }
//     }

//     private ComfortResult? EvaluateComfort(
//         string sourceFileName,
//         string handName,
//         Vector3 beforeTargetWorld,
//         Vector3 afterTargetWorld)
//     {
//         if (!TryGetRelevantChain(handName, out var upperArm, out var foreArm, out var hand))
//         {
//             Debug.LogWarning($"[ComfortEval] Relevant chain not found for hand '{handName}'.");
//             return null;
//         }

//         Transform shoulder = upperArm.parent;
//         if (shoulder == null)
//         {
//             Debug.LogWarning($"[ComfortEval] Upper arm parent missing for hand '{handName}'.");
//             return null;
//         }

//         PoseData shoulderPose = new PoseData(shoulder.position, shoulder.rotation);
//         PoseData upperArmPose = new PoseData(upperArm.position, upperArm.rotation);
//         PoseData foreArmPose = new PoseData(foreArm.position, foreArm.rotation);
//         PoseData handPose = new PoseData(hand.position, hand.rotation);

//         // Solve "before" pose: target = txt hand position
//         bool beforeSolved = SolveTwoBoneIK(upperArm, foreArm, hand, beforeTargetWorld);
//         JointSnapshot before = CaptureJointSnapshot(upperArm, foreArm, hand);

//         RestoreWorldPose(shoulder, shoulderPose);
//         RestoreWorldPose(upperArm, upperArmPose);
//         RestoreWorldPose(foreArm, foreArmPose);
//         RestoreWorldPose(hand, handPose);

//         // Solve "after" pose: target = active object position
//         bool afterSolved = SolveTwoBoneIK(upperArm, foreArm, hand, afterTargetWorld);
//         JointSnapshot after = CaptureJointSnapshot(upperArm, foreArm, hand);

//         if (logIKDiagnostics)
//         {
//             float beforeDist = Vector3.Distance(hand.position, beforeTargetWorld); // not meaningful after restore, so log separate below
//             Debug.Log(
//                 $"[ComfortEval][IK] file={sourceFileName} handName={handName} " +
//                 $"beforeTarget={beforeTargetWorld} afterTarget={afterTargetWorld} " +
//                 $"beforeSolved={beforeSolved} afterSolved={afterSolved}");
//         }

//         RestoreWorldPose(shoulder, shoulderPose);
//         RestoreWorldPose(upperArm, upperArmPose);
//         RestoreWorldPose(foreArm, foreArmPose);
//         RestoreWorldPose(hand, handPose);

//         return new ComfortResult
//         {
//             shoulder = 0f,
//             arm = Quaternion.Angle(before.upperArmLocal, after.upperArmLocal),
//             foreArm = Quaternion.Angle(before.foreArmLocal, after.foreArmLocal),
//             hand = Quaternion.Angle(before.handLocal, after.handLocal)
//         };
//     }

//     private bool SolveTwoBoneIK(Transform upperArm, Transform foreArm, Transform hand, Vector3 targetWorld)
//     {
//         if (upperArm == null || foreArm == null || hand == null)
//             return false;

//         Vector3 shoulderPos = upperArm.position;
//         Vector3 elbowPos = foreArm.position;
//         Vector3 wristPos = hand.position;

//         float upperLen = Vector3.Distance(shoulderPos, elbowPos);
//         float foreLen = Vector3.Distance(elbowPos, wristPos);

//         if (upperLen < 1e-6f || foreLen < 1e-6f)
//             return false;

//         Vector3 toTarget = targetWorld - shoulderPos;
//         float distToTarget = toTarget.magnitude;

//         if (distToTarget < 1e-6f)
//             return false;

//         Vector3 targetDir = toTarget / distToTarget;

//         float clampedDist = Mathf.Clamp(distToTarget, Mathf.Abs(upperLen - foreLen) + 1e-5f, upperLen + foreLen - 1e-5f);

//         Vector3 currentShoulderToWrist = wristPos - shoulderPos;
//         if (currentShoulderToWrist.sqrMagnitude < 1e-10f)
//             return false;

//         Vector3 bendNormal = Vector3.Cross(elbowPos - shoulderPos, wristPos - elbowPos);
//         if (bendNormal.sqrMagnitude < 1e-10f)
//         {
//             bendNormal = Vector3.Cross(targetDir, upperArm.up);
//             if (bendNormal.sqrMagnitude < 1e-10f)
//                 bendNormal = Vector3.Cross(targetDir, upperArm.right);
//         }
//         bendNormal.Normalize();

//         float cosShoulder =
//             (upperLen * upperLen + clampedDist * clampedDist - foreLen * foreLen) /
//             (2f * upperLen * clampedDist);
//         cosShoulder = Mathf.Clamp(cosShoulder, -1f, 1f);
//         float shoulderAngle = Mathf.Acos(cosShoulder);

//         Vector3 axisInPlane = Vector3.Cross(bendNormal, targetDir).normalized;
//         Vector3 desiredUpperDir = (targetDir * Mathf.Cos(shoulderAngle) + axisInPlane * Mathf.Sin(shoulderAngle)).normalized;
//         Vector3 desiredElbowPos = shoulderPos + desiredUpperDir * upperLen;
//         Vector3 desiredForeDir = (targetWorld - desiredElbowPos).normalized;

//         Quaternion deltaUpper = Quaternion.FromToRotation(elbowPos - shoulderPos, desiredElbowPos - shoulderPos);
//         upperArm.rotation = deltaUpper * upperArm.rotation;

//         elbowPos = foreArm.position;
//         wristPos = hand.position;

//         Quaternion deltaFore = Quaternion.FromToRotation(wristPos - elbowPos, desiredForeDir * foreLen);
//         foreArm.rotation = deltaFore * foreArm.rotation;

//         return true;
//     }

//     private JointSnapshot CaptureJointSnapshot(
//         Transform upperArm,
//         Transform foreArm,
//         Transform hand)
//     {
//         return new JointSnapshot
//         {
//             upperArmLocal = upperArm.localRotation,
//             foreArmLocal = foreArm.localRotation,
//             handLocal = hand.localRotation
//         };
//     }

//     private void RestoreWorldPose(Transform tr, PoseData pose)
//     {
//         if (tr == null) return;
//         tr.position = pose.position;
//         tr.rotation = pose.rotation;
//     }

//     private bool TryGetRelevantChain(
//         string handName,
//         out Transform upperArm,
//         out Transform foreArm,
//         out Transform hand)
//     {
//         upperArm = null;
//         foreArm = null;
//         hand = null;

//         bool isLeft = string.Equals(handName, "left_hand", StringComparison.OrdinalIgnoreCase);

//         string upperArmName = isLeft ? leftUpperArmName : rightUpperArmName;
//         string foreArmName = isLeft ? leftForeArmName : rightForeArmName;
//         string handBoneName = isLeft ? leftHandName : rightHandName;

//         bool ok =
//             _nameToTransform.TryGetValue(upperArmName, out upperArm) &&
//             _nameToTransform.TryGetValue(foreArmName, out foreArm) &&
//             _nameToTransform.TryGetValue(handBoneName, out hand);

//         if (!ok && logMissingObjects)
//         {
//             if (upperArm == null) Debug.LogWarning($"[ComfortEval] Missing bone: {upperArmName}");
//             if (foreArm == null) Debug.LogWarning($"[ComfortEval] Missing bone: {foreArmName}");
//             if (hand == null) Debug.LogWarning($"[ComfortEval] Missing bone: {handBoneName}");
//         }

//         return ok;
//     }

//     private string GetLastNonEmptyFrame(string[] rawLines)
//     {
//         if (rawLines == null || rawLines.Length == 0)
//             return null;

//         for (int i = rawLines.Length - 1; i >= 0; i--)
//         {
//             string line = rawLines[i]?.Trim();
//             if (!string.IsNullOrEmpty(line))
//                 return line;
//         }

//         return null;
//     }

//     private bool TryExtractPoseFromFrame(string line, string targetName, out PoseData pose)
//     {
//         pose = default;

//         if (string.IsNullOrWhiteSpace(line) || string.IsNullOrWhiteSpace(targetName))
//             return false;

//         if (line.StartsWith("TRANSFORM|", StringComparison.OrdinalIgnoreCase))
//             line = line.Substring("TRANSFORM|".Length);

//         var segments = line.Split(';');

//         for (int i = 0; i < segments.Length; i++)
//         {
//             var seg = segments[i].Trim();
//             if (string.IsNullOrEmpty(seg))
//                 continue;

//             var parts = seg.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
//             if (parts.Length != 8)
//                 continue;

//             string name = parts[0];
//             if (!string.Equals(name, targetName, StringComparison.Ordinal))
//                 continue;

//             if (!TryF(parts[1], out float px) ||
//                 !TryF(parts[2], out float py) ||
//                 !TryF(parts[3], out float pz) ||
//                 !TryF(parts[4], out float rx) ||
//                 !TryF(parts[5], out float ry) ||
//                 !TryF(parts[6], out float rz) ||
//                 !TryF(parts[7], out float rw))
//             {
//                 return false;
//             }

//             pose = new PoseData(
//                 new Vector3(px, py, pz),
//                 new Quaternion(rx, ry, rz, rw));

//             return true;
//         }

//         return false;
//     }

//     private void BuildSceneObjectCache()
//     {
//         _nameToTransform.Clear();

//         var scene = SceneManager.GetActiveScene();
//         foreach (var root in scene.GetRootGameObjects())
//         {
//             foreach (var t in root.GetComponentsInChildren<Transform>(true))
//                 _nameToTransform[t.gameObject.name] = t;
//         }

//         Debug.Log($"[ComfortEval] Cached {_nameToTransform.Count} scene objects by name.");
//     }

//     private List<AnimatorState> SetAnimatorsEnabled(bool enabled)
//     {
//         var animators = FindObjectsOfType<Animator>(true);
//         var states = new List<AnimatorState>(animators.Length);

//         for (int i = 0; i < animators.Length; i++)
//         {
//             var a = animators[i];
//             states.Add(new AnimatorState
//             {
//                 animator = a,
//                 wasEnabled = a.enabled
//             });
//             a.enabled = enabled;
//         }

//         Debug.Log($"[ComfortEval] Animator override: set {animators.Length} animator(s) enabled={enabled}");
//         return states;
//     }

//     private void RestoreAnimatorStates(List<AnimatorState> states)
//     {
//         for (int i = 0; i < states.Count; i++)
//         {
//             if (states[i].animator != null)
//                 states[i].animator.enabled = states[i].wasEnabled;
//         }

//         Debug.Log($"[ComfortEval] Restored {states.Count} animator state(s).");
//     }

//     private static string ExtractActiveObjectFromFilename(string filename)
//     {
//         var stem = Path.GetFileNameWithoutExtension(filename);
//         if (string.IsNullOrEmpty(stem))
//             return null;

//         var parts = stem.Split('_');
//         return parts.Length >= 1 ? parts[0] : null;
//     }

//     private static string ExtractHandNameFromFilename(string filename)
//     {
//         var stem = Path.GetFileNameWithoutExtension(filename);
//         if (string.IsNullOrEmpty(stem))
//             return null;

//         var parts = stem.Split('_');
//         if (parts.Length < 2)
//             return null;

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
// }








