// Assets/Editor/ImportSimpleWithTexture.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class ImportSimpleWithTexture
{
    private const string RootModelsFolder = "Assets/Models";
    private const bool OverwriteExistingInScene = false; // set true to replace same-named objects
    private const bool LayoutOnGrid = true;
    private const float GridSpacing = 1.5f;

    [MenuItem("Tools/Models/Import (Force PNG Texture, Ignore .mtl)")]
    public static void ImportAll()
    {
        if (!AssetDatabase.IsValidFolder(RootModelsFolder))
        {
            EditorUtility.DisplayDialog("Importer", $"Folder not found:\n{RootModelsFolder}", "OK");
            return;
        }

        AssetDatabase.Refresh();

        // 1) Find all .obj recursively
        var objPaths = FindAll(RootModelsFolder, "*.obj");
        if (objPaths.Count == 0)
        {
            EditorUtility.DisplayDialog("Importer", "No .obj files found under Assets/Models.", "OK");
            return;
        }

        // 2) Group by <object_name> (top-level folder) and pick one OBJ per object_name
        var groups = GroupByTopFolder(objPaths, RootModelsFolder);
        var chosen = new List<(string objectName, string objPath)>();
        foreach (var kv in groups)
        {
            string objectName = kv.Key;
            var candidates = kv.Value;

            string best = candidates
                .OrderBy(p => !Path.GetFileNameWithoutExtension(p)
                    .Equals(objectName, StringComparison.OrdinalIgnoreCase))      // prefer filename match
                .ThenBy(p => DepthAfterFolder(p, RootModelsFolder, objectName))  // prefer shallower
                .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
                .First();

            chosen.Add((objectName, best));
        }

        // 3) Instantiate + assign a single material with the folder's PNG
        int count = chosen.Count;
        int cols  = Mathf.CeilToInt(Mathf.Sqrt(count));
        int imported = 0;

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();

        foreach (var (objectName, objPath) in chosen)
        {
            // Instantiate (or reuse)
            GameObject go = GameObject.Find(objectName);
            if (go != null)
            {
                if (OverwriteExistingInScene)
                    Undo.DestroyObjectImmediate(go);
                else
                {
                    Debug.Log($"[SimpleImport] Using existing scene object: {objectName}");
                }
            }

            if (go == null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(objPath);
                if (prefab == null)
                {
                    Debug.LogWarning($"[SimpleImport] Could not load GameObject from: {objPath}");
                    continue;
                }
                go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                go.name = objectName;

                if (LayoutOnGrid)
                {
                    int i = imported;
                    int r = i / cols;
                    int c = i % cols;
                    go.transform.position = new Vector3(c * GridSpacing, 0f, r * GridSpacing);

                    // Sit on ground
                    var rends = go.GetComponentsInChildren<Renderer>();
                    if (rends.Length > 0)
                    {
                        var b = rends[0].bounds;
                        foreach (var rrr in rends) b.Encapsulate(rrr.bounds);
                        float lift = go.transform.position.y - b.min.y;
                        go.transform.position += new Vector3(0, lift, 0);
                    }
                }
                Undo.RegisterCreatedObjectUndo(go, "Import Model");
                imported++;
                Debug.Log($"[SimpleImport] Instantiated '{objectName}' from {objPath}");
            }

            // Make/assign a single material with the folder's PNG
            string folder = Path.GetDirectoryName(objPath).Replace('\\', '/');
            string texPath = PickTexture(folder, objectName); // prefer <object_name>.png, else any .png/.jpg
            var mat = EnsureMaterialWithTexture(folder, objectName, texPath);

            int slotsAssigned = 0;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var slots = r.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    slots[i] = mat;
                    slotsAssigned++;
                }
                r.sharedMaterials = slots;
            }

            Debug.Log($"[SimpleImport] {objectName}: assigned {slotsAssigned} slot(s) " +
                      $"{(texPath != null ? $"with {Path.GetFileName(texPath)}" : "(no texture found)")}");
        }

        AssetDatabase.SaveAssets();
        Undo.CollapseUndoOperations(undoGroup);

        EditorUtility.DisplayDialog("Importer",
            $"Imported {imported} object(s). Materials assigned with PNGs where found. Check Console for details.",
            "OK");
    }

    // ---------- helpers ----------

    private static string PickTexture(string folderAssets, string objectName)
    {
        // Prefer a texture named like the object, else first PNG/JPG in the folder
        var allPng = FindAll(folderAssets, "*.png");
        var allJpg = FindAll(folderAssets, "*.jpg").Concat(FindAll(folderAssets, "*.jpeg")).ToList();
        var all = allPng.Concat(allJpg).ToList();

        var exact = all.FirstOrDefault(p =>
            string.Equals(Path.GetFileNameWithoutExtension(p), objectName, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(exact)) return exact;

        return all.FirstOrDefault(); // any
    }

    private static Material EnsureMaterialWithTexture(string folderAssets, string objectName, string texAssetsPath)
    {
        string matPath = $"{folderAssets}/{Sanitize(objectName)}_Auto.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(GetLitShader()) { name = $"{objectName}_Auto" };
            AssetDatabase.CreateAsset(mat, matPath);
        }

        if (!string.IsNullOrEmpty(texAssetsPath))
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texAssetsPath);
            if (tex)
            {
                if (mat.HasProperty("_MainTex"))  mat.SetTexture("_MainTex", tex);   // Built-in
                if (mat.HasProperty("_BaseMap"))  mat.SetTexture("_BaseMap", tex);   // URP/HDRP
            }
        }

        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static Shader GetLitShader()
    {
        var rp = GraphicsSettings.currentRenderPipeline;
        if (rp == null) return Shader.Find("Standard") ?? Shader.Find("Diffuse");
        var n = rp.GetType().Name;
        if (n.Contains("Universal")) return Shader.Find("Universal Render Pipeline/Lit");
        if (n.Contains("HD"))        return Shader.Find("HDRP/Lit");
        return Shader.Find("Standard");
    }

    private static List<string> FindAll(string startAssetsFolder, string pattern)
    {
        string abs = ToAbsolutePath(startAssetsFolder);
        if (string.IsNullOrEmpty(abs) || !Directory.Exists(abs)) return new List<string>();
        return Directory.EnumerateFiles(abs, pattern, SearchOption.AllDirectories)
                        .Select(ToAssetsRelative)
                        .Where(s => !string.IsNullOrEmpty(s))
                        .Select(s => s.Replace('\\', '/'))
                        .ToList();
    }

    private static Dictionary<string, List<string>> GroupByTopFolder(List<string> assetPaths, string root)
    {
        var dict = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in assetPaths)
        {
            var name = TopFolderAfterRoot(p, root);
            if (string.IsNullOrEmpty(name)) continue;
            if (!dict.TryGetValue(name, out var list)) { list = new List<string>(); dict[name] = list; }
            list.Add(p);
        }
        return dict;
    }

    private static string TopFolderAfterRoot(string assetPath, string root)
    {
        var p = assetPath.Replace('\\', '/');
        if (!p.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)) return null;
        int start = root.Length + 1;
        int slash = p.IndexOf('/', start);
        if (slash < 0) return null;
        return p.Substring(start, slash - start);
    }

    private static int DepthAfterFolder(string assetPath, string root, string objectName)
    {
        var p = assetPath.Replace('\\', '/');
        string prefix = $"{root}/{objectName}/";
        if (!p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return int.MaxValue;
        string after = p.Substring(prefix.Length);
        return after.Count(ch => ch == '/');
    }

    private static string ToAbsolutePath(string assetsPath)
    {
        if (string.IsNullOrEmpty(assetsPath)) return null;
        if (!assetsPath.StartsWith("Assets/") && !assetsPath.Equals("Assets", StringComparison.OrdinalIgnoreCase))
            return null;
        string projectAssets = Application.dataPath.Replace('\\', '/'); // .../Project/Assets
        return projectAssets.Substring(0, projectAssets.Length - "Assets".Length) + assetsPath;
    }

    private static string ToAssetsRelative(string absPath)
    {
        absPath = absPath.Replace('\\', '/');
        string assetsAbs = Application.dataPath.Replace('\\', '/');
        if (!absPath.StartsWith(assetsAbs, StringComparison.OrdinalIgnoreCase)) return null;
        return "Assets" + absPath.Substring(assetsAbs.Length);
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }
}
