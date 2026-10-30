using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Non-destructive authoring commands and native prefab/layout regression checks.</summary>
[InitializeOnLoad]
public static class CurtainGameplayUIEditor
{
    const string PrefabPath = "Assets/Resources/UI/CurtainGameplayUI.prefab";
    const string RequestPath = "Temp/CurtainGameplayUI.Validate";
    const string ReportPath = "Logs/CurtainGameplayUI-validation.txt";
    static readonly string[] Scenes = {
        "Assets/LevelWhitebox/Scenes/Level01_FirstFloor.unity",
        "Assets/Scenes/SampleScene.unity"
    };

    static CurtainGameplayUIEditor()
    {
        // Terminal checks opt in with an ignored local marker; normal editor sessions do no polling.
        if (File.Exists(RequestPath)) EditorApplication.update += ProcessRequestedValidation;
    }

    static void ProcessRequestedValidation()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        EditorApplication.update -= ProcessRequestedValidation;
        File.Delete(RequestPath);
        Directory.CreateDirectory("Logs");
        try
        {
            Validate();
            File.WriteAllText(ReportPath, "PASS: Unity imported all three canvases, native references and both scene instances; follow/beat updates preserve authored layout and styling.");
        }
        catch (Exception error)
        {
            File.WriteAllText(ReportPath, "FAIL: " + error);
            Debug.LogException(error);
        }
    }

    [MenuItem("Ghost Party/UI/Select Curtain Canvases")]
    public static void SelectCanvases()
    {
        var existing = FindInActiveScene();
        if (existing == null) { Debug.LogWarning("No CurtainGameplayUI in this scene. Use Ghost Party > UI > Add Curtain Canvases."); return; }
        Selection.activeGameObject = existing.gameObject;
        EditorGUIUtility.PingObject(existing.gameObject);
    }

    [MenuItem("Ghost Party/UI/Add Curtain Canvases")]
    public static void AddCanvases()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("Exit Play Mode before adding persistent scene UI."); return; }
        var existing = FindInActiveScene();
        if (existing != null) { Selection.activeGameObject = existing.gameObject; return; }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Require(prefab != null, "UI prefab is missing.");
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, SceneManager.GetActiveScene());
        Undo.RegisterCreatedObjectUndo(go, "Add curtain gameplay canvases");
        var curtain = GameObject.Find("Curtain");
        if (curtain != null) go.transform.position = curtain.transform.position;
        Selection.activeGameObject = go;
        EditorSceneManager.MarkSceneDirty(go.scene);
    }

    static CurtainGameplayUI FindInActiveScene()
    {
        return UnityEngine.Object.FindObjectsOfType<CurtainGameplayUI>(true)
            .FirstOrDefault(view => view.gameObject.scene == SceneManager.GetActiveScene());
    }

    [MenuItem("Ghost Party/UI/Validate Curtain Canvases")]
    public static void Validate()
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var view = root.GetComponent<CurtainGameplayUI>();
            Require(view != null && view.IsConfigured, "Prefab UI references are incomplete.");
            var canvases = root.GetComponentsInChildren<Canvas>(true);
            Require(canvases.Length == 3 && canvases.All(c => c.renderMode == RenderMode.WorldSpace), "Expected exactly three world-space canvases.");
            var images = root.GetComponentsInChildren<Image>(true);
            Require(images.Length == 4 && images.All(i => i.sprite != null && !i.raycastTarget), "Image sprite bindings are missing or intercept input.");
            Require(root.GetComponentsInChildren<Text>(true).All(t => t.font != null && !t.raycastTarget), "A label font is missing or intercepts input.");
            Require(view.EscapeFill.type == Image.Type.Filled && view.EscapeFill.fillMethod == Image.FillMethod.Radial360, "Escape progress must retain radial fill.");
            string prefabGuid = AssetDatabase.AssetPathToGUID(PrefabPath);
            foreach (string scenePath in Scenes)
            {
                Require(AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) != null, "Scene did not import: " + scenePath);
                string sourcePrefab = "m_SourcePrefab: {fileID: 100100000, guid: " + prefabGuid + ", type: 3}";
                Require(File.ReadAllLines(scenePath).Count(line => line.Trim() == sourcePrefab) == 1,
                    "Scene must contain one authored UI prefab instance: " + scenePath);
            }
            // The loaded prefab lives in Unity's isolated preview scene; no active scene is opened or saved.
            var camera = root.AddComponent<Camera>();
            camera.enabled = false;
            camera.transform.SetPositionAndRotation(new Vector3(5, 8, -10), Quaternion.Euler(30, 0, 0));
            // Simulate designer edits, then exercise follow and both ends of the beat pulse.
            var rects = canvases.Select(c => (RectTransform)c.transform).ToArray();
            for (int i = 0; i < rects.Length; i++)
            {
                rects[i].localPosition = new Vector3(9 + i, 14 - i, 0);
                rects[i].localRotation = Quaternion.Euler(0, 0, 6 + i);
                rects[i].localScale *= 1.3f;
                rects[i].sizeDelta += new Vector2(17, 13);
            }
            var positions = rects.Select(r => r.localPosition).ToArray();
            var rotations = rects.Select(r => r.localRotation).ToArray();
            var scales = rects.Select(r => r.localScale).ToArray();
            var sizes = rects.Select(r => r.sizeDelta).ToArray();
            var labels = root.GetComponentsInChildren<Text>(true);
            foreach (var label in labels) { label.fontSize = 57; label.color = Color.magenta; }
            foreach (var image in images) image.color = Color.cyan;
            view.BindCamera(camera);
            foreach (float pulse in new[] { 0f, 1f })
            {
                view.FollowCurtain(new Vector3(-18, 2, 2), camera, pulse, pulse);
                view.FollowGuest(new Vector3(-15, 0.62f, 2), camera);
                for (int i = 0; i < rects.Length; i++)
                    Require(rects[i].localPosition == positions[i] && Quaternion.Angle(rects[i].localRotation, rotations[i]) < 0.01f
                        && rects[i].localScale == scales[i] && rects[i].sizeDelta == sizes[i], "Gameplay overwrote a designer's Canvas transform.");
                Require(labels.All(t => t.fontSize == 57 && t.color == Color.magenta) && images.All(i => i.color == Color.cyan), "Gameplay overwrote authored styling.");
            }
            Debug.Log("CURTAIN_GAMEPLAY_UI_VALIDATION_OK: three canvases; both scenes; native sprites/fonts; authored layout preserved.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
