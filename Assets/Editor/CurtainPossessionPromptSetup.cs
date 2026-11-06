using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using GhostParty.LevelWhitebox;

public static class CurtainPossessionPromptSetup
{
    const string ScenePath = "Assets/LevelWhitebox/Scenes/Level01_FirstFloor.unity";
    [MenuItem("Ghost Party/UI/Install Curtain Possession Bubble")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play before installing the scene UI.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath) throw new InvalidOperationException("Open Level01_FirstFloor before installing.");
        if (scene.isDirty) throw new InvalidOperationException("Save existing scene edits before installing the UI.");
        var floor = UnityEngine.Object.FindObjectOfType<WhiteboxPlayable>();
        var serializedFloor = new SerializedObject(floor);
        var encounter = serializedFloor.FindProperty("curtainPrank").objectReferenceValue as LevelCurtainEncounter;
        var spot = serializedFloor.FindProperty("curtainSpot").objectReferenceValue as Transform;
        if (encounter == null || encounter.transform != spot || spot.name != "Curtain")
            throw new InvalidOperationException("The actual floor interaction target is not the expected Curtain.");
        Sprite keyboard = Import("Possess_E");
        Sprite gamepad = Import("Possess_A");
        Transform anchor = spot.Find("PromptAnchor");
        if (anchor == null)
        {
            var go = new GameObject("PromptAnchor");
            Undo.RegisterCreatedObjectUndo(go, "Create possession anchor");
            anchor = go.transform;
            anchor.SetParent(spot, false);
            // The authored block is rotated almost 90 degrees: -local X and
            // +local Z is its screen-right edge in the exploration camera.
            anchor.localPosition = new Vector3(-0.5f, 0.22f, 0.5f);
        }
        var prompt = spot.GetComponent<CurtainPossessionPrompt>();
        if (prompt == null) prompt = Undo.AddComponent<CurtainPossessionPrompt>(spot.gameObject);
        var serializedPrompt = new SerializedObject(prompt);
        serializedPrompt.FindProperty("promptAnchor").objectReferenceValue = anchor;
        serializedPrompt.FindProperty("keyboardSprite").objectReferenceValue = keyboard;
        serializedPrompt.FindProperty("gamepadSprite").objectReferenceValue = gamepad;
        serializedPrompt.FindProperty("displaySize").vector2Value = new Vector2(216f, 64f);
        serializedPrompt.FindProperty("tipPivot").vector2Value = new Vector2(0.019f, 0.5f);
        serializedPrompt.ApplyModifiedProperties();
        foreach (var text in UnityEngine.Object.FindObjectsOfType<TextMesh>(true))
            if (text.gameObject.scene == scene && text.text.Trim() == "Press E")
                Undo.DestroyObjectImmediate(text.gameObject);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Failed to save the scene.");
        AssetDatabase.SaveAssets();
        Debug.Log("POSSESSION_UI_INSTALLED: Curtain/PromptAnchor; original Canvas; sprites and scene saved.");
    }

    static Sprite Import(string name)
    {
        string path = "Assets/Resources/UI/" + name + ".png";
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 2048;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null) throw new InvalidOperationException("Sprite import failed: " + path);
        return sprite;
    }
}
