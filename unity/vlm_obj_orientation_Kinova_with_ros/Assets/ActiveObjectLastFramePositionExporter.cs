using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class ActiveObjectLastFramePositionExporter : MonoBehaviour
{
    [Header("File Source")]
    public string folderRelativeToStreamingAssets = "trajectories_march26";
    public string fileExtension = "*.txt";

    [Header("Filtering")]
    public string activeObjectFilter = "";

    [Header("Output")]
    // Written under Application.dataPath
    public string outputRelativePath = "trajectory/activeObjectLastFramePositions.txt";

    [Header("Logging")]
    public bool logPerFile = true;
    public bool logWarnings = true;

    private static readonly IFormatProvider Inv = CultureInfo.InvariantCulture;

    [ContextMenu("Export Active Object Last-Frame Positions")]
    public void ExportActiveObjectLastFramePositions()
    {
        string baseFolder = Path.Combine(Application.streamingAssetsPath, folderRelativeToStreamingAssets);
        string outPath = Path.Combine(Application.dataPath, outputRelativePath);

        Debug.Log($"[ActiveObjPos] Input folder: {baseFolder}");
        Debug.Log($"[ActiveObjPos] Output file: {outPath}");

        if (!Directory.Exists(baseFolder))
        {
            Debug.LogError($"[ActiveObjPos] Trajectory folder not found: {baseFolder}");
            return;
        }

        string outDir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(outDir))
            Directory.CreateDirectory(outDir);

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

        if (files.Length == 0)
        {
            Debug.LogWarning($"[ActiveObjPos] No trajectory files found in {baseFolder} matching '{prefix}*'.");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("FileName;ActiveObject;PosX;PosY;PosZ");

        int okCount = 0;
        int failCount = 0;

        foreach (var path in files)
        {
            string fileName = Path.GetFileName(path);
            string activeObj = ExtractActiveObjectFromFilename(fileName);

            try
            {
                string[] rawLines = File.ReadAllLines(path);
                string lastFrame = GetLastNonEmptyFrame(rawLines);

                if (string.IsNullOrEmpty(lastFrame))
                {
                    failCount++;
                    if (logWarnings)
                        Debug.LogWarning($"[ActiveObjPos] No valid frame in '{fileName}'.");
                    sb.AppendLine($"{fileName};{activeObj};NaN;NaN;NaN");
                    continue;
                }

                if (!TryExtractObjectPositionFromFrame(lastFrame, activeObj, out Vector3 pos))
                {
                    failCount++;
                    if (logWarnings)
                        Debug.LogWarning($"[ActiveObjPos] Active object '{activeObj}' not found in last frame of '{fileName}'.");
                    sb.AppendLine($"{fileName};{activeObj};NaN;NaN;NaN");
                    continue;
                }

                sb.AppendLine(
                    $"{fileName};{activeObj};" +
                    $"{pos.x.ToString("G9", Inv)};" +
                    $"{pos.y.ToString("G9", Inv)};" +
                    $"{pos.z.ToString("G9", Inv)}");

                if (logPerFile)
                    Debug.Log($"[ActiveObjPos] {fileName} -> {activeObj} = ({pos.x:G9}, {pos.y:G9}, {pos.z:G9})");

                okCount++;
            }
            catch (Exception e)
            {
                failCount++;
                Debug.LogError($"[ActiveObjPos] Failed processing '{fileName}': {e}");
                sb.AppendLine($"{fileName};{activeObj};NaN;NaN;NaN");
            }
        }

        File.WriteAllText(outPath, sb.ToString(), Encoding.UTF8);

#if UNITY_EDITOR
        AssetDatabase.Refresh();
#endif

        Debug.Log($"[ActiveObjPos] Done. Success={okCount}, Failed={failCount}, Saved='{outPath}'");
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

    private bool TryExtractObjectPositionFromFrame(string line, string objectName, out Vector3 position)
    {
        position = default;

        if (string.IsNullOrWhiteSpace(line) || string.IsNullOrWhiteSpace(objectName))
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
            if (!string.Equals(name, objectName, StringComparison.Ordinal))
                continue;

            if (!TryF(parts[1], out float px) ||
                !TryF(parts[2], out float py) ||
                !TryF(parts[3], out float pz))
            {
                return false;
            }

            position = new Vector3(px, py, pz);
            return true;
        }

        return false;
    }

    private static string ExtractActiveObjectFromFilename(string filename)
    {
        var stem = Path.GetFileNameWithoutExtension(filename);
        if (string.IsNullOrEmpty(stem))
            return null;

        var parts = stem.Split('_');
        return parts.Length >= 1 ? parts[0] : null;
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