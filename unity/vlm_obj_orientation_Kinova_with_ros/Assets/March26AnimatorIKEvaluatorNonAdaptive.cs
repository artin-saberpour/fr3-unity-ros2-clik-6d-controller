using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

public class March26AnimatorIKEvaluatorNonAdaptive : MonoBehaviour
{
    [Header("Input")]
    public string trajectoriesFolderRelativeToStreamingAssets = "trajectories_march26";
    public string nonAdaptiveFolderRelativeToStreamingAssets = "handover_pos_nonAdaptive";
    public string fileExtension = "*.txt";

    [Header("Output")]
    public string outputRelativePath = "trajectory/march26_animator_ik_comfort_nonAdaptive";

    [Header("Animator")]
    public Animator animator;

    [Header("Bone names")]
    public string leftUpperArmName = "mixamorig4:LeftArm";
    public string leftForeArmName = "mixamorig4:LeftForeArm";
    public string leftHandName = "mixamorig4:LeftHand";
    public string rightUpperArmName = "mixamorig4:RightArm";
    public string rightForeArmName = "mixamorig4:RightForeArm";
    public string rightHandName = "mixamorig4:RightHand";

    [Header("Options")]
    public Vector3 afterTargetOffset = Vector3.zero;
    public bool runOnStart = false;

    private static readonly IFormatProvider Inv = CultureInfo.InvariantCulture;

    private Transform _leftUpperArm;
    private Transform _leftForeArm;
    private Transform _leftHand;
    private Transform _rightUpperArm;
    private Transform _rightForeArm;
    private Transform _rightHand;

    private bool _ikActive;
    private AvatarIKGoal _activeGoal;
    private Vector3 _activeTargetPos;
    private float _posWeight;
    private bool _ikAppliedThisFrame;

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

    private struct Result
    {
        public float arm;
        public float foreArm;
        public float hand;
        public float sum;
        public float average;
        public float reachability;
    }

    private void Awake()
    {
        if (animator == null)
            animator = GetComponent<Animator>();

        CacheBones();
    }

    private void Start()
    {
        if (runOnStart)
            StartCoroutine(ExportBatchCoroutine());
    }

    [ContextMenu("Export March26 Animator IK Comfort NonAdaptive")]
    public void ExportBatch()
    {
        StartCoroutine(ExportBatchCoroutine());
    }

    private void CacheBones()
    {
        var all = GetComponentsInChildren<Transform>(true);
        _leftUpperArm = all.FirstOrDefault(t => t.name == leftUpperArmName);
        _leftForeArm = all.FirstOrDefault(t => t.name == leftForeArmName);
        _leftHand = all.FirstOrDefault(t => t.name == leftHandName);
        _rightUpperArm = all.FirstOrDefault(t => t.name == rightUpperArmName);
        _rightForeArm = all.FirstOrDefault(t => t.name == rightForeArmName);
        _rightHand = all.FirstOrDefault(t => t.name == rightHandName);
    }

