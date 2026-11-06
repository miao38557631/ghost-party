using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Real Play Mode with virtual Input System devices and intercepted rumble commands.
/// Does not claim physical controller, motor, or native OS-input acceptance.</summary>
[InitializeOnLoad]
public static class GhostGamepadPlayChecks
{
    const string Key = "GhostInput.PlayChecks";
    const string ReportPath = "Logs/InputSystem/play-checks.json";
    static readonly string[] Scenes = { "Assets/LevelWhitebox/Scenes/Level01_FirstFloor.unity", "Assets/Scenes/SampleScene.unity" };
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    [Serializable] public class Report
    {
        public string kind = "Unity Play Mode, virtual gamepad/keyboard/mouse, captured rumble commands; not physical hardware acceptance";
        public bool complete;
        public string running;
        public List<string> passed = new List<string>();
        public List<string> failed = new List<string>();
        public List<string> mouseDiagnostics = new List<string>();
    }
    sealed class Rumble { public int device; public float low, high; public double time; }
    static Report report;
    static readonly List<Rumble> commands = new List<Rumble>();
    static readonly List<InputDevice> restoreDevices = new List<InputDevice>();
    static readonly Stack<IEnumerator> routines = new Stack<IEnumerator>();
    static Gamepad pad, other;
    static Keyboard keyboard;
    static Mouse mouse;
    static MonoBehaviour prank, input;
    static Transform player;
    static Type inputType;
    static int lastFrame;
    static double timeout;
    static InputSettings.BackgroundBehavior previousBackground;
    static bool backgroundSaved;
    static bool finishedScene;

    static GhostGamepadPlayChecks()
    {
        EditorApplication.playModeStateChanged += StateChanged;
        if (SessionState.GetBool(Key, false) && !EditorApplication.isPlayingOrWillChangePlaymode)
            EditorApplication.update += StartSceneOnNextUpdate;
    }

