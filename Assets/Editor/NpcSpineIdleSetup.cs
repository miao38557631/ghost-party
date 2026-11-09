using Spine.Unity;
using UnityEditor;
using UnityEngine;

/// <summary>Builds a Resources prefab that keeps a live reference to the imported Spine data.</summary>
[InitializeOnLoad]
public static class NpcSpineIdleSetup
{
    const string DataPath = "Assets/2DTexture/NPC1/Idle/skeleton_SkeletonData.asset";
    const string PrefabPath = "Assets/Resources/NPC1/NPC1_SpineIdle.prefab";

    static NpcSpineIdleSetup()
    {
        EditorApplication.delayCall += BuildIfMissing;
    }

    static void BuildIfMissing()
    {
        ConfigureMaterial();
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
            Build();
    }

    static void ConfigureMaterial()
    {
        const string materialPath = "Assets/2DTexture/NPC1/Idle/skeleton_Material.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null || !material.HasProperty("_StraightAlphaInput") ||
            (material.GetFloat("_StraightAlphaInput") > 0.5f && material.IsKeywordEnabled("_STRAIGHT_ALPHA_INPUT")))
            return;
        material.SetFloat("_StraightAlphaInput", 1f);
        material.EnableKeyword("_STRAIGHT_ALPHA_INPUT");
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Ghost Party/Build NPC1 Spine Idle Prefab")]
    public static void Build()
    {
        ConfigureMaterial();
        var data = AssetDatabase.LoadAssetAtPath<SkeletonDataAsset>(DataPath);
        if (data == null || data.GetSkeletonData(true) == null)
        {
            Debug.LogError("NPC1 Spine data is missing or invalid: " + DataPath);
            return;
        }
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder("Assets/Resources/NPC1"))
            AssetDatabase.CreateFolder("Assets/Resources", "NPC1");

        var go = new GameObject("NPC1_SpineIdle");
        try
        {
            var components = SkeletonAnimation.AddToGameObject(go, data);
            components.skeletonAnimation.loop = true;
            components.skeletonAnimation.AnimationName = "idle";
            go.GetComponent<MeshRenderer>().sortingOrder = 2;
            if (PrefabUtility.SaveAsPrefabAsset(go, PrefabPath) == null)
                Debug.LogError("Could not save NPC1 Spine idle prefab: " + PrefabPath);
            else
                Debug.Log("NPC1 Spine idle prefab ready: " + PrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }
}
