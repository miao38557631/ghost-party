using System;
using UnityEditor;
using UnityEngine;

/// <summary>Explicit, repeatable import/binding for the artist's separated artwork.</summary>
public static class CurtainDesignerUIEditor
{
    const string Folder = "Assets/Resources/UI/CurtainInteraction/";
    const string Prefab = "Assets/Resources/UI/CurtainGameplayUI.prefab";
    static readonly string[] Assets = { "GhostWhite", "GhostBlack", "ArrowWhite", "ArrowBlack", "RingOuter", "RingInner",
        "MouseIdle", "MousePressed", "TapChinese", "HoldChinese", "TapEnglish", "HoldEnglish" };
    static readonly string[] Fields = { "ghostWhite", "ghostBlack", "arrowWhite", "arrowBlack", "ringOuter", "ringInner",
        "mouseIdle", "mousePressed", "tapChinese", "holdChinese", "tapEnglish", "holdEnglish" };

    [MenuItem("Ghost Party/UI/Bind Designer Interaction Artwork")]
    public static void Bind()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        foreach (string name in Assets)
        {
            string path = Folder + name + ".png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Missing artwork: " + path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.sRGBTexture = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }
        var root = PrefabUtility.LoadPrefabContents(Prefab);
        try
        {
            var view = root.GetComponent<CurtainGameplayUI>();
            var serialized = new SerializedObject(view);
            for (int i = 0; i < Fields.Length; i++)
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Folder + Assets[i] + ".png");
                if (sprite == null) throw new InvalidOperationException("Sprite import failed: " + Assets[i]);
                serialized.FindProperty(Fields[i]).objectReferenceValue = sprite;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            if (!view.HasDesignerArtwork) throw new InvalidOperationException("Designer bindings incomplete.");
            PrefabUtility.SaveAsPrefabAsset(root, Prefab);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
        Debug.Log("CURTAIN_DESIGNER_UI_BOUND: 12 transparent Single/Full Rect sprites; existing scene prefab instances inherit the artwork.");
    }
}