    private IEnumerator ExportBatchCoroutine()
    {
        if (animator == null)
        {
            Debug.LogError("Missing Animator.");
            yield break;
        }

        if (!animator.isHuman)
        {
            Debug.LogError("Animator IK requires a Humanoid avatar.");
            yield break;
        }

        string trajectoriesDir = Path.Combine(Application.streamingAssetsPath, trajectoriesFolderRelativeToStreamingAssets);
        string nonAdaptiveDir = Path.Combine(Application.streamingAssetsPath, nonAdaptiveFolderRelativeToStreamingAssets);
        string outputDir = Path.Combine(Application.dataPath, outputRelativePath);

        if (!Directory.Exists(trajectoriesDir))
        {
            Debug.LogError("Input folder not found: " + trajectoriesDir);
            yield break;
        }

        if (!Directory.Exists(nonAdaptiveDir))
        {
            Debug.LogError("NonAdaptive folder not found: " + nonAdaptiveDir);
            yield break;
        }

        Directory.CreateDirectory(outputDir);

        var files = Directory.GetFiles(trajectoriesDir, fileExtension, SearchOption.TopDirectoryOnly)
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        List<Result> allResults = new List<Result>();

        foreach (var trajectoryPath in files)
        {
            string fileName = Path.GetFileName(trajectoryPath);
            string outPath = Path.Combine(outputDir, fileName);

            string relativeHand = ExtractRelativeHandFromFilename(fileName);
            if (string.IsNullOrWhiteSpace(relativeHand))
            {
                Debug.LogWarning("[IK Eval] Could not determine left/right hand from filename: " + fileName);
                File.WriteAllText(outPath, "NaN");
                continue;
            }

            string[] rawLines = File.ReadAllLines(trajectoryPath);
            string lastFrame = GetLastNonEmptyFrame(rawLines);

            if (string.IsNullOrWhiteSpace(lastFrame))
            {
                Debug.LogWarning("[IK Eval] No valid frame in trajectory file: " + fileName);
                File.WriteAllText(outPath, "NaN");
                continue;
            }

            if (!TryExtractPoseFromFrame(lastFrame, relativeHand, out PoseData handPose))
            {
                Debug.LogWarning("[IK Eval] Could not find " + relativeHand + " in last frame of: " + fileName);
                File.WriteAllText(outPath, "NaN");
                continue;
            }

            string nonAdaptivePath = FindMatchingNonAdaptiveFile(nonAdaptiveDir, fileName);
            if (string.IsNullOrWhiteSpace(nonAdaptivePath) || !File.Exists(nonAdaptivePath))
            {
                Debug.LogWarning("[IK Eval] No matching nonAdaptive target file for: " + fileName);
                File.WriteAllText(outPath, "NaN");
                continue;
            }

            if (!TryReadVector3FromBracketFile(nonAdaptivePath, out Vector3 after))
            {
                Debug.LogWarning("[IK Eval] Could not parse nonAdaptive target from: " + nonAdaptivePath);
                File.WriteAllText(outPath, "NaN");
                continue;
            }

            Vector3 before = handPose.position;
            after += afterTargetOffset;

            Result? result = null;
            yield return StartCoroutine(EvaluateOneFile(relativeHand, before, after, r => result = r));

            if (result.HasValue)
            {
                string line =
                    $"Arm={result.Value.arm.ToString("G9", Inv)};" +
                    $"ForeArm={result.Value.foreArm.ToString("G9", Inv)};" +
                    $"Hand={result.Value.hand.ToString("G9", Inv)};" +
                    $"Sum={result.Value.sum.ToString("G9", Inv)};" +
                    $"Average={result.Value.average.ToString("G9", Inv)};" +
                    $"Reachability={result.Value.reachability.ToString("G9", Inv)}";

                File.WriteAllText(outPath, line);
                allResults.Add(result.Value);
            }
            else
            {
                Debug.LogWarning("[IK Eval] IK evaluation failed for: " + fileName);
                File.WriteAllText(outPath, "NaN");
            }

            yield return null;
        }

        SaveOverallAverageFile(outputDir, allResults);
        Debug.Log("[IK Eval] Done.");
    }

    private IEnumerator EvaluateOneFile(string relativeHand, Vector3 beforeTarget, Vector3 afterTarget, Action<Result?> callback)
    {
        if (!TryGetChain(relativeHand, out Transform upperArm, out Transform foreArm, out Transform hand))
        {
            Debug.LogWarning("[IK Eval] Missing arm chain for hand: " + relativeHand);
            callback(null);
            yield break;
        }

        PoseData upperArmPose = new PoseData(upperArm.position, upperArm.rotation);
        PoseData foreArmPose = new PoseData(foreArm.position, foreArm.rotation);
        PoseData handPose = new PoseData(hand.position, hand.rotation);

        AvatarIKGoal goal = relativeHand == "left_hand" ? AvatarIKGoal.LeftHand : AvatarIKGoal.RightHand;
        float reachability = IsTargetReachable(upperArm, foreArm, hand, afterTarget) ? 0f : 1f;

        yield return StartCoroutine(ApplyIKAndWait(goal, beforeTarget, 1f));
        JointSnapshot before = CaptureJointSnapshot(upperArm, foreArm, hand);

        RestoreWorldPose(upperArm, upperArmPose);
        RestoreWorldPose(foreArm, foreArmPose);
        RestoreWorldPose(hand, handPose);
        ClearIK();
        animator.Update(0f);
        yield return null;

        yield return StartCoroutine(ApplyIKAndWait(goal, afterTarget, 1f));
        JointSnapshot after = CaptureJointSnapshot(upperArm, foreArm, hand);

        RestoreWorldPose(upperArm, upperArmPose);
        RestoreWorldPose(foreArm, foreArmPose);
        RestoreWorldPose(hand, handPose);
        ClearIK();
        animator.Update(0f);
        yield return null;

        float arm = Quaternion.Angle(before.upperArmLocal, after.upperArmLocal);
        float foreArmAngle = Quaternion.Angle(before.foreArmLocal, after.foreArmLocal);
        float handAngle = Quaternion.Angle(before.handLocal, after.handLocal);
        float sum = arm + foreArmAngle + handAngle;
        float average = sum / 3f;

        callback(new Result
        {
            arm = arm,
            foreArm = foreArmAngle,
            hand = handAngle,
            sum = sum,
            average = average,
            reachability = reachability
        });
    }

