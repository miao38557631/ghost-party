using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Exercise production target selection and UI with virtual devices in the real floor scene.</summary>
[InitializeOnLoad]
public static class PossessionPromptPlayChecks
{
    const string Key = "GhostPossessionPrompt.Checks";
    const string ReportPath = "Logs/InputSystem/possess-ui-checks.json";
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    [Serializable] public class Report
    {
        public bool complete;
        public List<string> passed = new List<string>();
        public List<string> failed = new List<string>();
        public List<string> runtimeErrors = new List<string>();
        public List<string> screenshots = new List<string>();
    }
    static Report report;
    static readonly Stack<IEnumerator> routines = new Stack<IEnumerator>();
    static readonly List<InputDevice> restoreDevices = new List<InputDevice>();
    static Keyboard keyboard;
    static Mouse mouse;
    static Gamepad pad;
    static MonoBehaviour input, floor, prompt, prank, encounter;
    static Transform player;
    static Image image;
    static InputSettings.BackgroundBehavior background;
    static bool savedBackground, finished;
    static double deadline;
    static int lastFrame;

    static PossessionPromptPlayChecks() { EditorApplication.playModeStateChanged += StateChanged; }

    [MenuItem("Ghost Party/UI/Run Possession Prompt Play Checks")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Save scene edits before checking.");
        Directory.CreateDirectory("Logs/InputSystem");
        report = new Report(); Save();
        SessionState.SetBool(Key, true);
        SessionState.SetString(Key + ".start", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetString(Key + ".scene", SceneManager.GetActiveScene().path);
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/LevelWhitebox/Scenes/Level01_FirstFloor.unity");
        EditorApplication.EnterPlaymode();
    }
    static void StateChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            report = JsonUtility.FromJson<Report>(File.ReadAllText(ReportPath));
            finished = false; lastFrame = -1; deadline = EditorApplication.timeSinceStartup + 60;
            routines.Clear(); routines.Push(Checks());
            Application.logMessageReceived += ObserveError;
            EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            if (!finished && report != null) { report.failed.Add("Checks interrupted"); Save(); }
            Cleanup();
        }
        if (state != PlayModeStateChange.EnteredEditMode) return;
        report = JsonUtility.FromJson<Report>(File.ReadAllText(ReportPath));
        string old = SessionState.GetString(Key + ".start", "");
        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(old) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(old);
        bool restored = SceneManager.GetActiveScene().path == SessionState.GetString(Key + ".scene", "") && !SceneManager.GetActiveScene().isDirty;
        (restored ? report.passed : report.failed).Add("Original editor scene restored without saving Play changes");
        report.complete = true; Save(); SessionState.SetBool(Key, false);
        Debug.Log("POSSESSION_UI_CHECKS_COMPLETE: " + report.passed.Count + " passed; " + report.failed.Count + " failed; " + report.runtimeErrors.Count + " runtime errors.");
    }
    static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isPaused || lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Possession prompt checks timed out");
            while (routines.Count > 0)
            {
                var routine = routines.Peek();
                if (!routine.MoveNext()) { routines.Pop(); continue; }
                if (routine.Current is IEnumerator nested) { routines.Push(nested); continue; }
                return;
            }
            Finish();
        }
        catch (Exception exception) { report.failed.Add(exception.ToString()); Finish(); }
    }
    static void ObserveError(string message, string stack, LogType type)
    {
        if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
        { report.runtimeErrors.Add(message + "\n" + stack); Save(); }
    }
    static void Finish() { finished = true; Cleanup(); Save(); EditorApplication.ExitPlaymode(); }
    static void Cleanup()
    {
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= ObserveError;
        if (pad != null && pad.added) InputSystem.RemoveDevice(pad);
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
        foreach (var device in restoreDevices) if (device.added) InputSystem.EnableDevice(device);
        restoreDevices.Clear();
        if (savedBackground) InputSystem.settings.backgroundBehavior = background;
        savedBackground = false;
    }
    static void Save() { File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true)); }
    static void Check(bool result, string label) { if (!result) throw new InvalidOperationException(label); report.passed.Add(label); Save(); }
    static object Get(object obj, string name) { return obj.GetType().GetField(name, Flags).GetValue(obj); }
    static void Set(object obj, string name, object value) { obj.GetType().GetField(name, Flags).SetValue(obj, value); }
    static T Property<T>(object obj, string name) { return (T)obj.GetType().GetProperty(name, Flags).GetValue(obj); }
    static object Call(object obj, string name, params object[] args) { return obj.GetType().GetMethod(name, Flags).Invoke(obj, args); }
    static MonoBehaviour Find(string name) { return (MonoBehaviour)Object.FindObjectOfType(Type.GetType(name + ", Assembly-CSharp", true)); }
    static IEnumerator Frames(int count) { for (int i = 0; i < count; i++) yield return null; }
    static void Keys(params UnityEngine.InputSystem.Key[] keys) { InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys)); }
    static void Pad(Vector2 stick = default, bool a = false, bool b = false)
    {
        var state = new GamepadState { leftStick = stick };
        if (a) state = state.WithButton(GamepadButton.South);
        if (b) state = state.WithButton(GamepadButton.East);
        InputSystem.QueueStateEvent(pad, state);
    }
    static void Move(Vector3 position) { Call(player.GetComponent("GhostPlayerMotor"), "Teleport", position); }
    static bool Visible { get { return Property<bool>(prompt, "IsVisible"); } }
    static string Mode { get { return Get(prank, "mode").ToString(); } }
    static IEnumerator Capture(string suffix)
    {
        string path = "Logs/InputSystem/possess-" + suffix + ".png";
        if (File.Exists(path)) File.Delete(path);
        ScreenCapture.CaptureScreenshot(path);
        yield return Frames(20);
        Check(File.Exists(path), "Captured actual Game view: " + suffix);
        report.screenshots.Add(path); Save();
    }

    static IEnumerator Checks()
    {
        yield return Frames(6);
        input = Find("GhostInput"); floor = Find("GhostParty.LevelWhitebox.WhiteboxPlayable");
        prank = Find("GhostPartyPrototype"); prompt = Find("CurtainPossessionPrompt");
        encounter = Find("GhostParty.LevelWhitebox.LevelCurtainEncounter");
        player = GameObject.Find("InvisibleGhost").transform;
        Check(prompt != null && prank != null && encounter.transform == prank.transform, "Prompt belongs to the actual curtain encounter");
        input.SendMessage("OnApplicationFocus", true);
        background = InputSystem.settings.backgroundBehavior; savedBackground = true;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        foreach (var device in InputSystem.devices.ToArray())
            if (device.enabled && (device is Gamepad || device is Keyboard || device is Mouse))
            { restoreDevices.Add(device); InputSystem.DisableDevice(device); }
        keyboard = InputSystem.AddDevice<Keyboard>("PossessionCheckKeyboard");
        mouse = InputSystem.AddDevice<Mouse>("PossessionCheckMouse");
        pad = InputSystem.AddDevice<Gamepad>("PossessionCheckPad");
        foreach (var name in new[] { "Possess_E", "Possess_A" })
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath("Assets/Resources/UI/" + name + ".png");
            var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
            Check(importer.textureType == TextureImporterType.Sprite && importer.spriteImportMode == SpriteImportMode.Single &&
                settings.spriteMeshType == SpriteMeshType.FullRect && importer.alphaIsTransparency &&
                importer.alphaSource == TextureImporterAlphaSource.FromInput, name + " uses Sprite / Single / Full Rect / source alpha");
        }
        Transform anchor = Property<Transform>(prompt, "Anchor"); image = Property<Image>(prompt, "Bubble");
        Check(anchor != null && anchor.parent == prank.transform, "PromptAnchor is a saved child of the brown curtain block");
        Check(image.canvas.name == "PrototypeHUD" && !image.raycastTarget, "Reuses PrototypeHUD and does not block UI raycasts");
        var scaler = image.canvas.GetComponent<CanvasScaler>();
        Check(scaler.referenceResolution == new Vector2(1920, 1080), "Retains the existing 1920 x 1080 Canvas reference");
        Check(!Object.FindObjectsOfType<TextMesh>(true).Any(t => t.text.Trim() == "Press E"), "Old red Press E scene text is removed");
        Vector3 home = (Vector3)Get(prank, "curtainHome");
        Vector3 near = home + new Vector3(.8f, 0, -.6f); near.y = player.position.y;
        Keys(UnityEngine.InputSystem.Key.W); yield return Frames(2); Keys(); Move(near); yield return Frames(40);
        Check(Visible && image.sprite.name == "Possess_E", "Near valid target shows the E bubble");
        Check(Property<Transform>(floor, "CurrentInteractionTarget") == prank.transform, "Prompt and keyboard use the identical selected target");
        Check(image.rectTransform.sizeDelta == new Vector2(216, 64), "Bubble size is 216 x 64 reference pixels");
        Vector2 projected = Camera.main.WorldToScreenPoint(anchor.position);
        Vector2 pivot = RectTransformUtility.WorldToScreenPoint(null, image.rectTransform.position);
        Check(Vector2.Distance(projected, pivot) < 1f, "Bubble tip pivot follows the scene anchor within one screen pixel");
        Check(image.rectTransform.localRotation == Quaternion.identity, "Bubble faces the screen");
        yield return Capture("E");

        Move(near + Vector3.right * 3); yield return Frames(3); Check(!Visible, "Leaving range hides the bubble");
        Move(near); yield return Frames(3); Check(Visible, "Returning to range restores the bubble");
        encounter.enabled = false; yield return Frames(2); Check(!Visible, "Disabled encounter hides the bubble");
        encounter.enabled = true; yield return Frames(2); Check(Visible, "Re-enabled encounter is reevaluated");
        var spot = Get(floor, "curtainSpot"); Set(floor, "curtainSpot", null); yield return Frames(2);
        Check(!Visible && Property<Transform>(floor, "CurrentInteractionTarget") == null, "Invalid target reference hides the prompt and cannot interact");
        Set(floor, "curtainSpot", spot);
        var stair = (Transform)Get(floor, "stair"); Vector3 stairPosition = stair.position;
        stair.position = prank.transform.position; yield return Frames(2);
        Check(Property<Transform>(floor, "CurrentInteractionTarget") == stair && !Visible, "Overlapping targets retain actual stair priority and hide the curtain bubble");
        stair.position = stairPosition; yield return Frames(2); Check(Visible, "Removing overlap restores the selected curtain prompt");
        Vector3 local = anchor.localPosition;
        anchor.localPosition += new Vector3(0, .1f, 0); yield return Frames(2);
        projected = Camera.main.WorldToScreenPoint(anchor.position); pivot = RectTransformUtility.WorldToScreenPoint(null, image.rectTransform.position);
        Check(Vector2.Distance(projected, pivot) < 1f, "Inspector anchor changes move the bubble without changing interaction range");
        anchor.localPosition = local;
        anchor.gameObject.SetActive(false); yield return Frames(2); Check(!Visible, "Invalid anchor hides the bubble");
        anchor.gameObject.SetActive(true); yield return Frames(2);
        Pad(new Vector2(.15f, .05f)); yield return Frames(3);
        Check(Visible && image.sprite.name == "Possess_E", "Stick drift does not switch the keyboard icon");
        Keys(UnityEngine.InputSystem.Key.E); yield return Frames(2); Keys(); yield return Frames(2);
        Check(Mode == "Closeup" && !Visible, "E enters this curtain and immediately hides its bubble");
        Keys(UnityEngine.InputSystem.Key.Escape); yield return Frames(2); Keys(); yield return Frames(30);
        Check(Mode == "Explore" && Visible && image.sprite.name == "Possess_E", "Escape restores E prompt according to current distance");

        Pad(new Vector2(.8f, 0)); yield return Frames(2); Pad(); Move(near); yield return Frames(40);
        Check(Visible && image.sprite.name == "Possess_A", "Intentional controller input selects the green A bubble");
        yield return Capture("A");
        Keys(UnityEngine.InputSystem.Key.W); yield return Frames(2); Keys(); Move(near); yield return Frames(3);
        Check(Visible && image.sprite.name == "Possess_E", "Meaningful keyboard operation switches back to E");
        Pad(new Vector2(.8f, 0)); yield return Frames(2); Pad(); Move(near); yield return Frames(3);
        Pad(a: true); yield return Frames(3);
        Check(Mode == "Closeup" && !Visible && (int)Get(prank, "curtainStep") == 0, "A enters the identical target without consuming the first rhythmic tap");
        Check((bool)input.GetType().GetProperty("TapNeedsRelease", Flags).GetValue(null), "Entry A remains release-gated while held");
        Pad(); yield return Frames(3);
        Check(!(bool)input.GetType().GetProperty("TapNeedsRelease", Flags).GetValue(null), "Releasing A opens the next tap gate");
        Pad(b: true); yield return Frames(2); Pad(); yield return Frames(30);
        Check(Mode == "Explore" && Visible && image.sprite.name == "Possess_A", "B exits and restores A prompt for the selected nearby curtain");
        Move(near + Vector3.right * 3); yield return Frames(3); Check(!Visible, "Prompt also hides when leaving after possession");
        Check(report.runtimeErrors.Count == 0, "No Unity runtime errors during prompt checks");
    }
}
