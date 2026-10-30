using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>Turns selected transparent PNGs into world-space decoration prefabs.</summary>
public static class DecorationPrefabBatchBuilder
{
    const string MenuPath = "Tools/Ghost Party/Build Decoration Prefabs From Selected PNGs";
    const string AddBillboardMenuPath = "Tools/Ghost Party/Add Camera Facing To Selected Prefabs";
    const string ShaderPath = "Assets/2DTexture/DecorationProps/DecorationTransparent.shader";
    const string OutputRoot = "Assets/2DTexture/DecorationProps";
    const float ColliderThickness = 0.08f;

    [MenuItem(MenuPath, true)]
    static bool CanBuild() { return GetSelectedPngPaths().Count > 0; }

    [MenuItem(AddBillboardMenuPath, true)]
    static bool CanAddBillboard() { return GetSelectedPrefabPaths().Count > 0; }

    [MenuItem(AddBillboardMenuPath)]
    static void AddBillboardToSelectedPrefabs()
    {
        var paths = GetSelectedPrefabPaths();
        int added = 0;
        int alreadyPresent = 0;
        int failed = 0;
        foreach (string path in paths)
        {
            GameObject root = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(path);
                if (root == null) throw new InvalidOperationException("Could not open prefab");
                if (root.GetComponent<FaceCameraBillboard>() != null)
                {
                    alreadyPresent++;
                    continue;
                }
                root.AddComponent<FaceCameraBillboard>();
                if (PrefabUtility.SaveAsPrefabAsset(root, path) == null)
                    throw new InvalidOperationException("Could not save prefab");
                added++;
            }
            catch (Exception ex)
            {
                failed++;
                Debug.LogError("Could not add camera facing to " + path + "\n" + ex);
            }
            finally
            {
                if (root != null) PrefabUtility.UnloadPrefabContents(root);
            }
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"Camera facing: added to {added} prefabs; {alreadyPresent} already had it; {failed} failed.");
    }

    [MenuItem(MenuPath)]
    static void Build()
    {
        var paths = GetSelectedPngPaths();
        if (paths.Count == 0) return;

        var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (shader == null || !shader.isSupported)
        {
            Debug.LogError("Decoration shader is missing or unsupported: " + ShaderPath);
            return;
        }

        EnsureFolder("Assets/2DTexture", "DecorationProps");
        EnsureFolder(OutputRoot, "Materials");
        EnsureFolder(OutputRoot, "Prefabs");

        var generated = new List<Object>();
        var failed = new List<string>();
        try
        {
            for (int i = 0; i < paths.Count; i++)
            {
                string path = paths[i];
                EditorUtility.DisplayProgressBar("Building decoration prefabs", path, (float)i / paths.Count);
                try { generated.Add(BuildOne(path, shader)); }
                catch (Exception ex)
                {
                    failed.Add(path);
                    Debug.LogError("Could not build decoration from " + path + "\n" + ex);
                }
            }
        }
        finally { EditorUtility.ClearProgressBar(); }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"Decoration prefabs: {generated.Count} built in {OutputRoot}/Prefabs, {failed.Count} failed.");
        if (failed.Count > 0)
            EditorUtility.DisplayDialog("Decoration prefabs", $"Built {generated.Count}; failed {failed.Count}. See Console for paths.", "OK");
    }

    static List<string> GetSelectedPngPaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var selected in Selection.objects)
        {
            string path = AssetDatabase.GetAssetPath(selected);
            if (string.IsNullOrEmpty(path)) continue;
            if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                paths.Add(path);
                continue;
            }
            if (!AssetDatabase.IsValidFolder(path)) continue;
            foreach (string guid in AssetDatabase.FindAssets("", new[] { path }))
            {
                string imagePath = AssetDatabase.GUIDToAssetPath(guid);
                if (imagePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) paths.Add(imagePath);
            }
        }
        var result = new List<string>(paths);
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    static List<string> GetSelectedPrefabPaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var selected in Selection.objects)
        {
            string path = AssetDatabase.GetAssetPath(selected);
            if (!string.IsNullOrEmpty(path) && path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                paths.Add(path);
        }
        var result = new List<string>(paths);
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    static Object BuildOne(string sourcePath, Shader shader)
    {
        var importer = AssetImporter.GetAtPath(sourcePath) as TextureImporter;
        if (importer == null) throw new InvalidOperationException("Not an imported texture");

        bool reimport = false;
        if (importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            reimport = true;
        }
        if (importer.spriteImportMode != SpriteImportMode.Single)
        {
            importer.spriteImportMode = SpriteImportMode.Single;
            reimport = true;
        }
        if (importer.alphaSource != TextureImporterAlphaSource.FromInput)
        {
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            reimport = true;
        }
        if (!importer.alphaIsTransparency)
        {
            importer.alphaIsTransparency = true;
            reimport = true;
        }
        if (reimport) importer.SaveAndReimport();

        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(sourcePath);
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
        if (texture == null && sprite != null) texture = sprite.texture;
        if (sprite == null || texture == null) throw new InvalidOperationException("Sprite import failed");

        float aspect = sprite.rect.width / sprite.rect.height;
        string stem = Path.GetFileNameWithoutExtension(sourcePath);
        string id = AssetDatabase.AssetPathToGUID(sourcePath).Substring(0, 8);
        string assetName = stem + "_" + id;
        string materialPath = OutputRoot + "/Materials/" + assetName + ".mat";
        string prefabPath = OutputRoot + "/Prefabs/" + assetName + ".prefab";
        var existingBillboard = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath)?.GetComponent<FaceCameraBillboard>();
        bool faceCamera = existingBillboard == null || existingBillboard.faceCamera;

        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, materialPath);
        }
        material.shader = shader;
        material.mainTexture = texture;
        material.color = Color.white;
        material.renderQueue = -1;
        EditorUtility.SetDirty(material);

        GameObject root = null;
        try
        {
            root = new GameObject(stem);
            var visual = GameObject.CreatePrimitive(PrimitiveType.Quad);
            visual.name = "Visual";
            visual.transform.SetParent(root.transform, false);
            visual.transform.localScale = new Vector3(aspect, 1f, 1f);
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            var renderer = visual.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var collider = root.AddComponent<BoxCollider>();
            collider.center = Vector3.zero;
            collider.size = new Vector3(aspect, 1f, ColliderThickness);
            collider.isTrigger = false;
            root.AddComponent<FaceCameraBillboard>().faceCamera = faceCamera;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            if (prefab == null) throw new InvalidOperationException("Prefab save failed");
            return prefab;
        }
        finally
        {
            if (root != null) Object.DestroyImmediate(root);
        }
    }

    static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name))
            AssetDatabase.CreateFolder(parent, name);
    }
}