    private bool IsTargetReachable(Transform upperArm, Transform foreArm, Transform hand, Vector3 targetWorld)
    {
        if (upperArm == null || foreArm == null || hand == null)
            return false;

        float upperLen = Vector3.Distance(upperArm.position, foreArm.position);
        float foreLen = Vector3.Distance(foreArm.position, hand.position);
        float targetDist = Vector3.Distance(upperArm.position, targetWorld);

        float minReach = Mathf.Abs(upperLen - foreLen);
        float maxReach = upperLen + foreLen;

        return targetDist >= minReach && targetDist <= maxReach;
    }

    // private void SaveOverallAverageFile(string outputDir, List<Result> results)
    // {
    //     string outPath = Path.Combine(outputDir, "_overall_average.txt");

    //     if (results == null || results.Count == 0)
    //     {
    //         File.WriteAllText(outPath, "NaN");
    //         return;
    //     }

    //     float armAvg = results.Average(r => r.arm);
    //     float foreArmAvg = results.Average(r => r.foreArm);
    //     float handAvg = results.Average(r => r.hand);
    //     float sumAvg = results.Average(r => r.sum);
    //     float averageAvg = results.Average(r => r.average);
    //     float reachabilityPercentage = results.Average(r => r.reachability) * 100f;

    //     string line =
    //         $"Arm={armAvg.ToString("G9", Inv)};" +
    //         $"ForeArm={foreArmAvg.ToString("G9", Inv)};" +
    //         $"Hand={handAvg.ToString("G9", Inv)};" +
    //         $"Sum={sumAvg.ToString("G9", Inv)};" +
    //         $"Average={averageAvg.ToString("G9", Inv)};" +
    //         $"ReachabilityPercentage={reachabilityPercentage.ToString("G9", Inv)}";

