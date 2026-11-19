using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public sealed class GhostToonLookTool : EditorWindow
{
    const string ToonShaderName = "GhostParty/Toon/Surface";
    const string DecorationShaderName = "GhostParty/DecorationTransparent";
    const float DefaultOutlinePixels = 1.75f;
    float outlinePixels = DefaultOutlinePixels;

    [MenuItem("Ghost Party/Look/Make F9 Default In Active Scene")]
    static void MakeF9Default()
    {
        Scene scene = SceneManager.GetActiveScene();
        Shader toon = Shader.Find(ToonShaderName);
        if (!scene.IsValid() || toon == null)
        {
            Debug.LogError("GHOST_TOON: active scene or toon shader is missing.");
            return;
        }

        int converted = 0;
        foreach (Material material in SceneMaterials(scene))
        {
            string original = material.shader != null ? material.shader.name : "";
            if (original != "Standard" && original != DecorationShaderName && original != ToonShaderName) continue;
            if (original != ToonShaderName)
            {
                Undo.RecordObject(material, "Make F9 toon material default");
                material.shader = toon;
                material.SetFloat("_GhostSourceShader", original == "Standard" ? 1f : 0f);
                material.SetFloat("_OutlinePixels", DefaultOutlinePixels);
                converted++;
            }
            else Undo.RecordObject(material, "Set F9 toon look");
            material.SetFloat("_ToonLook", 2f);
            EditorUtility.SetDirty(material);
        }

        Transform wall = GhostWallCutout.ResolveWallRoot(scene);
        if (wall != null)
        {
            GhostWallCutout cutout = wall.GetComponent<GhostWallCutout>();
            if (cutout == null) cutout = Undo.AddComponent<GhostWallCutout>(wall.gameObject);
            Undo.RecordObject(cutout, "Set F9 as default look");
            cutout.Look = GhostWallCutout.ToonLook.Hybrid;
            EditorUtility.SetDirty(cutout);
        }

        foreach (GameObject root in scene.GetRootGameObjects())
        foreach (Light light in root.GetComponentsInChildren<Light>(true))
        {
            if (light.name != "Directional Light" || light.type != LightType.Directional) continue;
            Undo.RecordObject(light.transform, "Set F9 preview light direction");
            Undo.RecordObject(light, "Set F9 preview light shadows");
            light.transform.rotation = Quaternion.Euler(50f, -25f, 0f);
            light.shadows = LightShadows.Soft;
        }

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("GHOST_TOON: F9 is now the edit and Play default for " + scene.name +
                  "; converted " + converted + " shared materials. Adjust width under Ghost Party > Look > Outline Width.");
    }

    [MenuItem("Ghost Party/Look/Outline Width")]
    static void OpenOutlineWidth()
    {
        GetWindow<GhostToonLookTool>("Toon Outline");
    }

    void OnEnable()
    {
        foreach (Material material in SceneMaterials(SceneManager.GetActiveScene()))
        {
            if (material.shader == null || material.shader.name != ToonShaderName) continue;
            outlinePixels = material.GetFloat("_OutlinePixels");
            break;
        }
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("F9 outline in the active scene", EditorStyles.boldLabel);
        outlinePixels = EditorGUILayout.Slider("Width (screen pixels)", outlinePixels, 0f, 4f);
        if (GUILayout.Button("Apply To Scene Materials"))
        {
            int updated = 0;
            foreach (Material material in SceneMaterials(SceneManager.GetActiveScene()))
            {
                if (material.shader == null || material.shader.name != ToonShaderName) continue;
                Undo.RecordObject(material, "Adjust toon outline width");
                material.SetFloat("_OutlinePixels", outlinePixels);
                EditorUtility.SetDirty(material);
                updated++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log("GHOST_TOON: outline width " + outlinePixels.ToString("0.00") +
                      " px applied to " + updated + " scene materials.");
        }
        EditorGUILayout.HelpBox("For one object only, select its Material and change Ink Outline > Outer Width (screen pixels).", MessageType.Info);
    }

    static IEnumerable<Material> SceneMaterials(Scene scene)
    {
        var seen = new HashSet<Material>();
        if (!scene.IsValid()) yield break;
        foreach (GameObject root in scene.GetRootGameObjects())
        foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        foreach (Material material in renderer.sharedMaterials)
        {
            if (material == null || !seen.Add(material)) continue;
            string path = AssetDatabase.GetAssetPath(material);
            if (!path.EndsWith(".mat", System.StringComparison.OrdinalIgnoreCase)) continue;
            yield return material;
        }
    }
}
