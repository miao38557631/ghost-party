using UnityEditor;
using UnityEngine;

public static class SofaShadeMaskPrototypeSetup
{
    const string MaskPath = "Assets/2DTexture/DecorationProps/ShadeMasks/L_sofa_grayscale_mask_v1.png";
    const string MaterialPath = "Assets/2DTexture/DecorationProps/Materials/L sofa_336d784b.mat";

    [MenuItem("Ghost Party/Look/Apply Sofa Shade Mask Prototype")]
    public static void Apply()
    {
        var importer = AssetImporter.GetAtPath(MaskPath) as TextureImporter;
        if (importer == null) { Debug.LogError("SOFA_MASK: texture importer missing."); return; }
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = false;
        importer.alphaSource = TextureImporterAlphaSource.None;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.SaveAndReimport();

        Texture2D mask = AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath);
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mask == null || material == null || !material.HasProperty("_ShadeMask"))
        {
            Debug.LogError("SOFA_MASK: mask or F9 sofa material is missing.");
            return;
        }
        Undo.RecordObject(material, "Apply sofa grayscale shade mask");
        material.SetTexture("_ShadeMask", mask);
        material.SetFloat("_ShadeMaskStrength", 1f);
        material.SetFloat("_ShadeThreshold", 0.58f);
        material.SetFloat("_ShadeMinBrightness", 0.72f);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        Debug.Log("SOFA_MASK: two-tone mask applied to L sofa; adjust Shadow Boundary and Shadow Brightness in its material.");
    }
}