    //     File.WriteAllText(outPath, line);
    // }


// private void SaveOverallAverageFile(string outputDir, List<Result> results)
// {
//     string outPath = Path.Combine(outputDir, "_overall_average.txt");

//     if (results == null || results.Count == 0)
//     {
//         File.WriteAllText(outPath, "NaN");
//         return;
//     }

//     List<Result> reachableResults = results.Where(r => r.reachability > 0.5f).ToList();

//     float armAvg = reachableResults.Count > 0 ? reachableResults.Average(r => r.arm) : float.NaN;
//     float foreArmAvg = reachableResults.Count > 0 ? reachableResults.Average(r => r.foreArm) : float.NaN;
//     float handAvg = reachableResults.Count > 0 ? reachableResults.Average(r => r.hand) : float.NaN;
//     float sumAvg = reachableResults.Count > 0 ? reachableResults.Average(r => r.sum) : float.NaN;
//     float averageAvg = reachableResults.Count > 0 ? reachableResults.Average(r => r.average) : float.NaN;

//     float reachabilityPercentage = results.Average(r => r.reachability) * 100f;

//     string line =
//         $"Arm={armAvg.ToString("G9", Inv)};" +
//         $"ForeArm={foreArmAvg.ToString("G9", Inv)};" +
//         $"Hand={handAvg.ToString("G9", Inv)};" +
//         $"Sum={sumAvg.ToString("G9", Inv)};" +
//         $"Average={averageAvg.ToString("G9", Inv)};" +
//         $"ReachabilityPercentage={reachabilityPercentage.ToString("G9", Inv)}";

//     File.WriteAllText(outPath, line);
// }




private float Mean(IEnumerable<float> values)
{
    if (values == null || !values.Any())
        return float.NaN;

    float sum = 0f;
    int count = 0;

    foreach (var v in values)
    {
        sum += v;
        count++;
    }

    return sum / count;
}

private float SD(IEnumerable<float> values)
{
    if (values == null || !values.Any())
        return float.NaN;

    float mean = Mean(values);

    float sumSq = 0f;
    int count = 0;

    foreach (var v in values)
    {
        float d = v - mean;
        sumSq += d * d;
        count++;
    }

    return Mathf.Sqrt(sumSq / count); // population SD
}

private void SaveOverallAverageFile(string outputDir, List<Result> results)
{
    string outPath = Path.Combine(outputDir, "_overall_average.txt");

    if (results == null || results.Count == 0)
    {
        File.WriteAllText(outPath, "NaN");
        return;
    }

    List<Result> reachableResults = results.Where(r => r.reachability > 0.5f).ToList();

    float armAvg = Mean(reachableResults.Select(r => r.arm));
    float foreArmAvg = Mean(reachableResults.Select(r => r.foreArm));
    float handAvg = Mean(reachableResults.Select(r => r.hand));
    float sumAvg = Mean(reachableResults.Select(r => r.sum));
    float averageAvg = Mean(reachableResults.Select(r => r.average));

    float armSd = SD(reachableResults.Select(r => r.arm));
    float foreArmSd = SD(reachableResults.Select(r => r.foreArm));
    float handSd = SD(reachableResults.Select(r => r.hand));
    float sumSd = SD(reachableResults.Select(r => r.sum));
    float averageSd = SD(reachableResults.Select(r => r.average));

    float reachabilityPercentage = results.Average(r => r.reachability) * 100f;

    string line =
        $"Arm={armAvg.ToString("G9", Inv)};" +
        $"ForeArm={foreArmAvg.ToString("G9", Inv)};" +
        $"Hand={handAvg.ToString("G9", Inv)};" +
        $"Sum={sumAvg.ToString("G9", Inv)};" +
        $"Average={averageAvg.ToString("G9", Inv)};" +

        $"ArmSD={armSd.ToString("G9", Inv)};" +
        $"ForeArmSD={foreArmSd.ToString("G9", Inv)};" +
        $"HandSD={handSd.ToString("G9", Inv)};" +
        $"SumSD={sumSd.ToString("G9", Inv)};" +
        $"AverageSD={averageSd.ToString("G9", Inv)};" +

        $"ReachabilityPercentage={reachabilityPercentage.ToString("G9", Inv)}";

    File.WriteAllText(outPath, line);
}


    private IEnumerator ApplyIKAndWait(AvatarIKGoal goal, Vector3 worldPos, float posWeight)
    {
        _activeGoal = goal;
        _activeTargetPos = worldPos;
        _posWeight = posWeight;
        _ikActive = true;
        _ikAppliedThisFrame = false;

        animator.Update(0f);
        yield return null;

        if (!_ikAppliedThisFrame)
            animator.Update(0f);
    }

    private void ClearIK()
    {
        _ikActive = false;
        _posWeight = 0f;

        if (animator != null)
        {
            animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
            animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
            animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
            animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
        }
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (!_ikActive || animator == null)
            return;

        animator.SetIKPositionWeight(_activeGoal, _posWeight);
        animator.SetIKPosition(_activeGoal, _activeTargetPos);
        animator.SetIKRotationWeight(_activeGoal, 0f);

        _ikAppliedThisFrame = true;
    }

    private JointSnapshot CaptureJointSnapshot(Transform upperArm, Transform foreArm, Transform hand)
    {
        return new JointSnapshot
        {
            upperArmLocal = upperArm.localRotation,
            foreArmLocal = foreArm.localRotation,
            handLocal = hand.localRotation
        };
    }

