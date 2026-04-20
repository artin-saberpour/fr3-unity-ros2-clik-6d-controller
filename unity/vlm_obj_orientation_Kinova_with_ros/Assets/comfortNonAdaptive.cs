using System;
using System.Globalization;
using System.IO;
using UnityEngine;

public class comfortNonAdaptive : MonoBehaviour
{
    [Header("Folders relative to Assets")]
    [Tooltip("Keep using this for the old input / before-pose source")]
    public string oldCasesFolderRelativeToAssets = "trajecotries_march26";

    [Tooltip("NEW: target positions come from here")]
    public string handoverTargetFolderRelativeToAssets = "handover_pos_nonAdaptive";

    private static readonly IFormatProvider Inv = CultureInfo.InvariantCulture;

    public void ProcessAllCases()
    {
        string oldCasesFolder = Path.Combine(Application.dataPath, oldCasesFolderRelativeToAssets);
        string handoverFolder = Path.Combine(Application.dataPath, handoverTargetFolderRelativeToAssets);

        if (!Directory.Exists(oldCasesFolder))
        {
            Debug.LogError($"Old cases folder not found: {oldCasesFolder}");
            return;
        }

        if (!Directory.Exists(handoverFolder))
        {
            Debug.LogError($"handover_pos_nonAdaptive folder not found: {handoverFolder}");
            return;
        }

        string[] caseFiles = Directory.GetFiles(oldCasesFolder, "*.txt", SearchOption.AllDirectories);

        foreach (string oldCaseFile in caseFiles)
        {
            string relativePath = Path.GetRelativePath(oldCasesFolder, oldCaseFile);
            string handoverFile = Path.Combine(handoverFolder, relativePath);

            // 1) BEFORE pose still comes from the old source / old logic
            //    Keep your existing code here.
            bool beforeLoaded = TryLoadBeforePoseFromOldVersion(oldCaseFile, out PoseData beforePose);
            if (!beforeLoaded)
            {
                Debug.LogWarning($"Skipping case because before pose could not be loaded: {oldCaseFile}");
                continue;
            }

            // 2) AFTER target position now comes from handover_pos_nonAdaptive
            if (!File.Exists(handoverFile))
            {
                Debug.LogWarning($"Skipping case because matching handover target file does not exist: {handoverFile}");
                continue;
            }

            if (!TryReadTargetPositionFromHandoverFile(handoverFile, out Vector3 targetPosition))
            {
                Debug.LogWarning($"Skipping case because handover target file is malformed: {handoverFile}");
                continue;
            }

            // 3) Build the AFTER pose exactly like the old version,
            //    except the hand target now uses targetPosition from handover_pos_nonAdaptive
            bool afterSolved = TrySolveAfterPoseUsingTargetPosition(beforePose, targetPosition, out PoseData afterPose);
            if (!afterSolved)
            {
                Debug.LogWarning($"Skipping case because after pose could not be solved: {handoverFile}");
                continue;
            }

            // 4) Compute angle changes the same way as in the old code
            AngleChangeResult result = ComputeAngleChanges(beforePose, afterPose);

            // 5) Save / log / accumulate result exactly as you already do
            SaveResultForCase(oldCaseFile, result);

            Debug.Log($"Processed: {relativePath} | target={targetPosition}");
        }
    }

    private static bool TryReadTargetPositionFromHandoverFile(string filePath, out Vector3 targetPosition)
    {
        targetPosition = Vector3.zero;

        string line;
        try
        {
            line = File.ReadAllText(filePath).Trim();
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to read handover target file '{filePath}': {ex.Message}");
            return false;
        }

        if (string.IsNullOrWhiteSpace(line))
            return false;

        // Expected format:
        // [-0.63756515  0.55221274 -0.55214103]
        line = line.Trim();

        if (line.StartsWith("["))
            line = line.Substring(1);

        if (line.EndsWith("]"))
            line = line.Substring(0, line.Length - 1);

        // Split on any whitespace, including repeated spaces/tabs
        string[] parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
            return false;

        if (!float.TryParse(parts[0], NumberStyles.Float, Inv, out float x))
            return false;

        if (!float.TryParse(parts[1], NumberStyles.Float, Inv, out float y))
            return false;

        if (!float.TryParse(parts[2], NumberStyles.Float, Inv, out float z))
            return false;

        targetPosition = new Vector3(x, y, z);
        return true;
    }

    // --------------------------------------------------------------------
    // KEEP / ADAPT THESE TO MATCH YOUR OLD SCRIPT
    // --------------------------------------------------------------------

    private bool TryLoadBeforePoseFromOldVersion(string oldCaseFile, out PoseData beforePose)
    {
        beforePose = default;

        // Keep your OLD logic here.
        // This should still load the user's before pose from the same place as before.

        return true;
    }

    private bool TrySolveAfterPoseUsingTargetPosition(PoseData beforePose, Vector3 targetPosition, out PoseData afterPose)
    {
        afterPose = default;

        // Keep your OLD after-case logic here,
        // but replace the old target source with:
        //
        //     targetPosition
        //
        // Example:
        // handTransform.position = targetPosition;
        // run IK / articulation / forward kinematics
        // read resulting joint angles into afterPose

        return true;
    }

    private AngleChangeResult ComputeAngleChanges(PoseData beforePose, PoseData afterPose)
    {
        // Keep your OLD angle-difference logic here.
        // Example:
        // shoulderDelta = Mathf.Abs(Mathf.DeltaAngle(beforePose.shoulder, afterPose.shoulder));
        // armDelta      = Mathf.Abs(Mathf.DeltaAngle(beforePose.arm, afterPose.arm));
        // forearmDelta  = Mathf.Abs(Mathf.DeltaAngle(beforePose.forearm, afterPose.forearm));
        // handDelta     = Mathf.Abs(Mathf.DeltaAngle(beforePose.hand, afterPose.hand));

        return new AngleChangeResult();
    }

    private void SaveResultForCase(string oldCaseFile, AngleChangeResult result)
    {
        // Keep your OLD save logic here.
    }
}

// ------------------------------------------------------------------------
// Example placeholders - replace with your real data structures
// ------------------------------------------------------------------------

[Serializable]
public struct PoseData
{
    public float shoulder;
    public float arm;
    public float forearm;
    public float hand;
}

[Serializable]
public struct AngleChangeResult
{
    public float shoulderDelta;
    public float armDelta;
    public float forearmDelta;
    public float handDelta;
    public float average;
}