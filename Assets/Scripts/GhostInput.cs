using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;

/// <summary>Input adapter only. Gameplay still owns interaction, direction validation and the DSP beat clock.</summary>
[DefaultExecutionOrder(-1000)]
public sealed class GhostInput : MonoBehaviour
{
    public enum Hit { WindowTap, CurtainHit, FinalThrow }
    sealed class PadState
    {
        public Vector2 activityStick;
        public bool grip;
        public int direction = -1;
    }
    static GhostInput instance;
    readonly Dictionary<Gamepad, PadState> pads = new Dictionary<Gamepad, PadState>();
    GhostInputSettings settings;
    Gamepad activePad, rumblingPad;
    double rumbleDeadline;
    bool usingGamepad, tapBlocked, focused = true, applicationPaused;
    bool grip, gripReleased, directionAvailable;
    Vector2 directionSample, rawStick;
    int sampledFrame = -1, entryFrame = -1, revision;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { instance = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap() { Ensure(); }

    static GhostInput Ensure()
    {
        if (instance == null)
        {
            var go = new GameObject("GhostInput");
            instance = go.AddComponent<GhostInput>();
            DontDestroyOnLoad(go);
        }
        return instance;
    }

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        settings = Resources.Load<GhostInputSettings>("Input/GhostInputSettings");
        if (settings == null) settings = ScriptableObject.CreateInstance<GhostInputSettings>();
    }

    void OnEnable()
    {
        InputSystem.onDeviceChange += DeviceChanged;
        SceneManager.activeSceneChanged += SceneChanged;
        SceneManager.sceneUnloaded += SceneUnloaded;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.pauseStateChanged += EditorPauseChanged;
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += StopHaptics;
#endif
    }

    void OnDisable()
    {
        StopRumble();
        InputSystem.onDeviceChange -= DeviceChanged;
        SceneManager.activeSceneChanged -= SceneChanged;
        SceneManager.sceneUnloaded -= SceneUnloaded;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.pauseStateChanged -= EditorPauseChanged;
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= StopHaptics;
#endif
    }

    void OnDestroy() { if (instance == this) instance = null; }
    void OnApplicationQuit() { StopRumble(); }
    void OnApplicationFocus(bool value) { focused = value; if (!value) Suspend(); }
    void OnApplicationPause(bool value) { applicationPaused = value; if (value) Suspend(); }
    void SceneChanged(Scene previous, Scene next) { Suspend(); }
    void SceneUnloaded(Scene scene) { Suspend(); }
#if UNITY_EDITOR
    void EditorPauseChanged(UnityEditor.PauseState state) { if (state == UnityEditor.PauseState.Paused) Suspend(); }
#endif

    void Suspend()
    {
        StopRumble();
        tapBlocked = true;
        directionAvailable = grip = false;
        revision++;
    }

    void Update()
    {
        if (Time.timeScale <= 0f || !focused || applicationPaused) StopRumble();
        else if (rumblingPad != null && Time.realtimeSinceStartupAsDouble >= rumbleDeadline) StopRumble();
        Sample();
    }

    static GhostInput Read() { var input = Ensure(); input.Sample(); return input; }
    public static bool UsingGamepad { get { return Read().usingGamepad; } }
    public static Gamepad ActiveGamepad { get { return Read().activePad; } }
    public static int DeviceRevision { get { return Read().revision; } }
    public static bool Suspended { get { var s = Read(); return !s.focused || s.applicationPaused || Time.timeScale <= 0f; } }
    public static bool GripHeld { get { return Read().grip && !Suspended; } }
    public static bool GripReleased { get { return Read().gripReleased; } }
    public static bool TapNeedsRelease { get { return Read().tapBlocked; } }
    public static bool GamepadTapPressed
    {
        get
        {
            var s = Read();
            return !Suspended && !s.tapBlocked && Time.frameCount != s.entryFrame && s.usingGamepad &&
                s.activePad != null && s.activePad.buttonSouth.wasPressedThisFrame;
        }
    }
    public static bool InteractPressed
    {
        get
        {
            var s = Read();
            return !Suspended && (KeyDown(KeyCode.E) || (s.activePad != null && s.usingGamepad && s.activePad.buttonSouth.wasPressedThisFrame));
        }
    }
    public static bool CancelPressed
    {
        get
        {
            var s = Read();
            return KeyDown(KeyCode.Escape) || (s.activePad != null && s.activePad.buttonEast.wasPressedThisFrame);
        }
    }