    private bool TryGetChain(string handName, out Transform upperArm, out Transform foreArm, out Transform hand)
    {
        if (handName == "left_hand")
        {
            upperArm = _leftUpperArm;
            foreArm = _leftForeArm;
            hand = _leftHand;
        }
        else
        {
            upperArm = _rightUpperArm;
            foreArm = _rightForeArm;
            hand = _rightHand;
        }

        return upperArm != null && foreArm != null && hand != null;
    }

    private void RestoreWorldPose(Transform tr, PoseData pose)
    {
        if (tr == null) return;
        tr.position = pose.position;
        tr.rotation = pose.rotation;
    }

    private string GetLastNonEmptyFrame(string[] rawLines)
    {
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

        if (line.StartsWith("TRANSFORM|", StringComparison.OrdinalIgnoreCase))
            line = line.Substring("TRANSFORM|".Length);

        string[] segments = line.Split(';');

        foreach (string rawSeg in segments)
        {
            string seg = rawSeg.Trim();
            if (string.IsNullOrEmpty(seg))
                continue;

            string[] parts = seg.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 8)
                continue;

            if (!string.Equals(parts[0], targetName, StringComparison.Ordinal))
                continue;

            if (!TryF(parts[1], out float px) ||
                !TryF(parts[2], out float py) ||
                !TryF(parts[3], out float pz) ||
                !TryF(parts[4], out float qx) ||
                !TryF(parts[5], out float qy) ||
                !TryF(parts[6], out float qz) ||
                !TryF(parts[7], out float qw))
            {
                return false;
            }

            pose = new PoseData(
                new Vector3(px, py, pz),
                new Quaternion(qx, qy, qz, qw));

            return true;
        }

        return false;
    }

    // private bool TryReadVector3FromBracketFile(string path, out Vector3 value)
    // {
    //     value = Vector3.zero;

    //     if (!File.Exists(path))
    //         return false;

    //     string firstLine = File.ReadLines(path)
    //         .FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));

    //     if (string.IsNullOrWhiteSpace(firstLine))
    //         return false;

    //     string text = firstLine.Trim();
    //     text = text.Trim('[', ']');

    //     string[] parts = text.Split(',');

    //     if (parts.Length != 3)
    //         return false;

    //     if (!TryF(parts[0].Trim(), out float x) ||
    //         !TryF(parts[1].Trim(), out float y) ||
    //         !TryF(parts[2].Trim(), out float z))
    //     {
    //         return false;
    //     }

    //     value = new Vector3(x, y, z);
    //     return true;
    // }

    private bool TryReadVector3FromBracketFile(string path, out Vector3 value)
    {
        value = Vector3.zero;

        if (!File.Exists(path))
            return false;

        string text = File.ReadAllText(path).Trim();

        if (string.IsNullOrWhiteSpace(text))
            return false;

        text = text.Replace("[", "").Replace("]", "").Trim();

        string[] parts = text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 3)
            return false;

        if (!TryF(parts[0], out float x) ||
            !TryF(parts[1], out float y) ||
            !TryF(parts[2], out float z))
        {
            return false;
        }

        value = new Vector3(x, y, z);
        return true;
    }
    private string FindMatchingNonAdaptiveFile(string nonAdaptiveDir, string trajectoryFileName)
    {
        string exact = Path.Combine(nonAdaptiveDir, trajectoryFileName);
        if (File.Exists(exact))
            return exact;

        string stem = Path.GetFileNameWithoutExtension(trajectoryFileName);

        var sameStem = Directory.GetFiles(nonAdaptiveDir, "*.txt", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(p =>
                string.Equals(
                    Path.GetFileNameWithoutExtension(p),
                    stem,
                    StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(sameStem))
            return sameStem;

        return null;
    }

    private static string ExtractRelativeHandFromFilename(string filename)
    {
        string stem = Path.GetFileNameWithoutExtension(filename).ToLowerInvariant();

        if (stem.Contains("left"))
            return "left_hand";

        if (stem.Contains("right"))
            return "right_hand";

        return null;
    }

    private static bool TryF(string s, out float v)
    {
        return float.TryParse(
            s,
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out v);
    }
}