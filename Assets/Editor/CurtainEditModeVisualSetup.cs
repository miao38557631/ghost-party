using Spine.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class CurtainEditModeVisualSetup
{
    const string PrefabPath = "Assets/Curtain Drag IK Preview.prefab";
    const string DataPath = "Assets/2DTexture/Curtain/Curtain_SkeletonData.asset";
    const string MaterialPath = "Assets/2DTexture/Curtain/Curtain_Material.mat";

    [MenuItem("Ghost Party/Curtain/Show Interactive Curtain In Editor")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("GHOST_CURTAIN: exit Play Mode before saving its editor visual.");
            return;
        }
        SkeletonDataAsset data = AssetDatabase.LoadAssetAtPath<SkeletonDataAsset>(DataPath);
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        Shader shader = Shader.Find("GhostParty/Toon/SpineCurtain");
        if (data == null || material == null || shader == null)
        {
            Debug.LogError("GHOST_CURTAIN: skeleton data, atlas material, or toon shader is missing.");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            SpineCurtainDragIK solver = root.GetComponent<SpineCurtainDragIK>();
            if (solver == null) throw new System.InvalidOperationException("Curtain IK prefab has no solver.");
            SkeletonAnimation animation = root.GetComponentInChildren<SkeletonAnimation>(true);
            if (animation == null)
            {
                GameObject child = new GameObject("Curtain Spine (drag IK)");
                child.transform.SetParent(root.transform, false);
                animation = SkeletonAnimation.AddToGameObject(child, data).skeletonAnimation;
            }
            animation.transform.localPosition = Vector3.zero;
            animation.transform.localRotation = Quaternion.identity;
            animation.transform.localScale = Vector3.one;
            MeshRenderer renderer = animation.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
            renderer.receiveShadows = true;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { Object.DestroyImmediate(root); }

        Undo.RecordObject(material, "Light the interactive Spine curtain");
        material.shader = shader;
        material.SetFloat("_ToonLook", 2f);
        material.SetFloat("_ToonStrength", 1f);
        material.SetFloat("_StraightAlphaInput", 1f);
        material.EnableKeyword("_STRAIGHT_ALPHA_INPUT");
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();

        Scene scene = SceneManager.GetActiveScene();
        if (scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        Debug.Log("GHOST_CURTAIN: saved the interactive Spine curtain as an editor-visible prefab child with F9 lighting.");
    }
}