    public static void BeginPossession()
    {
        var s = Read();
        s.tapBlocked = true;
        s.entryFrame = Time.frameCount;
        s.directionAvailable = false;
        StopHaptics();
    }

    public static bool TryConsumeDirection(out Vector2 direction)
    {
        var s = Read();
        direction = s.directionSample;
        bool available = s.directionAvailable && s.grip && !Suspended;
        s.directionAvailable = false;
        return available;
    }

    void SelectPad(Gamepad pad)
    {
        if (activePad == pad && usingGamepad) return;
        StopRumble();
        activePad = pad;
        usingGamepad = true;
        revision++;
    }

    void Sample()
    {
        if (sampledFrame == Time.frameCount) return;
        sampledFrame = Time.frameCount;
        directionAvailable = false;
        gripReleased = false;
        bool anySouthHeld = false;
        Gamepad candidate = null;
        double candidateTime = double.MinValue;
        foreach (var pad in Gamepad.all)
        {
            if (!pad.enabled) continue;
            if (!pads.TryGetValue(pad, out var state)) { state = new PadState(); pads.Add(pad, state); }
            var stick = pad.leftStick.ReadUnprocessedValue();
            bool newGrip = pad.rightTrigger.ReadValue() >= (state.grip ? settings.triggerReleaseThreshold : settings.triggerPressThreshold);
            bool intentionalStick = stick.magnitude >= Mathf.Max(settings.deviceSwitchThreshold, settings.movementDeadzone) &&
                (stick - state.activityStick).sqrMagnitude >= 0.04f;
            bool activity = pad.buttonSouth.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame ||
                (newGrip && !state.grip) || intentionalStick;
            if (activity && pad.lastUpdateTime >= candidateTime) { candidate = pad; candidateTime = pad.lastUpdateTime; }
            if (intentionalStick || stick.magnitude < settings.movementDeadzone) state.activityStick = stick;
            anySouthHeld |= pad.buttonSouth.isPressed;
        }
        if (candidate != null) SelectPad(candidate);
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;
        bool keyboardActivity = keyboard != null && keyboard.anyKey.wasPressedThisFrame;
        bool mouseActivity = mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame ||
            mouse.delta.ReadValue().sqrMagnitude > 4f || mouse.scroll.ReadValue().sqrMagnitude > 0.1f);
        // Real keyboard/mouse action takes ownership; stationary gamepads cannot steal it back.
        if (keyboardActivity || mouseActivity)
        {
            if (usingGamepad) { StopRumble(); usingGamepad = false; revision++; }
        }
        if (tapBlocked && !anySouthHeld && Time.frameCount != entryFrame) tapBlocked = false;
        rawStick = Vector2.zero;
        bool oldGrip = grip;
        grip = false;
        foreach (var pad in Gamepad.all)
        {
            if (!pad.enabled || !pads.TryGetValue(pad, out var state)) continue;
            var stick = pad.leftStick.ReadUnprocessedValue();
            state.grip = pad.rightTrigger.ReadValue() >= (state.grip ? settings.triggerReleaseThreshold : settings.triggerPressThreshold);
            if (!state.grip || stick.magnitude <= settings.directionReleaseThreshold) state.direction = -1;
            bool freshDirection = false;
            if (state.grip && stick.magnitude >= settings.directionThreshold)
            {
                float angle = Mathf.Atan2(stick.y, stick.x) * Mathf.Rad2Deg;
                int sector = (Mathf.RoundToInt(angle / 45f) + 8) % 8;
                if (state.direction < 0 || Mathf.Abs(Mathf.DeltaAngle(state.direction * 45f, angle)) > 22.5f + settings.directionHysteresisDegrees)
                { state.direction = sector; freshDirection = true; }
            }
            if (pad != activePad || !usingGamepad) continue;
            rawStick = stick;
            grip = state.grip;
            if (freshDirection) { directionSample = stick.normalized; directionAvailable = true; }
        }
        gripReleased = oldGrip && !grip;
        if (!focused || applicationPaused || Time.timeScale <= 0f) directionAvailable = grip = false;
    }

    public static Vector3 GroundMovement()
    {
        var s = Read();
        if (Suspended) return Vector3.zero;
        Vector2 movement;
        if (s.usingGamepad)
        {
            float magnitude = s.rawStick.magnitude;
            movement = magnitude <= s.settings.movementDeadzone ? Vector2.zero : s.rawStick.normalized *
                Mathf.Clamp01((magnitude - s.settings.movementDeadzone) / (1f - s.settings.movementDeadzone));
        }
        else movement = new Vector2((Key(KeyCode.D) || Key(KeyCode.RightArrow) ? 1f : 0f) - (Key(KeyCode.A) || Key(KeyCode.LeftArrow) ? 1f : 0f),
            (Key(KeyCode.W) || Key(KeyCode.UpArrow) ? 1f : 0f) - (Key(KeyCode.S) || Key(KeyCode.DownArrow) ? 1f : 0f));
        return Vector3.ClampMagnitude(new Vector3(movement.x, 0f, movement.y), 1f);
    }

    public static void ConfirmHit(Gamepad source, Hit hit)
    {
        var s = Read();
        if (source == null || !source.added || !source.enabled || Suspended) return;
        var pulse = hit == Hit.WindowTap ? s.settings.windowTap : hit == Hit.CurtainHit ? s.settings.curtainHit : s.settings.finalThrow;
        s.StopRumble();
        if (pulse.durationMilliseconds <= 0f) return;
        s.rumblingPad = source;
        s.rumbleDeadline = Time.realtimeSinceStartupAsDouble + pulse.durationMilliseconds / 1000.0;
        source.SetMotorSpeeds(Mathf.Clamp01(pulse.lowFrequency), Mathf.Clamp01(pulse.highFrequency));
    }

    public static void StopHaptics() { if (instance != null) instance.StopRumble(); }
    void StopRumble()
    {
        if (rumblingPad != null) rumblingPad.ResetHaptics();
        rumblingPad = null;
        rumbleDeadline = 0;
    }
    void DeviceChanged(InputDevice device, InputDeviceChange change)
    {
        var pad = device as Gamepad;
        if (pad == null) return;
        if (change == InputDeviceChange.Removed || change == InputDeviceChange.Disconnected || change == InputDeviceChange.Disabled)
        {
            if (rumblingPad == pad) StopRumble();
            pads.Remove(pad);
            if (activePad == pad) { activePad = null; usingGamepad = false; Suspend(); gripReleased = true; }
        }
        else if (change == InputDeviceChange.Reconnected || change == InputDeviceChange.Enabled) pad.ResetHaptics();
    }

    // Keyboard/mouse gameplay uses the same new-system devices as the regression harness.
    // Both remains enabled for the existing StandaloneInputModule and legacy diagnostics.
    static KeyControl KeyboardControl(KeyCode code)
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return null;
        if (code >= KeyCode.A && code <= KeyCode.Z) return keyboard[(UnityEngine.InputSystem.Key)((int)UnityEngine.InputSystem.Key.A + (int)code - (int)KeyCode.A)];
        if (code >= KeyCode.F1 && code <= KeyCode.F12) return keyboard[(UnityEngine.InputSystem.Key)((int)UnityEngine.InputSystem.Key.F1 + (int)code - (int)KeyCode.F1)];
        switch (code)
        {
            case KeyCode.Space: return keyboard.spaceKey;
            case KeyCode.Escape: return keyboard.escapeKey;
            case KeyCode.LeftArrow: return keyboard.leftArrowKey;
            case KeyCode.RightArrow: return keyboard.rightArrowKey;
            case KeyCode.UpArrow: return keyboard.upArrowKey;
            case KeyCode.DownArrow: return keyboard.downArrowKey;
            default: return null;
        }
    }
    public static bool Key(KeyCode code) { return KeyboardControl(code)?.isPressed ?? false; }
    public static bool KeyDown(KeyCode code) { return KeyboardControl(code)?.wasPressedThisFrame ?? false; }
    static ButtonControl MouseButton(int button)
    {
        var mouse = Mouse.current;
        return mouse == null ? null : button == 0 ? mouse.leftButton : button == 1 ? mouse.rightButton : mouse.middleButton;
    }
    public static bool MouseHeld(int button) { return MouseButton(button)?.isPressed ?? false; }
    public static bool MouseDown(int button) { return MouseButton(button)?.wasPressedThisFrame ?? false; }
    public static bool MouseUp(int button) { return MouseButton(button)?.wasReleasedThisFrame ?? false; }
    public static Vector3 MousePosition { get { return Mouse.current == null ? Vector3.zero : (Vector3)Mouse.current.position.ReadValue(); } }
}
