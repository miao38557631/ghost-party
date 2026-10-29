using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>Imports the exported PNG sequence and builds the playable NPC idle assets.</summary>
[InitializeOnLoad]
public static class NpcIdleAnimationSetup
{
    const string SourceFolder = "Assets/2DTexture/NPC1/idle";
    const string OutputFolder = "Assets/Resources/NPC1";
    const string ClipPath = OutputFolder + "/NPC1_Idle.anim";
    const string ControllerPath = OutputFolder + "/NPC1_Idle.controller";
    const int FrameCount = 24;
    const float FramesPerSecond = 12f;
    const float PixelsPerUnit = 750f;

    static NpcIdleAnimationSetup()
    {
        // Unity compiles this script after the user adds it. Build once, then
        // leave the generated assets alone until explicitly rebuilt from the menu.
        EditorApplication.delayCall += BuildIfMissing;
    }

    static void BuildIfMissing()
    {
        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath) != null &&
            AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
            return;
        Build();
    }

    [MenuItem("Ghost Party/Build NPC1 Idle")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(OutputFolder))
            AssetDatabase.CreateFolder("Assets/Resources", "NPC1");

        var sprites = new Sprite[FrameCount];
        for (int i = 0; i < FrameCount; i++)
        {
            string path = SourceFolder + "/npc1_Idle_" + i.ToString("00") + ".png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogError("NPC1 idle image missing: " + path);
                return;
            }
            if (importer.textureType != TextureImporterType.Sprite ||
                importer.spriteImportMode != SpriteImportMode.Single ||
                Math.Abs(importer.spritePixelsPerUnit - PixelsPerUnit) > 0.01f ||
                importer.mipmapEnabled || !importer.alphaIsTransparency)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = PixelsPerUnit;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }
            sprites[i] = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprites[i] == null)
            {
                Debug.LogError("NPC1 idle sprite failed to import: " + path);
                return;
            }
        }

        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        if (clip == null)
        {
            clip = new AnimationClip { name = "NPC1_Idle" };
            AssetDatabase.CreateAsset(clip, ClipPath);
        }
        clip.frameRate = FramesPerSecond;
        var keys = new ObjectReferenceKeyframe[FrameCount + 1];
        for (int i = 0; i < FrameCount; i++)
            keys[i] = new ObjectReferenceKeyframe { time = i / FramesPerSecond, value = sprites[i] };
        // First pose at the end makes the loop exactly two seconds long.
        keys[FrameCount] = new ObjectReferenceKeyframe { time = FrameCount / FramesPerSecond, value = sprites[0] };
        AnimationUtility.SetObjectReferenceCurve(clip,
            EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite"), keys);
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        settings.stopTime = FrameCount / FramesPerSecond;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var machine = controller.layers[0].stateMachine;
        var state = Array.Find(machine.states, child => child.state.name == "Idle").state;
        if (state == null) state = machine.AddState("Idle");
        state.motion = clip;
        machine.defaultState = state;
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log("NPC1 idle ready: 24 sprites at 12 FPS, two-second looping clip and controller.");
    }
}
