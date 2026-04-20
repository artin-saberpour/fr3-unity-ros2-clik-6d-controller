using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class ComfortAverageAggregator : MonoBehaviour
{
    [Header("Input Folder")]
    [Tooltip("Folder relative to Assets/. Example: trajectory/ours/comfort")]
    public string inputFolderRelativeToAssets = "trajectory/ours/comfort";

    [Header("Output File")]
    [Tooltip("Output file name to create inside the same folder")]
    public string outputFileName = "comfort_average_ours.txt";

    [Header("Options")]
    [Tooltip("If true, ignores files whose content is NaN or malformed")]
    public bool skipInvalidFiles = true;

    [Tooltip("Use sample standard deviation (n-1). If false, population SD is used.")]
    public bool useSampleStandardDeviation = true;

    private static readonly IFormatProvider Inv = CultureInfo.InvariantCulture;

    [ContextMenu("Compute Comfort Averages")]
    public void ComputeComfortAverages()
    {
        string inputFolder = Path.Combine(Application.dataPath, inputFolderRelativeToAssets);

        if (!Directory.Exists(inputFolder))
        {
            Debug.LogError($"[ComfortAverageAggregator] Input folder not found: {inputFolder}");
            return;
        }

        string[] files = Directory
            .GetFiles(inputFolder, "*.txt", SearchOption.TopDirectoryOnly)
            .Where(f => !string.Equals(Path.GetFileName(f), outputFileName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (files.Length == 0)
        {
            Debug.LogWarning($"[ComfortAverageAggregator] No txt files found in: {inputFolder}");
            return;
        }

        Dictionary<string, List<float>> objectToAverageValues = new Dictionary<string, List<float>>(StringComparer.OrdinalIgnoreCase);

        int processed = 0;
        int skipped = 0;

        foreach (string filePath in files)
        {
            string fileName = Path.GetFileName(filePath);
            string activeObject = ExtractActiveObjectFromFilename(fileName);

            if (string.IsNullOrWhiteSpace(activeObject))
            {
                Debug.LogWarning($"[ComfortAverageAggregator] Could not extract active object from filename: {fileName}");
                skipped++;
                continue;
            }

            string content;
            try
            {
                content = File.ReadAllText(filePath).Trim();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ComfortAverageAggregator] Failed reading file '{fileName}': {ex.Message}");
                skipped++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(content) || content.Equals("NaN", StringComparison.OrdinalIgnoreCase))
            {
                if (!skipInvalidFiles)
                {
                    Debug.LogWarning($"[ComfortAverageAggregator] Invalid content in file: {fileName}");
                }
                skipped++;
                continue;
            }

            if (!TryExtractAverage(content, out float avgValue))
            {
                Debug.LogWarning($"[ComfortAverageAggregator] Could not parse Average from file: {fileName} | Content: {content}");
                skipped++;
                continue;
            }

            if (!objectToAverageValues.TryGetValue(activeObject, out var list))
            {
                list = new List<float>();
                objectToAverageValues[activeObject] = list;
            }

            list.Add(avgValue);
            processed++;
        }

        if (objectToAverageValues.Count == 0)
        {
            Debug.LogWarning("[ComfortAverageAggregator] No valid averages were collected.");
            return;
        }

        List<string> outputLines = new List<string>();

        foreach (var kvp in objectToAverageValues.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            string activeObject = kvp.Key;
            List<float> values = kvp.Value;

            double mean = values.Average(v => (double)v);
            double sd = ComputeStandardDeviation(values, useSampleStandardDeviation);

            // Example output:
            // banana;Mean=36.6295509;SD=4.81234567;Count=12
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
            Debug.Log($"[ComfortAverageAggregator] Done. Processed={processed}, Skipped={skipped}, Output={outputPath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ComfortAverageAggregator] Failed writing output file: {ex.Message}");
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

    private static bool TryExtractAverage(string content, out float averageValue)
    {
        averageValue = 0f;

        // Expected format:
        // Shoulder=0;Arm=75.9402008;ForeArm=70.5780029;Hand=0;Average=36.6295509

        string[] parts = content.Split(';');

        foreach (string part in parts)
        {
            if (string.IsNullOrWhiteSpace(part))
                continue;

            string[] kv = part.Split('=');
            if (kv.Length != 2)
                continue;

            string key = kv[0].Trim();
            string value = kv[1].Trim();

            if (key.Equals("Average", StringComparison.OrdinalIgnoreCase))
            {
                return float.TryParse(
                    value,
                    NumberStyles.Float | NumberStyles.AllowThousands,
                    Inv,
                    out averageValue
                );
            }
        }

        return false;
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