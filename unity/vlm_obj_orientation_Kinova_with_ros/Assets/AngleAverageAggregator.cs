using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using Meta.XR.ImmersiveDebugger.UserInterface;


#if UNITY_EDITOR
using UnityEditor;
#endif

public class AngleAverageAggregator : MonoBehaviour
{
    [Header("Input Folder")]
    [Tooltip("Folder relative to Assets/. Example: trajectory/ours/angles")]
    public string inputFolderRelativeToAssets = "trajectory/ours/angles";

    [Header("Output File")]
    [Tooltip("Output file name to create inside the same folder")]
    public string outputFileName = "average_angles_ours.txt";

    [Header("Options")]
    [Tooltip("If true, ignores invalid or empty lines")]
    public bool skipInvalidLines = true;

    [Tooltip("Use sample standard deviation (n-1). If false, population SD is used.")]
    public bool useSampleStandardDeviation = true;

    private static readonly IFormatProvider Inv = CultureInfo.InvariantCulture;

    [ContextMenu("Compute Angle Averages")]
    public void ComputeAngleAverages()
    {
        string inputFolder = Path.Combine(Application.dataPath, inputFolderRelativeToAssets);

        if (!Directory.Exists(inputFolder))
        {
            Debug.LogError($"[AngleAverageAggregator] Input folder not found: {inputFolder}");
            return;
        }

        string[] files = Directory
            .GetFiles(inputFolder, "*.txt", SearchOption.TopDirectoryOnly)
            .Where(f => !string.Equals(Path.GetFileName(f), outputFileName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (files.Length == 0)
        {
            Debug.LogWarning($"[AngleAverageAggregator] No txt files found in: {inputFolder}");
            return;
        }

        Dictionary<string, List<float>> objectToAngles = new Dictionary<string, List<float>>(StringComparer.OrdinalIgnoreCase);

        int processedFiles = 0;
        int skippedFiles = 0;
        int processedAngles = 0;
        int skippedLines = 0;

        foreach (string filePath in files)
        {
            string fileName = Path.GetFileName(filePath);
            string activeObject = ExtractActiveObjectFromFilename(fileName);

            if (string.IsNullOrWhiteSpace(activeObject))
            {
                Debug.LogWarning($"[AngleAverageAggregator] Could not extract active object from filename: {fileName}");
                skippedFiles++;
                continue;
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(filePath);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AngleAverageAggregator] Failed reading file '{fileName}': {ex.Message}");
                skippedFiles++;
                continue;
            }

            if (!objectToAngles.TryGetValue(activeObject, out var angleList))
            {
                angleList = new List<float>();
                objectToAngles[activeObject] = angleList;
            }

            bool fileHadValidValue = false;

            // foreach (string rawLine in lines.Skip(5))
            foreach (string rawLine in lines.TakeLast(5))
            {
                string line = rawLine.Trim();

                if (string.IsNullOrWhiteSpace(line))
                {
                    skippedLines++;
                    continue;
                }

                if (float.TryParse(line, NumberStyles.Float | NumberStyles.AllowThousands, Inv, out float angleValue))
                {
                    if (!float.IsNaN(angleValue) && !float.IsInfinity(angleValue))
                    {
                        angleList.Add(angleValue);
                        processedAngles++;
                        fileHadValidValue = true;
                    }
                    else
                    {
                        skippedLines++;
                    }
                }
                else
                {
                    if (!skipInvalidLines)
                        Debug.LogWarning($"[AngleAverageAggregator] Invalid float in file '{fileName}': {line}");

                    skippedLines++;
                }
            }

            if (fileHadValidValue)
                processedFiles++;
            else
                skippedFiles++;
        }

        if (objectToAngles.Count == 0 || objectToAngles.All(kvp => kvp.Value.Count == 0))
        {
            Debug.LogWarning("[AngleAverageAggregator] No valid angle values were collected.");
            return;
        }

        List<string> outputLines = new List<string>();

        foreach (var kvp in objectToAngles.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            string activeObject = kvp.Key;
            List<float> values = kvp.Value;

            if (values.Count == 0)
                continue;

            double mean = values.Average(v => (double)v);
            double minimum = values.Min(v => (double)v);
            double sd = ComputeStandardDeviation(values, useSampleStandardDeviation);

            // Example:
            // banana;Mean=42.1532;SD=5.4721;Count=120
            string line =
                $"{activeObject};" +
                $"Mean={mean.ToString("G9", Inv)};" +
                $"SD={sd.ToString("G9", Inv)};" +
                $"Count={values.Count}";

            outputLines.Add(line);
        }

        string outputPath = Path.Combine(inputFolder, outputFileName);

        try
        {
            File.WriteAllLines(outputPath, outputLines);
            Debug.Log($"[AngleAverageAggregator] Done. ProcessedFiles={processedFiles}, SkippedFiles={skippedFiles}, ProcessedAngles={processedAngles}, SkippedLines={skippedLines}, Output={outputPath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[AngleAverageAggregator] Failed writing output file: {ex.Message}");
            return;
        }

#if UNITY_EDITOR
        AssetDatabase.Refresh();
#endif
    }

    private static string ExtractActiveObjectFromFilename(string filename)
    {
        string stem = Path.GetFileNameWithoutExtension(filename);
        if (string.IsNullOrWhiteSpace(stem))
            return null;

        string[] parts = stem.Split('_');
        if (parts.Length == 0)
            return null;

        return parts[0].Trim();
    }

    private static double ComputeStandardDeviation(List<float> values, bool sampleSd)
    {
        if (values == null || values.Count == 0)
            return 0.0;

        if (values.Count == 1)
            return 0.0;

        double mean = values.Average(v => (double)v);
        double sumSq = 0.0;

        for (int i = 0; i < values.Count; i++)
        {
            double diff = values[i] - mean;
            sumSq += diff * diff;
        }

        double divisor = sampleSd ? (values.Count - 1) : values.Count;
        return Math.Sqrt(sumSq / divisor);
    }
}