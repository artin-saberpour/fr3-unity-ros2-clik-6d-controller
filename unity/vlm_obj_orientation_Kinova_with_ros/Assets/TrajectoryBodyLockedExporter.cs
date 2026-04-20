using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor; // to refresh Project window after writing files
#endif

/// <summary>
/// Exports per-frame average angles with body parts locked to frame 1 of each file.
/// Writes one output file per active object under Assets/avgAnglesBL1/
/// Format:
///   <inputFileName1>
///   <avgFrame1>
///   <avgFrame2>
///   ...
///
///   <inputFileName2>
///   ...
/// </summary>
public class TrajectoryBodyLockedExporter : MonoBehaviour
{
    [Header("Input")]
    [Tooltip("Folder relative to StreamingAssets containing trajectory .txt files.")]
    public string folderRelativeToStreamingAssets = "trajectories";

    [Tooltip("Glob extension to search for.")]
    public string fileExtension = "*.txt";

    [Header("Output")]
    [Tooltip("Relative to Assets/ . Files will be written into Assets/avgAnglesBL1/")]
    public string outputFolderUnderAssets = "avgAnglesBL1";

    [ContextMenu("Export Avg Angles (Body Locked @ Frame1)")]
    public void ExportAvgAnglesBodyLockedAtFrame1()
    {
        // Locate trajectory folder
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

        // Build constraint set once
        var constraints = TrajectorySimCore.BuildSimConstraints();

        // Group files by active object (filename part 1)
        var groups = files
            .GroupBy(p => TrajectorySimCore.ExtractActiveObjectFromFilename(Path.GetFileName(p)),
                     StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        // Prepare output directory under Assets/
        string outDir = Path.Combine(Application.dataPath, outputFolderUnderAssets);
        Directory.CreateDirectory(outDir);

        int objectCount = 0, fileCount = 0, frameCount = 0;

        foreach (var grp in groups)
        {
            string objName = grp.Key;
            if (string.IsNullOrEmpty(objName)) continue;

            // Only export for objects that have constraints
            if (constraints == null || !constraints.ContainsKey(objName) ||
                constraints[objName] == null || constraints[objName].Count == 0)
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
                    string hand = TrajectorySimCore.ExtractHandNameFromFilename(fname); // "left_hand"/"right_hand"/null

                    sw.WriteLine(fname);

                    var lines = File.ReadAllLines(path);

                    // ——— Build body-lock map from the first non-empty line ———
                    Dictionary<string, TrajectorySimCore.SimTrans> bodyLock = null;
                    List<string> bodyNames = null;

                    foreach (var raw in lines)
                    {
                        var first = raw.Trim();
                        if (string.IsNullOrEmpty(first)) continue;

                        bodyNames = TrajectorySimCore.ExtractBodyPartNamesFromLine(first);
                        var fullMapFirst = TrajectorySimCore.ParseSimFrame(first);

                        if (bodyNames != null && bodyNames.Count > 0)
                        {
                            bodyLock = new Dictionary<string, TrajectorySimCore.SimTrans>(StringComparer.Ordinal);
                            foreach (var bn in bodyNames)
                                if (fullMapFirst.TryGetValue(bn, out var tr))
                                    bodyLock[bn] = tr;
                        }
                        break;
                    }

                    // ——— Per-frame: override body parts with frame-1 values, compute avg ———
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var line = lines[i].Trim();
                        if (string.IsNullOrEmpty(line)) { sw.WriteLine("NaN"); continue; }

                        var map = TrajectorySimCore.ParseSimFrame(line);

                        if (bodyLock != null)
                        {
                            foreach (var kv in bodyLock)
                                map[kv.Key] = kv.Value; // lock body part to frame-1
                        }

                        float? avg = TrajectorySimCore.ComputeAvgAngle(map, objName, hand, constraints);
                        sw.WriteLine(avg.HasValue ? avg.Value.ToString("G9", CultureInfo.InvariantCulture) : "NaN");
                        frameCount++;
                    }

                    sw.WriteLine(); // blank line between files
                    fileCount++;
                }
            }

            objectCount++;
            Debug.Log($"[BL1] Wrote: Assets/{outputFolderUnderAssets}/{objName}.txt");
        }

#if UNITY_EDITOR
        AssetDatabase.Refresh(); // show new/updated files in Project window
#endif

        Debug.Log($"[BL1] Export complete → Assets/{outputFolderUnderAssets}  (objects: {objectCount}, files: {fileCount}, frames: {frameCount})");
    }
}
