using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>Builds native Unity assets without changing scenes or replacing existing asset GUIDs.</summary>
public static class GhostPlayerArtBuilder
{
    public const string Output = "Assets/Resources/Player";
    public const string PrefabPath = Output + "/GhostVisual.prefab";
    public const string ControllerPath = Output + "/GhostLocomotion.controller";
    public const float PixelsPerUnit = 512f;
    public const float FramesPerSecond = 24f; // Temporary until art supplies an authored playback rate.
    const string Report = "Temp/GhostPlayerArt-build-result.json";

    [Serializable]
    class BuildResult
    {
        public bool success;
        public string message;
        public int idleFrames, walkFrames;
        public float fps, pixelsPerUnit;
    }

    public static void ScheduleBuild()
    {
        // Return to the editor bridge before the potentially long texture import.
        EditorApplication.CallbackFunction once = null;
        once = delegate
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            EditorApplication.update -= once;
            var result = new BuildResult { fps = FramesPerSecond, pixelsPerUnit = PixelsPerUnit };
            try
            {
                Build();
                result.success = true;
                result.idleFrames = 30;
                result.walkFrames = 30;
                result.message = "Native sprites, clips, controller and visual prefab built and validated.";
            }
            catch (Exception error) { result.message = error.ToString(); Debug.LogException(error); }
            Directory.CreateDirectory("Temp");
            File.WriteAllText(Report, JsonUtility.ToJson(result, true));
        };
        EditorApplication.update += once;
    }

    [MenuItem("Ghost Party/Player/Build or Update Ghost Art")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play before building ghost artwork. Scenes will not be saved.");
        string[] idlePaths = Frames("Idle");
        string[] walkPaths = Frames("Walk");
        EnsureFolder("Assets/Resources", "Player");
        // Each group has a fixed pivot at its alpha union's bottom-centre. Individual frames are
        // never cropped/recentred/scaled separately, preserving intended floating/leaning motion.
        AssetDatabase.StartAssetEditing();
        try
        {
            ImportFrames(idlePaths, new Vector2(517f / 1223f, 145f / 1244f));
            ImportFrames(walkPaths, new Vector2(535.5f / 1321f, 172f / 1299f));
        }
        finally { AssetDatabase.StopAssetEditing(); }
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var idle = BuildClip("Ghost_Idle", idlePaths);
        var walk = BuildClip("Ghost_Walk", walkPaths);
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        if (!controller.parameters.Any(p => p.name == "IsMoving"))
            controller.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);
        if (controller.layers.Length == 0) controller.AddLayer("Base Layer");
        var machine = controller.layers[0].stateMachine;
        var idleState = FindOrAddState(machine, "Idle", new Vector3(220, 100));
        var walkState = FindOrAddState(machine, "Walk", new Vector3(460, 100));
        idleState.motion = idle; walkState.motion = walk;
        idleState.writeDefaultValues = false; walkState.writeDefaultValues = false;
        machine.defaultState = idleState;
        ConfigureTransition(idleState, walkState, AnimatorConditionMode.If);
        ConfigureTransition(walkState, idleState, AnimatorConditionMode.IfNot);
        EditorUtility.SetDirty(controller);

        string materialPath = Output + "/GhostSprite.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            var shader = Shader.Find("Sprites/Default");
            Require(shader != null, "Built-in Sprites/Default shader is missing.");
            material = new Material(shader) { name = "GhostSprite" };
            AssetDatabase.CreateAsset(material, materialPath);
        }
        material.color = Color.white;
        EditorUtility.SetDirty(material);
        bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
        var visual = exists ? PrefabUtility.LoadPrefabContents(PrefabPath) : new GameObject("Visual");
        try
        {
            visual.name = "Visual";
            var renderer = visual.GetComponent<SpriteRenderer>();
            if (renderer == null) renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(idlePaths[0]);
            renderer.sharedMaterial = material;
            renderer.color = Color.white;
            renderer.flipX = renderer.flipY = false; // Authored view faces right; runtime GhostPlayerVisual owns horizontal mirroring.
            renderer.sortingOrder = 0;
            renderer.spriteSortPoint = SpriteSortPoint.Pivot;
            var animator = visual.GetComponent<Animator>();
            if (animator == null) animator = visual.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (visual.GetComponent<GhostPlayerVisual>() == null) visual.AddComponent<GhostPlayerVisual>();
            PrefabUtility.SaveAsPrefabAsset(visual, PrefabPath);
        }
        finally
        {
            if (exists) PrefabUtility.UnloadPrefabContents(visual);
            else UnityEngine.Object.DestroyImmediate(visual);
        }
        AssetDatabase.SaveAssets();
        Validate();
        Debug.Log("GHOST_PLAYER_ART_BUILD_OK: Idle 30, Walk 30; 24 FPS; 512 PPU; existing player root retained.");
    }

    static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
    }

    static string[] Frames(string group)
    {
        string folder = "Assets/2DTexture/Ghost/" + group;
        Require(Directory.Exists(folder), "Missing artwork directory: " + folder);
        var paths = Directory.GetFiles(folder, "*.png")
            .OrderBy(p => FrameNumber(p)).Select(p => p.Replace('\\', '/')).ToArray();
        Require(paths.Length == 30, "Expected 30 independent PNGs in " + folder);
        for (int i = 0; i < paths.Length; i++) Require(FrameNumber(paths[i]) == i, "Missing/duplicate frame number in " + folder);
        return paths;
    }

    static int FrameNumber(string path)
    {
        var match = Regex.Match(Path.GetFileNameWithoutExtension(path), @"(\d+)$");
        Require(match.Success, "Frame needs a numeric suffix: " + path);
        return int.Parse(match.Value);
    }

    static void ImportFrames(string[] paths, Vector2 pivot)
    {
        foreach (string path in paths)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Require(importer != null, "Missing TextureImporter: " + path);
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            bool changed = importer.textureType != TextureImporterType.Sprite
                || importer.spriteImportMode != SpriteImportMode.Single
                || !Mathf.Approximately(importer.spritePixelsPerUnit, PixelsPerUnit)
                || (importer.spritePivot - pivot).sqrMagnitude > 0.00000001f
                || settings.spriteAlignment != (int)SpriteAlignment.Custom
                || settings.spriteMeshType != SpriteMeshType.FullRect
                || importer.mipmapEnabled || !importer.alphaIsTransparency
                || importer.textureCompression != TextureImporterCompression.Uncompressed;
            if (!changed) continue;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = pivot;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }
    }

    static AnimationClip BuildClip(string name, string[] paths)
    {
        string path = Output + "/" + name + ".anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) { clip = new AnimationClip { name = name }; AssetDatabase.CreateAsset(clip, path); }
        clip.frameRate = FramesPerSecond;
        var keys = new ObjectReferenceKeyframe[paths.Length + 1];
        for (int i = 0; i < paths.Length; i++)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(paths[i]);
            Require(sprite != null, "Single sprite failed to import: " + paths[i]);
            keys[i] = new ObjectReferenceKeyframe { time = i / FramesPerSecond, value = sprite };
        }
        keys[paths.Length] = new ObjectReferenceKeyframe { time = paths.Length / FramesPerSecond, value = keys[0].value };
        var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    static AnimatorState FindOrAddState(AnimatorStateMachine machine, string name, Vector3 position)
    {
        foreach (var child in machine.states) if (child.state.name == name) return child.state;
        return machine.AddState(name, position);
    }

    static void ConfigureTransition(AnimatorState from, AnimatorState to, AnimatorConditionMode mode)
    {
        var transition = from.transitions.FirstOrDefault(t => t.destinationState == to);
        if (transition == null) transition = from.AddTransition(to);
        foreach (var condition in transition.conditions) transition.RemoveCondition(condition);
        transition.hasExitTime = false;
        transition.hasFixedDuration = true;
        transition.duration = 0f;
        transition.canTransitionToSelf = false;
        transition.AddCondition(mode, 0f, "IsMoving");
    }

    [MenuItem("Ghost Party/Player/Validate Ghost Art")]
    public static void Validate()
    {
        foreach (string group in new[] { "Idle", "Walk" })
        {
            var paths = Frames(group);
            foreach (string path in paths)
            {
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Require(importer != null && importer.textureType == TextureImporterType.Sprite
                    && importer.spriteImportMode == SpriteImportMode.Single
                    && Mathf.Approximately(importer.spritePixelsPerUnit, PixelsPerUnit)
                    && importer.alphaIsTransparency && !importer.mipmapEnabled,
                    "Inconsistent sprite import: " + path);
            }
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Output + "/Ghost_" + group + ".anim");
            Require(clip != null && clip.isLooping, "Looping clip missing: " + group);
            var bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            Require(bindings.Length == 1 && bindings[0].path == "" && bindings[0].type == typeof(SpriteRenderer)
                && bindings[0].propertyName == "m_Sprite", "Animation must drive only the Visual sprite.");
            Require(AnimationUtility.GetCurveBindings(clip).Length == 0, "Animation must not drive transforms.");
            var keys = AnimationUtility.GetObjectReferenceCurve(clip, bindings[0]);
            Require(keys.Length == 31 && keys.All(k => k.value != null), "Missing sprite frames.");
            for (int i = 0; i < paths.Length; i++)
                Require(keys[i].value == AssetDatabase.LoadAssetAtPath<Sprite>(paths[i]), "Frame order mismatch.");
        }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Require(prefab != null && prefab.GetComponent<GhostPlayerVisual>() != null,
            "Reusable Visual prefab is missing.");
        var renderer = prefab.GetComponent<SpriteRenderer>();
        Require(renderer != null && renderer.sprite != null && renderer.sharedMaterial != null
            && renderer.sharedMaterial.shader != null && renderer.sharedMaterial.shader.isSupported,
            "Invalid ghost renderer/material.");
        var animator = prefab.GetComponent<Animator>();
        Require(animator != null && animator.runtimeAnimatorController != null && !animator.applyRootMotion,
            "Animator is missing or tries to drive the player root.");
        Debug.Log("GHOST_PLAYER_ART_VALIDATION_OK");
    }

    static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