    [MenuItem("Ghost Party/Input/Restart Editor for Both Input Backends")]
    public static void RestartForBoth()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play before restarting.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save or back up unsaved scenes before restarting.");
        string project = Directory.GetParent(Application.dataPath).FullName;
        EditorApplication.delayCall += () => EditorApplication.OpenProject(project);
    }

    [MenuItem("Ghost Party/Input/Run Gamepad and Keyboard Play Checks")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play before input checks.");
        Directory.CreateDirectory("Logs/InputSystem");
        report = new Report();
        Save();
        SessionState.SetBool(Key, true);
        SessionState.SetInt(Key + ".scene", 0);
        SessionState.SetString(Key + ".start", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetString(Key + ".editorScene", SceneManager.GetActiveScene().path);
        SessionState.SetBool(Key + ".dirty", SceneManager.GetActiveScene().isDirty);
        StartScene();
    }

    static void StartScene()
    {
        if (!SessionState.GetBool(Key, false) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(Scenes[SessionState.GetInt(Key + ".scene", 0)]);
        EditorApplication.EnterPlaymode();
    }
    static void StartSceneOnNextUpdate()
    {
        EditorApplication.update -= StartSceneOnNextUpdate;
        StartScene();
    }
    static void StateChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            report = JsonUtility.FromJson<Report>(File.ReadAllText(ReportPath));
            finishedScene = false;
            report.running = SceneManager.GetActiveScene().path;
            Save();
            routines.Clear(); routines.Push(Checks());
            lastFrame = -1; timeout = EditorApplication.timeSinceStartup + 100;
            EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            if (!finishedScene && report != null)
            { report.failed.Add("Play checks interrupted before completion"); Save(); }
            Cleanup();
        }
        if (state != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.update -= Tick;
        report = JsonUtility.FromJson<Report>(File.ReadAllText(ReportPath));
        int next = SessionState.GetInt(Key + ".scene", 0) + 1;
        if (next < Scenes.Length && report.failed.Count == 0)
        {
            SessionState.SetInt(Key + ".scene", next);
            EditorApplication.update += StartSceneOnNextUpdate;
            return;
        }
        var old = SessionState.GetString(Key + ".start", "");
        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(old) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(old);
        bool preserved = SceneManager.GetActiveScene().path == SessionState.GetString(Key + ".editorScene", "") &&
            SceneManager.GetActiveScene().isDirty == SessionState.GetBool(Key + ".dirty", false);
        (preserved ? report.passed : report.failed).Add("Editor scene and dirty state restored without saving");
        report.complete = true; report.running = ""; Save(); SessionState.SetBool(Key, false);
        Debug.Log("GHOST_INPUT_CHECKS_COMPLETE: " + report.passed.Count + " passed, " + report.failed.Count + " failed; " + ReportPath);
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isPaused) return;
        try
        {
            if (EditorApplication.timeSinceStartup > timeout) throw new TimeoutException("Input regression timeout: " + report.running);
            if (lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            while (routines.Count > 0)
            {
                var current = routines.Peek();
                if (!current.MoveNext()) { routines.Pop(); continue; }
                if (current.Current is IEnumerator nested) { routines.Push(nested); continue; }
                return;
            }
            Finish();
        }
        catch (Exception exception)
        {
            report.failed.Add(report.running + ": " + exception); Save(); Finish();
        }
    }
    static void Finish()
    {
        finishedScene = true;
        Cleanup();
        Save();
        EditorApplication.ExitPlaymode();
    }
    static void Cleanup()
    {
        EditorApplication.update -= Tick;
        if (inputType != null) CallStatic("StopHaptics");
        Time.timeScale = 1;
        if (pad != null && pad.added) InputSystem.RemoveDevice(pad);
        if (other != null && other.added) InputSystem.RemoveDevice(other);
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
        UnhookCommands();
        InputSystem.onAfterUpdate -= ObserveMouse;
        foreach (var device in restoreDevices) if (device.added) InputSystem.EnableDevice(device);
        restoreDevices.Clear();
        if (backgroundSaved) InputSystem.settings.backgroundBehavior = previousBackground;
        backgroundSaved = false;
    }
    static void Save() { File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true)); }
    static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        report.passed.Add(SceneManager.GetActiveScene().name + ": " + label); Save();
    }
    static object Get(object obj, string field) { return obj.GetType().GetField(field, Flags).GetValue(obj); }
    static void Set(object obj, string field, object value) { obj.GetType().GetField(field, Flags).SetValue(obj, value); }
    static object Call(object obj, string method, params object[] args) { return obj.GetType().GetMethod(method, Flags).Invoke(obj, args); }
    static object CallStatic(string method, params object[] args) { return inputType.GetMethod(method, Flags).Invoke(null, args); }
    static T InputProperty<T>(string name) { return (T)inputType.GetProperty(name, Flags).GetValue(null); }
    static int Step { get { return (int)Get(prank, "curtainStep"); } }
    static double Beat { get { var clock = Get(prank, "beatClock"); return (double)clock.GetType().GetProperty("BeatIndex").GetValue(clock); } }
    static double Target { get { return (double)Get(prank, "curtainTargetBeat"); } }
    static string Mode { get { return Get(prank, "mode").ToString(); } }
    static string Stage { get { return Get(prank, "stage").ToString(); } }
    static string ActionAt(int step) { return Get(((IList)Get(prank, "curtainPlan"))[step], "action").ToString(); }
    static int PulseCount { get { return commands.Count(c => c.low > 0 || c.high > 0); } }
    static bool Stopped { get { return commands.Count > 0 && commands[commands.Count - 1].low == 0 && commands[commands.Count - 1].high == 0; } }
    static IEnumerator Frames(int count) { for (int i = 0; i < count; i++) yield return null; }
    static IEnumerator WaitBeat(double value)
    {
        double end = Time.realtimeSinceStartupAsDouble + 4;
        double initialDsp = AudioSettings.dspTime;
        while (Beat < value)
        {
            if (Time.realtimeSinceStartupAsDouble > end)
                throw new TimeoutException("DSP beat did not advance: beat=" + Beat + ", waiting=" + value +
                    ", dsp=" + initialDsp + "->" + AudioSettings.dspTime + ", suspended=" + InputProperty<bool>("Suspended") +
                    ", audioPaused=" + AudioListener.pause + ", timeScale=" + Time.timeScale);
            yield return null;
        }
    }
    static IEnumerator WaitStep(int value)
    {
        double end = Time.realtimeSinceStartupAsDouble + 4;
        while (Step == value && Stage == "CurtainSequence")
        { if (Time.realtimeSinceStartupAsDouble > end) throw new TimeoutException("Step " + value + " (" + ActionAt(value) + ") did not complete"); yield return null; }
        Check(Step == value + 1, "Step " + value + " completed through the input/beat pipeline (actual=" + Step + ", stage=" + Stage + ")");
    }
    static void Pad(Vector2 stick = default, float rt = 0, bool a = false, bool b = false, Gamepad device = null)
    {
        var state = new GamepadState { leftStick = stick, rightTrigger = rt };
        if (a) state = state.WithButton(GamepadButton.South);
        if (b) state = state.WithButton(GamepadButton.East);
        InputSystem.QueueStateEvent(device ?? pad, state);
    }
    static void Keys(params UnityEngine.InputSystem.Key[] keys) { InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys)); }
    static void Pointer(Vector2 position, bool held)
    { InputSystem.QueueStateEvent(mouse, new MouseState { position = position, buttons = (ushort)(held ? 1 : 0) }); }
    static void NearCurtain()
    {
        var home = (Vector3)Get(prank, "curtainHome");
        var position = home + new Vector3(0.8f, 0, -0.6f); position.y = player.position.y;
        Call(player.GetComponent("GhostPlayerMotor"), "Teleport", position);
    }
    static Vector2 Direction(string action)
    {
        switch (action)
        {
            case "DragDown": return Vector2.down;
            case "SwipeLeft": return Vector2.left;
            case "WindLeft": return new Vector2(-0.9f, -0.9f);
            case "ThrowRightRelease": return new Vector2(0.9f, 0.9f);
            default: return Vector2.right;
        }
    }
    static unsafe long? DeviceCommand(InputDevice device, InputDeviceCommand* command)
    {
        if (device != pad && device != other) return null;
        if (command->type != new UnityEngine.InputSystem.Utilities.FourCC('R', 'M', 'B', 'L')) return null;
        if (command->sizeInBytes < InputDeviceCommand.BaseCommandSize + 8) return null;
        float* speeds = (float*)((byte*)command + InputDeviceCommand.BaseCommandSize);
        commands.Add(new Rumble { device = device.deviceId, low = speeds[0],
            high = speeds[1], time = Time.realtimeSinceStartupAsDouble });
        return InputDeviceCommand.GenericSuccess;
    }
    static unsafe void HookCommands() { InputSystem.onDeviceCommand += DeviceCommand; }
    static unsafe void UnhookCommands() { InputSystem.onDeviceCommand -= DeviceCommand; }
    static void Pulse()
    { CallStatic("ConfirmHit", pad, Enum.Parse(inputType.GetNestedType("Hit"), "FinalThrow")); }

    static IEnumerator Checks()
    {
        yield return Frames(6);
        inputType = Type.GetType("GhostInput, Assembly-CSharp", true);
        input = (MonoBehaviour)Object.FindObjectOfType(inputType);
        prank = (MonoBehaviour)Object.FindObjectOfType(Type.GetType("GhostPartyPrototype, Assembly-CSharp", true));
        player = GameObject.Find("InvisibleGhost").transform;
        Check(prank != null && input != null, "Runtime input adapter and existing curtain controller initialized");
        previousBackground = InputSystem.settings.backgroundBehavior; backgroundSaved = true;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        input.SendMessage("OnApplicationFocus", true);
        // Native mouse activity can legitimately replace Mouse.current between queued test
        // events. Isolate virtual devices within Unity, restoring every enabled device in Finish.
        restoreDevices.Clear();
        foreach (var device in InputSystem.devices.ToArray())
            if (device.enabled && (device is Keyboard || device is Mouse || device is Gamepad))
            { restoreDevices.Add(device); InputSystem.DisableDevice(device); }
        pad = InputSystem.AddDevice<Gamepad>("GhostCheckPad");
        other = InputSystem.AddDevice<Gamepad>("GhostCheckOtherPad");
        keyboard = InputSystem.AddDevice<Keyboard>("GhostCheckKeyboard");
        mouse = InputSystem.AddDevice<Mouse>("GhostCheckMouse");
        commands.Clear(); HookCommands();
        InputSystem.onAfterUpdate += ObserveMouse;
        Pointer(new Vector2(Screen.width * 0.5f, Screen.height * 0.1f), false); Keys(UnityEngine.InputSystem.Key.W);
        yield return Frames(3); Keys(); yield return Frames(2);
        Pad(new Vector2(0.15f, 0.05f)); yield return Frames(3);
        Check(!InputProperty<bool>("UsingGamepad"), "Stick drift does not switch keyboard prompts");
        Check(((Vector3)CallStatic("GroundMovement")).sqrMagnitude == 0, "Stick drift does not move the player");
        Vector3 before = player.position;
        Pad(Vector2.left * 0.8f); yield return Frames(6); Pad(); yield return Frames(2);
        Check(Vector3.Distance(before, player.position) > 0.01f, "Left stick drives the existing CharacterController motor; before=" + before + "; after=" + player.position + "; input=" + CallStatic("GroundMovement") + "; gamepad=" + InputProperty<bool>("UsingGamepad") + "; suspended=" + InputProperty<bool>("Suspended"));
        Check(InputProperty<bool>("UsingGamepad"), "Intentional stick motion selects gamepad prompts");
        NearCurtain(); Pad(a: true); yield return Frames(3);
        Check(Mode == "Closeup" && Step == 0, "A enters possession without performing the first tap");
        yield return WaitBeat(Target + 0.02);
        Check(Step == 0 && PulseCount == 0, "Holding the entry A through the beat produces neither tap nor rumble");
        Pad(); yield return Frames(2);
        // Give the next press a fresh target after deliberately holding through the first beat.
        // Editor stalls must not turn the release-gate assertion into a late-input test.
        Set(prank, "curtainTargetBeat", Math.Ceiling(Beat + 1));
        yield return WaitBeat(Target - .08);
        Pad(a: true); yield return Frames(2);
        Check(Step == 1 && PulseCount == 1, "Release then A accepts the first rhythmic tap once; step=" + Step + ", pulses=" + PulseCount);
        Pad(); yield return Frames(2);
        Pad(new Vector2(0.08f, 0), device: other); other.MakeCurrent(); yield return Frames(2);
        Check(InputProperty<Gamepad>("ActiveGamepad") == pad, "Gamepad.current changed by another device does not steal ownership");
        Check(Object.FindObjectsOfType<Text>().Any(t => t.text.Contains("A") && t.text.Contains("点")), "Player HUD presents A tap instructions");
        yield return Round(true);
        Check(Stage == "Complete", "Gamepad completes taps, RT directions, sustained hold and final throw");
        var pulses = commands.Where(c => c.low > 0 || c.high > 0).ToList();
        Check(pulses.Count == 11, "Exactly 2 tap, 8 swipe and 1 final-throw pulses; no wrap/hold duplicate");
        Check(pulses.All(c => c.device == pad.deviceId), "Every hit vibrates the acting gamepad only");
        Check(pulses.Count(c => Mathf.Approximately(c.low, .1f) && Mathf.Approximately(c.high, .2f)) == 2, "Tap pulse uses 0.10 / 0.20");
        Check(pulses.Count(c => Mathf.Approximately(c.low, .25f) && Mathf.Approximately(c.high, .38f)) == 8, "Swipe pulse uses 0.25 / 0.38");
        Check(pulses.Count(c => Mathf.Approximately(c.low, .65f) && Mathf.Approximately(c.high, .45f)) == 1, "Final throw pulse uses 0.65 / 0.45");
        yield return Frames(25); Check(Stopped, "Rumble stops after its unscaled deadline");
        foreach (var pulse in pulses)
        {
            var stop = commands.FirstOrDefault(c => c.device == pulse.device && c.time >= pulse.time && c.low == 0 && c.high == 0);
            double minimum = pulse.low < .2f ? .045 : pulse.low < .5f ? .065 : .140;
            Check(stop != null && stop.time - pulse.time >= minimum - .002,
                "Motor command persists for configured " + (minimum * 1000) + " ms and then stops");
        }
        Pad(b: true); yield return Frames(2); Pad(); yield return Frames(2);
        Check(Mode == "Explore" && (bool)player.GetComponent("GhostPlayerVisibility").GetType().GetProperty("IsVisible").GetValue(player.GetComponent("GhostPlayerVisibility")), "B restores exploration and player visibility");

        // Repeated identical actions expose accidental level-triggered rearming.
        NearCurtain(); Pad(a: true); yield return Frames(2); Pad(); yield return Frames(2);
        int earlyPulses = PulseCount;
        Pad(a: true); yield return Frames(2); Pad(); yield return Frames(2);
        Check(Step == 0 && PulseCount == earlyPulses, "Early A does not advance the action or trigger success rumble");
        Set(prank, "curtainStep", 6); Call(prank, "ResetGestureState"); Set(prank, "curtainTargetBeat", Math.Ceiling(Beat + 1));
        var nextConfig = ((IList)Get(prank, "curtainPlan"))[7]; var oldAction = Get(nextConfig, "action");
        Set(nextConfig, "action", Enum.Parse(oldAction.GetType(), "SwipeLeft"));
        int firstCount = PulseCount;
        yield return WaitBeat(Target - .20); Pad(Vector2.left, 1); yield return WaitStep(6);
        yield return WaitBeat(Target + .10);
        Check(Step == 7 && PulseCount == firstCount + 1, "One sustained stick deflection cannot complete two actions");
        Set(nextConfig, "action", oldAction);
        Set(prank, "curtainStep", 14); Call(prank, "ResetGestureState"); Set(prank, "curtainTargetBeat", Math.Ceiling(Beat + 1));
        Pad(Vector2.zero, 1); yield return Frames(2);
        yield return WaitBeat(Target - .20); Pad(new Vector2(-.9f, -.9f), 1); yield return WaitStep(14);
        int holdPulses = PulseCount; Pad(); yield return Frames(2);
        Check(Step == 14 && (bool)Get(prank, "gestureRetryPending") && PulseCount == holdPulses,
            "Releasing RT during Hold restarts wind-up without success rumble");
        Pulse();
        Pad(b: true); yield return Frames(2); Pad(); yield return Frames(2);
        Check(Stopped && Mode == "Explore", "B cancels an active rumble and possession");

        // Keyboard and mouse exercise the same production handlers and clock.
        Keys(UnityEngine.InputSystem.Key.W); before = player.position; yield return Frames(6); Keys(); yield return Frames(2);
        Check(Vector3.Distance(before, player.position) > .01f && !InputProperty<bool>("UsingGamepad"), "Keyboard movement and prompt switching remain functional");
        NearCurtain(); Keys(UnityEngine.InputSystem.Key.E); yield return Frames(2); Keys(); yield return Frames(2);
        Check(Mode == "Closeup" && Step == 0, "E still enters possession");
        int keyboardPulses = PulseCount;
        yield return Round(false);
        Check(Stage == "Complete" && PulseCount == keyboardPulses, "Mouse/keyboard completes the same full sequence without vibrating an idle gamepad");
        Keys(UnityEngine.InputSystem.Key.Escape); yield return Frames(2); Keys(); yield return Frames(2);
        Check(Mode == "Explore", "Escape still returns to exploration");
        Check(Object.FindObjectsOfType<StandaloneInputModule>().Count(m => m.isActiveAndEnabled) == 1,
            "Exactly one existing keyboard/mouse UI input module remains active");
        NearCurtain(); Keys(UnityEngine.InputSystem.Key.E); yield return Frames(2); Keys(); yield return Frames(2);
        Keys(UnityEngine.InputSystem.Key.F8); yield return Frames(2); Keys(); yield return Frames(2);
        Check((bool)Get(prank, "developerHudVisible"), "F8 opens the existing settings UI");
        yield return CheckUiButton("LanguageButton", "language");
        yield return CheckUiButton("DifficultyButton", "difficulty");
        Keys(UnityEngine.InputSystem.Key.F8); yield return Frames(2); Keys(); yield return Frames(2);
        Keys(UnityEngine.InputSystem.Key.Escape); yield return Frames(2); Keys(); yield return Frames(2);
        Check(Mode == "Explore" && !(bool)Get(prank, "developerHudVisible"), "Settings UI closes and Escape returns to exploration");

        // Cleanup tests issue intercepted virtual-device commands; no physical motors are used.
        Pulse(); Time.timeScale = 0; yield return Frames(2); Check(Stopped, "Time-scale pause immediately clears motors"); Time.timeScale = 1;
        Pulse(); input.SendMessage("OnApplicationFocus", false); Check(Stopped, "Focus loss clears motors"); input.SendMessage("OnApplicationFocus", true);
        Pulse(); input.SendMessage("OnApplicationPause", true); Check(Stopped, "Application pause clears motors"); input.SendMessage("OnApplicationPause", false);
        Pulse(); EditorApplication.isPaused = true; Check(Stopped, "Editor pause clears motors without an Update tick"); EditorApplication.isPaused = false;
        Pulse(); input.enabled = false; Check(Stopped, "Disabling the adapter clears motors"); input.enabled = true;
        Pulse(); var originalScene = SceneManager.GetActiveScene(); var temporaryScene = SceneManager.CreateScene("GhostInputCleanupCheck");
        SceneManager.SetActiveScene(temporaryScene); Check(Stopped, "Active scene change clears motors");
        SceneManager.SetActiveScene(originalScene); SceneManager.UnloadSceneAsync(temporaryScene);
        Pulse(); InputSystem.DisableDevice(pad); Check(Stopped, "Device disable clears motors"); InputSystem.EnableDevice(pad);
        Pulse(); Call(input, "DeviceChanged", pad, InputDeviceChange.Disconnected); Check(Stopped, "Disconnect callback clears motors and ownership");
        Pulse(); InputSystem.RemoveDevice(pad); Check(Stopped, "Device removal clears motors");
        Check(!InputProperty<bool>("GripHeld"), "Disconnected controller cannot leave a latched RT grip");
    }

    static IEnumerator Round(bool controller)
    {
        double deadline = Time.realtimeSinceStartupAsDouble + 35;
        while (Stage == "CurtainSequence")
        {
            if (Time.realtimeSinceStartupAsDouble > deadline) throw new TimeoutException("Full round timed out at " + Step);
            int step = Step; string action = ActionAt(step);
            if (action.StartsWith("Auto") || action == "Hold")
            {
                // Existing mouse controls deliberately release the previous gesture during
                // automatic wrapping, then require a fresh click for the next drag.
                if (!controller && action.StartsWith("Auto")) Pointer(GameplayPoint(), false);
                yield return WaitStep(step); continue;
            }
            if (action == "Tap")
            {
                yield return WaitBeat(Target - .05);
                if (controller) Pad(a: true);
                else if (step == 0) Keys(UnityEngine.InputSystem.Key.Space);
                else Pointer(GameplayPoint(), true);
                yield return WaitStep(step);
                if (controller) Pad();
                else { Keys(); Pointer(GameplayPoint(), false); }
                yield return Frames(2);
                continue;
            }
            if (!controller)
            {
                // Rebase the mouse for each gesture except Hold, preserving its grip.
                Pointer(new Vector2(Screen.width * .5f, Screen.height * .4f), true);
                yield return Frames(2);
            }
            yield return WaitBeat(Target - .20);
            if (controller) Pad(Direction(action), 1);
            else
            {
                Vector3 start = (Vector3)Get(prank, "gestureStart");
                Vector2 direction = Direction(action);
                Pointer((Vector2)start + Vector2.Scale(direction, new Vector2(Screen.width * .12f, Screen.height * .12f)), true);
            }
            yield return WaitStep(step);
            if (controller && action.StartsWith("Swipe")) Check(InputProperty<bool>("GripHeld"), "RT stays held through alternating " + action);
        }
    }

    static IEnumerator CheckUiButton(string name, string field)
    {
        var button = Object.FindObjectsOfType<Button>().First(b => b.name == name);
        var rect = (RectTransform)button.transform;
        var canvas = button.GetComponentInParent<Canvas>().rootCanvas;
        var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector2 position = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center));
        var pointer = new PointerEventData(EventSystem.current) { position = position, button = PointerEventData.InputButton.Left };
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointer, hits);
        Check(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == button,
            name + " remains reachable by the UI raycaster");
        Pointer(position, false); yield return Frames(2);
        Set(prank, "curtainTargetBeat", Beat + .15);
        Pointer(position, true); yield return Frames(2);
        Check(Mode == "Closeup" && Step == 0, name + " mouse press does not leak into a rhythmic tap");
        Pointer(position, false); yield return Frames(2);
        // Invoke the actual EventSystem click handler; native OS polling by the legacy
        // StandaloneInputModule still requires a real mouse acceptance check.
        var original = Get(prank, field);
        ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        Check(!Equals(original, Get(prank, field)), name + " EventSystem click changes its setting");
        ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        Check(Equals(original, Get(prank, field)), name + " second click restores its setting");
        Pointer(GameplayPoint(), false);
    }

    static Vector2 GameplayPoint()
    {
        foreach (var point in new[] { new Vector2(.9f, .1f), new Vector2(.5f, .5f), new Vector2(.1f, .1f), new Vector2(.9f, .8f) })
        {
            var position = Vector2.Scale(point, new Vector2(Screen.width, Screen.height));
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, hits);
            if (hits.Count == 0) return position;
        }
        throw new InvalidOperationException("No uncovered gameplay point available for mouse regression");
    }
    static void ObserveMouse()
    {
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame || report.mouseDiagnostics.Count > 20) return;
        report.mouseDiagnostics.Add(InputState.currentUpdateType + "; current=" + Mouse.current?.name + "; test=" + mouse.name +
            "; position=" + mouse.position.ReadValue() + "; screen=" + Screen.width + "x" + Screen.height);
        Save();
    }
}
