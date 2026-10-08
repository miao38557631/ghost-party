using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// First playable prototype for "别在这儿蹦跶": exploration -> curtain prank close-up -> guest exit.
/// Runtime-only setup keeps the existing sample scene and project assets intact.
/// </summary>
public sealed class GhostPartyPrototype : MonoBehaviour
{
    public enum Mode { Explore, Closeup }
    public enum PrankStage { CurtainSequence, Complete, Escaped }
    public enum Language { Chinese, English }
    public enum Difficulty { Normal, Easy }
    enum CurtainAction { Tap, AutoLift, AutoApproach, AutoWrapComplete, DragRight, DragDown, SwipeLeft, SwipeRight, WindLeft, Hold, ThrowRightRelease }

    [Serializable]
    sealed class CurtainStepConfig
    {
        public CurtainAction action;
        [Min(1)] public int durationBeats = 1;

        public CurtainStepConfig(CurtainAction action, int durationBeats = 1)
        {
            this.action = action;
            this.durationBeats = Mathf.Max(1, durationBeats);
        }
    }

    // The curtain sequence is data: operation, direction, and occupied beats
    // are kept together so timing, cue icons and performance can be changed in
    // one place. The 19 beat timeline is 18 entries because AutoApproach owns
    // beats 4-5.
    [SerializeField] List<CurtainStepConfig> curtainPlan = BuildDefaultCurtainPlan();

    static List<CurtainStepConfig> BuildDefaultCurtainPlan()
    {
        return new List<CurtainStepConfig>
        {
            new CurtainStepConfig(CurtainAction.Tap),
            new CurtainStepConfig(CurtainAction.Tap),
            new CurtainStepConfig(CurtainAction.AutoLift),
            new CurtainStepConfig(CurtainAction.AutoApproach, 2),
            new CurtainStepConfig(CurtainAction.DragRight),
            new CurtainStepConfig(CurtainAction.DragDown),
            new CurtainStepConfig(CurtainAction.AutoWrapComplete),
            new CurtainStepConfig(CurtainAction.SwipeLeft),
            new CurtainStepConfig(CurtainAction.SwipeRight),
            new CurtainStepConfig(CurtainAction.SwipeLeft),
            new CurtainStepConfig(CurtainAction.SwipeRight),
            new CurtainStepConfig(CurtainAction.SwipeLeft),
            new CurtainStepConfig(CurtainAction.SwipeRight),
            new CurtainStepConfig(CurtainAction.SwipeLeft),
            new CurtainStepConfig(CurtainAction.SwipeRight),
            new CurtainStepConfig(CurtainAction.WindLeft),
            new CurtainStepConfig(CurtainAction.Hold),
            new CurtainStepConfig(CurtainAction.ThrowRightRelease)
        };
    }

    int PlanCount { get { return curtainPlan == null ? 0 : curtainPlan.Count; } }
    CurtainAction PlanAction(int index) { return curtainPlan[index].action; }
    int PlanDuration(int index) { return Mathf.Max(1, curtainPlan[index].durationBeats); }
    int CurrentPlanBeatNumber()
    {
        int beat = 1;
        for (int i = 0; i < curtainStep && i < PlanCount; i++) beat += PlanDuration(i);
        return Mathf.Min(beat, TotalPlanBeats);
    }
    int TotalPlanBeats
    {
        get
        {
            int total = 0;
            for (int i = 0; i < PlanCount; i++) total += PlanDuration(i);
            return Mathf.Max(1, total);
        }
    }
    int CurrentStepDuration { get { return curtainStep < PlanCount ? PlanDuration(curtainStep) : 1; } }
    CurtainAction CurrentAction { get { return curtainStep < PlanCount ? PlanAction(curtainStep) : CurtainAction.Tap; } }
    bool IsAutomaticAction(CurtainAction action)
    {
        return action == CurtainAction.AutoLift || action == CurtainAction.AutoApproach || action == CurtainAction.AutoWrapComplete;
    }

    [Serializable]
    public class HudLayoutSettings
    {
        [Header("Panel")]
        public Vector2 panelAnchor = new Vector2(0.19f, 0.79f);
        public Vector2 panelSize = new Vector2(0.36f, 0.38f);
        [Header("Rows: normalized screen anchor / size / font")]
        public Vector2 titleAnchor = new Vector2(0.19f, 0.945f);
        public Vector2 titleSize = new Vector2(0.32f, 0.05f);
        [Range(10, 40)] public int titleFontSize = 16;
        public Vector2 actionAnchor = new Vector2(0.19f, 0.855f);
        public Vector2 actionSize = new Vector2(0.32f, 0.11f);
        [Range(8, 32)] public int actionFontSize = 12;
        public Vector2 progressAnchor = new Vector2(0.19f, 0.745f);
        public Vector2 progressSize = new Vector2(0.32f, 0.045f);
        [Range(8, 32)] public int progressFontSize = 12;
        public Vector2 promptAnchor = new Vector2(0.19f, 0.675f);
        public Vector2 promptSize = new Vector2(0.32f, 0.06f);
        [Range(8, 36)] public int promptFontSize = 13;
        public Vector2 statusAnchor = new Vector2(0.19f, 0.605f);
        public Vector2 statusSize = new Vector2(0.32f, 0.055f);
        [Range(8, 28)] public int statusFontSize = 11;
        public Vector2 feedbackAnchor = new Vector2(0.19f, 0.515f);
        public Vector2 feedbackSize = new Vector2(0.32f, 0.05f);
        [Range(10, 40)] public int feedbackFontSize = 15;
    }

    [Header("HUD Layout (edit in Inspector)")]
    [SerializeField] HudLayoutSettings hudLayout = new HudLayoutSettings();

    [Header("Language")]
    [SerializeField] Language language = Language.Chinese;
    [SerializeField] Difficulty difficulty = Difficulty.Normal;
    [SerializeField] bool beatAssistEnabled = false;

    [Header("Tuning")]
    [SerializeField] float moveSpeed = 4.5f;
    [SerializeField] float interactionRange = 2.0f;
    [SerializeField] float beatBpm = 112f;
    [SerializeField, Range(0.05f, 1f)] float bgmVolume = 0.34f;
    [SerializeField, Range(0.08f, 0.40f)] float beatTolerance = 0.34f;
    [SerializeField, Range(0.30f, 0.49f)] float easyBeatTolerance = 0.46f;
    [SerializeField, Min(1)] int normalMissesPerEscapeSegment = 2;
    [SerializeField, Min(1)] int easyMissesPerEscapeSegment = 3;
    [SerializeField, Range(-0.15f, 0.15f)] float rhythmCalibrationSeconds = 0f;
    [SerializeField, Range(0.5f, 2f)] float firstInputLeadBeats = 1f;
    [SerializeField, Range(0.05f, 0.50f)] float gestureStartLeadBeats = 0.30f;
    [SerializeField, Range(0.02f, 0.20f)] float curtainGestureThreshold = 0.035f;
    [SerializeField, Range(0.02f, 0.20f)] float easyGestureThreshold = 0.022f;

    Mode mode = Mode.Explore;
    PrankStage stage = PrankStage.CurtainSequence;
    Transform ghost, curtain, guest, guestHead;
    Camera cam;
    Canvas canvas;
    Text prompt, inputCueIcon, status, title, actionHint, progressText, feedback, languageButtonText, difficultyButtonText;
    Button languageButton, difficultyButton;
    Text beatAssistButtonText;
    Image vignette, beatFlash, topHudPanel, bottomHudPanel;
    Canvas worldCueCanvas;
    Image worldCueCircle;
    Text worldCueText;
    Canvas nextWorldCueCanvas;
    Text nextWorldCueText;
    Canvas escapeGaugeCanvas;
    Image escapeGaugeFill;
    Text escapeGaugeText;
    int escapeProgress;
    int consecutiveEmptyBeats;
    [SerializeField] float escapeZoomPerSegment = 0.12f;
    double lastEscapeCountedBeat = double.NaN;
    float feedbackUntil;
    AudioSource music;
    BeatClock beatClock;
    Vector3 exploreGhostPos = new Vector3(-4.6f, 0.38f, -1.5f);
    Vector3 guestHome = new Vector3(2.6f, 0.62f, 1.2f);
    Vector3 curtainHome;
    Vector3 curtainScaleHome;
    Vector3 guestHeadHome;
    Vector3 guestHeadScaleHome;
    Vector3 guestScaleHome = Vector3.one;
    Vector3 guestLaunchOrigin;
    Quaternion guestLaunchRotation;
    Vector3 guestLaunchScale;
    Vector3 curtainLaunchOrigin;
    Quaternion curtainLaunchRotation;
    Vector3 curtainLaunchScale;
    double curtainTargetBeat;
    int curtainStep;
    bool curtainHeld;
    bool gestureArmed;
    bool gestureStartCaptured;
    bool gestureRetryPending;
    float gestureRetryReadyAt;
    CurtainAction lastPerformedAction;
    float actionImpactUntil;
    Vector3 curtainGuestSpot = new Vector3(2.6f, 0.62f, 2.9f);
    float stageStartedAt;
    Vector3 gestureStart;
    float closeupPulse;
    float guestLeaveAt = -1f;
    // Camera feedback stays separate from game judgement: a failed gesture only
    // previews its direction, while a successful beat is what earns the punch.
    int renderedHudStep = -1;
    int renderedCueStep = -1;
    double renderedTargetBeat = double.NaN;
    bool renderedGestureArmed;
    string renderedRetryMessage;
    Language renderedHudLanguage;
    Language renderedCueLanguage;
    PrankStage renderedHudStage;
    CurtainAction cameraImpactAction;
    float cameraImpactStartedAt = -1f;
    float cameraZoomStartOffset;
    float appliedCameraZoomOffset;
    float appliedPressureZoomOffset;
    float pressureZoomTarget;
    float pressureZoomStart;
    float pressureZoomStartedAt = -1f;
    CurtainAction cameraPanAction;
    float cameraPanStartedAt = -1f;
    Vector3 cameraPanVelocity;
    bool cameraGestureStarted;
    float cameraReboundStartedAt = -1f;
    Vector3 cameraReboundOffset;
    Vector3 appliedCameraActionOffset;
    float worldCueCloseupBlend;
    string retryMessage = "";
    // Temporary diagnostics: fixed-size sampling, no per-frame strings or logging.
    [SerializeField] bool motionDiagnosticsEnabled = true;
    readonly MotionSample[] motionSamples = new MotionSample[8192];
    int motionWriteIndex, motionSampleCount, motionEvents;
    bool motionSessionActive, motionFinishPending;
    float motionSessionStartedAt;
    string motionSessionId;
    int motionPart;
    Vector3 previousDiagnosticMouse;
    bool diagnosticMouseInitialized;
    double gameplayCpuMs, cameraCpuMs;
    Task<string> motionExportTask;

    [Flags]
    enum MotionEvent { Press = 1, Release = 2, PanStart = 4, Armed = 8, Success = 16, Failure = 32, SessionStart = 64, SessionEnd = 128 }

    struct MotionSample
    {
        public int frame, step, action, events;
        public bool held, armed, closeup, edge;
        public float time, frameMs, scaledMs, zoom;
        public double beat, target, gameplayMs, cameraMs;
        public Vector3 mouse, mouseDelta, camera, offset, guest, curtain;
        public Vector2 mouseAxes;
    }

    static GhostPartyPrototype instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindObjectOfType<GhostPartyPrototype>() != null) return;
        var go = new GameObject("GhostPartyPrototype_Runtime");
        instance = go.AddComponent<GhostPartyPrototype>();
        DontDestroyOnLoad(go);
    }

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        Application.targetFrameRate = 120;
        if (curtainPlan == null || curtainPlan.Count == 0) curtainPlan = BuildDefaultCurtainPlan();
        BuildWorld();
        BuildAudio();
        BuildHud();
        SetExplorePrompt();
    }

    void Update()
    {
        long diagnosticStart = System.Diagnostics.Stopwatch.GetTimestamp();
        motionEvents = 0;
        if (Input.GetKeyDown(KeyCode.F9))
        {
            if (motionDiagnosticsEnabled) FinishMotionSession();
            motionDiagnosticsEnabled = !motionDiagnosticsEnabled;
            diagnosticMouseInitialized = false;
            Debug.Log("Motion diagnostics: " + (motionDiagnosticsEnabled ? "ON (automatic full-round capture)" : "OFF"));
        }
        if (motionExportTask != null && motionExportTask.IsCompleted)
        {
            if (motionExportTask.IsFaulted) Debug.LogWarning("Motion capture export failed: " + motionExportTask.Exception.GetBaseException().Message);
            else Debug.Log("Motion capture saved: " + motionExportTask.Result);
            motionExportTask = null;
        }
        if (motionDiagnosticsEnabled)
        {
            if (Input.GetMouseButtonDown(0)) motionEvents |= (int)MotionEvent.Press;
            if (Input.GetMouseButtonUp(0)) motionEvents |= (int)MotionEvent.Release;
        }
        if (beatClock != null) beatClock.Tick();
        UpdateFeedback();
        UpdateBeatCue();
        if (Input.GetKeyDown(KeyCode.L)) ToggleLanguage();
        HandleDeveloperShortcuts();
        if (mode == Mode.Explore) UpdateExplore();
        else UpdateCloseup();
        gameplayCpuMs = ElapsedDiagnosticMs(diagnosticStart);
    }

    void LateUpdate()
    {
        // Compose the camera once, after gameplay has finished moving the scene.
        long diagnosticStart = System.Diagnostics.Stopwatch.GetTimestamp();
        UpdateCamera();
        cameraCpuMs = ElapsedDiagnosticMs(diagnosticStart);
        UpdateWorldCue();
        UpdateEscapeGauge();
        SampleMotionDiagnostics();
    }

    static double ElapsedDiagnosticMs(long started)
    {
        return (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    }

    void SampleMotionDiagnostics()
    {
        if (!motionDiagnosticsEnabled || cam == null) return;
        if (!motionSessionActive && mode == Mode.Closeup) StartMotionSession();
        if (!motionSessionActive) return;
        if (motionFinishPending) motionEvents |= (int)MotionEvent.SessionEnd;
        Vector3 mouse = Input.mousePosition;
        motionSamples[motionWriteIndex] = new MotionSample
        {
            frame = Time.frameCount, time = Time.unscaledTime,
            frameMs = Time.unscaledDeltaTime * 1000f, scaledMs = Time.deltaTime * 1000f,
            step = curtainStep, action = stage == PrankStage.CurtainSequence && curtainStep < PlanCount ? (int)PlanAction(curtainStep) : -1,
            closeup = mode == Mode.Closeup, events = motionEvents, held = curtainHeld, armed = gestureArmed,
            beat = beatClock == null ? 0.0 : beatClock.BeatIndex, target = curtainTargetBeat,
            mouse = mouse, mouseDelta = diagnosticMouseInitialized ? mouse - previousDiagnosticMouse : Vector3.zero,
            mouseAxes = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")),
            edge = mouse.x <= 1f || mouse.y <= 1f || mouse.x >= Screen.width - 1f || mouse.y >= Screen.height - 1f,
            camera = cam.transform.position, offset = Quaternion.Inverse(cam.transform.rotation) * appliedCameraActionOffset,
            zoom = cam.orthographicSize, guest = guest == null ? Vector3.zero : guest.position,
            curtain = curtain == null ? Vector3.zero : curtain.position,
            gameplayMs = gameplayCpuMs, cameraMs = cameraCpuMs
        };
        previousDiagnosticMouse = mouse;
        diagnosticMouseInitialized = true;
        motionWriteIndex++;
        motionSampleCount++;
        // Long rounds are saved in numbered chunks, never overwritten by a ring.
        if (motionSampleCount == motionSamples.Length) ExportMotionCapture();
        if (motionFinishPending) FinishMotionSession();
    }

    void StartMotionSession()
    {
        if (!motionDiagnosticsEnabled) return;
        FinishMotionSession();
        motionSessionActive = true;
        motionFinishPending = false;
        motionSessionStartedAt = Time.unscaledTime;
        motionSessionId = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)
            + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        motionPart = 0;
        diagnosticMouseInitialized = false;
        motionEvents |= (int)MotionEvent.SessionStart;
    }

    void FinishMotionSession()
    {
        if (!motionSessionActive) return;
        ExportMotionCapture();
        motionSessionActive = false;
        motionFinishPending = false;
    }

    void ExportMotionCapture()
    {
        if (!motionSessionActive || motionSampleCount == 0) return;
        float sessionStart = motionSessionStartedAt;
        var snapshot = new MotionSample[motionSampleCount];
        Array.Copy(motionSamples, snapshot, motionSampleCount);
        string directory = Path.Combine(Application.dataPath, "..", "Logs", "MotionDiagnostics");
        string path = Path.Combine(directory, "round-" + motionSessionId + "-part" + (++motionPart).ToString("D3", CultureInfo.InvariantCulture) + ".csv");
        motionWriteIndex = motionSampleCount = 0;
        Task<string> previousExport = motionExportTask;
        // Formatting and disk I/O happen only after the capture, off the game thread.
        // The worker uses copied values and no Unity APIs.
        motionExportTask = Task.Run(async () =>
        {
            // Serialize background writes without blocking the next round.
            // A failed earlier write must not discard this later capture.
            if (previousExport != null)
            {
                try { await previousExport.ConfigureAwait(false); }
                catch (Exception) { /* Each part has its own file; still save this one. */ }
            }
            Directory.CreateDirectory(directory);
            var csv = new StringBuilder(65536);
            csv.AppendLine("# automatic full-round capture; numbered parts belong to the same round; relative_time_s is seconds since round start");
            csv.AppendLine("# events: 1=press,2=release,4=pan_start,8=armed,16=success,32=failure,64=round_start,128=round_end; values combine as bit flags");
            csv.AppendLine("# action: -1=none,0=tap,1=auto_lift,2=auto_approach,3=drag_right,4=drag_down,5=swipe_left,6=swipe_right,7=wind_left,8=throw_right; times in ms; positions in Unity world units");
            csv.AppendLine("frame,relative_time_s,frame_ms,scaled_ms,closeup,step,action,events,held,armed,beat,target,mouse_x,mouse_y,mouse_dx,mouse_dy,mouse_axis_x,mouse_axis_y,cursor_edge,camera_x,camera_y,camera_z,pan_x,pan_y,ortho_size,guest_x,guest_y,guest_z,curtain_x,curtain_y,curtain_z,gameplay_cpu_ms,camera_cpu_ms");
            foreach (MotionSample s in snapshot)
            {
                csv.AppendFormat(CultureInfo.InvariantCulture,
                    "{0},{1:F6},{2:F4},{3:F4},{4},{5},{6},{7},{8},{9},{10:F6},{11:F6},{12:F3},{13:F3},{14:F3},{15:F3},{16:F4},{17:F4},{18},{19:F6},{20:F6},{21:F6},{22:F6},{23:F6},{24:F6},{25:F6},{26:F6},{27:F6},{28:F6},{29:F6},{30:F6},{31:F6},{32:F6}\n",
                    s.frame, s.time - sessionStart, s.frameMs, s.scaledMs, s.closeup ? 1 : 0, s.step, s.action, s.events,
                    s.held ? 1 : 0, s.armed ? 1 : 0, s.beat, s.target, s.mouse.x, s.mouse.y, s.mouseDelta.x, s.mouseDelta.y,
                    s.mouseAxes.x, s.mouseAxes.y, s.edge ? 1 : 0, s.camera.x, s.camera.y, s.camera.z,
                    s.offset.x, s.offset.y, s.zoom, s.guest.x, s.guest.y, s.guest.z,
                    s.curtain.x, s.curtain.y, s.curtain.z, s.gameplayMs, s.cameraMs);
            }
            File.WriteAllText(path, csv.ToString(), new UTF8Encoding(false));
            return Path.GetFullPath(path);
        });
    }

    void OnDisable()
    {
        // Stopping Play mode or disabling the component preserves a partial round.
        FinishMotionSession();
        if (motionExportTask != null)
        {
            try { motionExportTask.GetAwaiter().GetResult(); }
            catch (Exception exception) { Debug.LogWarning("Motion capture export failed: " + exception.Message); }
        }
    }

    void BuildWorld()
    {
        cam = Camera.main;
        if (cam == null)
        {
            var cameraGo = new GameObject("Main Camera");
            cam = cameraGo.AddComponent<Camera>();
            cameraGo.tag = "MainCamera";
            cameraGo.AddComponent<AudioListener>();
        }
        // High, diagonal orthographic camera: a 2.5D room view inspired by isometric narrative RPGs.
        cam.orthographic = true;
        cam.orthographicSize = 6.4f;
        cam.transform.position = new Vector3(9.0f, 10.5f, -12.0f);
        cam.transform.rotation = Quaternion.LookRotation(new Vector3(0, 0.5f, 1.0f) - cam.transform.position, Vector3.up);
        cam.backgroundColor = new Color(0.055f, 0.07f, 0.12f);

        // Room plane uses X/Z for floor movement and Y for height.
        MakeRect("BackWall", new Vector3(0, 3.2f, 4.4f), new Vector3(16, 6.4f, 0.2f), new Color(0.22f, 0.16f, 0.20f));
        MakeRect("LeftWall", new Vector3(-7.9f, 3.2f, 0.4f), new Vector3(0.2f, 6.4f, 8.0f), new Color(0.18f, 0.14f, 0.18f));
        MakeRect("Floor", new Vector3(0, -0.12f, 0.4f), new Vector3(16, 0.24f, 8.2f), new Color(0.33f, 0.22f, 0.18f));
        MakeRect("Rug", new Vector3(0, 0.03f, 0.6f), new Vector3(9, 0.08f, 5.2f), new Color(0.16f, 0.21f, 0.25f));
        MakeRect("Window", new Vector3(4.0f, 3.5f, 4.25f), new Vector3(3.4f, 3.3f, 0.15f), new Color(0.08f, 0.16f, 0.26f));
        curtain = MakeRect("Curtain", new Vector3(2.6f, 3.4f, 4.05f), new Vector3(1.2f, 3.6f, 0.25f), new Color(0.54f, 0.11f, 0.22f)).transform;
        MakeRect("Sofa", new Vector3(-1.6f, 0.68f, 1.6f), new Vector3(3.2f, 1.4f, 1.6f), new Color(0.27f, 0.33f, 0.40f));
        MakeRect("Table", new Vector3(2.0f, 0.72f, -0.55f), new Vector3(2.2f, 0.18f, 1.25f), new Color(0.38f, 0.24f, 0.15f));
        MakeRect("TableLeg", new Vector3(2.0f, 0.25f, -0.55f), new Vector3(0.18f, 0.65f, 0.3f), new Color(0.25f, 0.15f, 0.10f));
        guest = MakeCircle("Guest", guestHome, 0.62f, new Color(0.84f, 0.50f, 0.38f)).transform;
        guestHead = MakeCircle("GuestHead", guestHome + new Vector3(0, 0.88f, 0), 0.42f, new Color(0.94f, 0.70f, 0.52f)).transform;
        curtainHome = curtain.position;
        curtainScaleHome = curtain.localScale;
        guestHeadHome = guestHead.position;
        guestHeadScaleHome = guestHead.localScale;
        guestScaleHome = guest.localScale;
        ghost = MakeCircle("InvisibleGhost", exploreGhostPos, 0.38f, new Color(0.48f, 0.93f, 0.95f, 0.45f)).transform;
        var glow = ghost.gameObject.AddComponent<Light>(); glow.type = LightType.Point; glow.color = new Color(0.2f, 0.9f, 1f); glow.range = 2.5f; glow.intensity = 0.8f;
        SetLayerRecursive(ghost.gameObject, 0);
    }
    void BuildAudio()
    {
        var go = new GameObject("BGM_BeatClock");
        go.transform.SetParent(transform);
        music = go.AddComponent<AudioSource>();
        music.loop = true;
        music.playOnAwake = false;
        music.spatialBlend = 0f;
        music.volume = bgmVolume;

        // 8 bars / 32 quarter notes: the clip length is an exact multiple of the same beat clock.
        const int sampleRate = 44100;
        const int beatsPerLoop = 32;
        double requestedSecondsPerBeat = 60.0 / Math.Max(1f, beatBpm);
        int samples = Mathf.RoundToInt((float)(requestedSecondsPerBeat * beatsPerLoop * sampleRate));
        // The clip must contain an integer number of samples. Drive both synthesis and
        // judging from the quantized duration so the beat grid cannot drift on loop.
        double secondsPerBeat = samples / (double)(beatsPerLoop * sampleRate);
        var clip = AudioClip.Create($"Prototype_BGM_8bar_{60.0 / secondsPerBeat:0.##}BPM", samples, 1, sampleRate, false);
        var data = new float[samples];
        float[] bassNotes = { 55f, 55f, 65.41f, 73.42f, 55f, 55f, 49f, 65.41f };
        float[] padRoots = { 110f, 130.81f, 146.83f, 123.47f };

        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)sampleRate;
            float beatPosition = t / (float)secondsPerBeat;
            int beatIndex = Mathf.FloorToInt(beatPosition);
            float beatPhase = beatPosition - beatIndex;
            float beatTime = beatPhase * (float)secondsPerBeat;
            int barBeat = beatIndex % 4;
            int barIndex = (beatIndex / 4) % 8;

            // Four-on-the-floor kick. The first beat of every bar is accented.
            float kickEnv = Mathf.Exp(-beatTime * 15f);
            float kickFreq = Mathf.Lerp(58f, 38f, Mathf.Clamp01(beatTime * 9f));
            float kick = Mathf.Sin(2f * Mathf.PI * kickFreq * beatTime) * kickEnv * (barBeat == 0 ? 0.52f : 0.38f);

            // Snare/clap on beats 2 and 4.
            float snare = 0f;
            if (barBeat == 1 || barBeat == 3)
            {
                float snareEnv = Mathf.Exp(-beatTime * 22f);
                float noise = DeterministicNoise(i);
                snare = (noise * 0.22f + Mathf.Sin(2f * Mathf.PI * 185f * beatTime) * 0.08f) * snareEnv;
            }

            // Bright eighth-note hi-hat so the beat remains audible during close-up input.
            float eighthPosition = beatPosition * 2f;
            float eighthPhase = eighthPosition - Mathf.Floor(eighthPosition);
            float hatTime = eighthPhase * (float)secondsPerBeat * 0.5f;
            float hatEnv = Mathf.Exp(-hatTime * 55f);
            float hat = DeterministicNoise(i + Mathf.FloorToInt(eighthPosition)) * hatEnv * 0.055f;
            if (Mathf.FloorToInt(eighthPosition) % 2 == 0) hat *= 1.18f;

            // Repeating bass pulse, one note per beat.
            float bassFreq = bassNotes[beatIndex % bassNotes.Length];
            float bassEnv = 0.30f + 0.70f * Mathf.Exp(-beatTime * 5f);
            float bass = Mathf.Sin(2f * Mathf.PI * bassFreq * t) * bassEnv * 0.13f;

            // Soft four-chord pad keeps it musical without masking the transients.
            float padRoot = padRoots[barIndex % padRoots.Length];
            float pad = (Mathf.Sin(2f * Mathf.PI * padRoot * t) * 0.018f
                       + Mathf.Sin(2f * Mathf.PI * padRoot * 1.25f * t) * 0.012f
                       + Mathf.Sin(2f * Mathf.PI * padRoot * 1.5f * t) * 0.009f);

            float downbeatAccent = barBeat == 0 ? Mathf.Exp(-beatTime * 12f) * 0.055f : 0f;
            data[i] = Mathf.Clamp(kick + snare + hat + bass + pad + downbeatAccent, -0.85f, 0.85f);
        }

        clip.SetData(data, 0);
        music.clip = clip;
        double outputLatency = EstimateOutputLatencySeconds();
        double heardBeatCompensation = Math.Max(0.0, outputLatency + rhythmCalibrationSeconds);
        beatClock = new BeatClock(secondsPerBeat, heardBeatCompensation);
        double startDsp = AudioSettings.dspTime + 0.1;
        music.PlayScheduled(startDsp);
        beatClock.Start(startDsp);
        Debug.Log($"Rhythm grid: {60.0 / secondsPerBeat:0.##} BPM, {secondsPerBeat * 1000.0:0.0} ms/beat, " +
                  $"heard-output compensation {heardBeatCompensation * 1000.0:0.0} ms.");
    }

    static double EstimateOutputLatencySeconds()
    {
        AudioSettings.GetDSPBufferSize(out int bufferLength, out int bufferCount);
        int outputRate = Math.Max(1, AudioSettings.outputSampleRate);
        return Math.Max(0, bufferLength) * (double)Math.Max(1, bufferCount) / outputRate;
    }

    static float DeterministicNoise(int index)
    {
        float value = Mathf.Sin(index * 12.9898f) * 43758.5453f;
        return (value - Mathf.Floor(value)) * 2f - 1f;
    }

    void BuildHud()
    {
        var canvasGo = new GameObject("PrototypeHUD"); canvas = canvasGo.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 50;
        canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasGo.AddComponent<GraphicRaycaster>();
        if (FindObjectOfType<EventSystem>() == null) { var es = new GameObject("EventSystem"); es.AddComponent<EventSystem>(); es.AddComponent<StandaloneInputModule>(); }
        // Compact top-left HUD: keep the play space visible and avoid covering the room.
        topHudPanel = MakePanel("TopHudPanel", hudLayout.panelAnchor, hudLayout.panelSize, new Color(0.02f, 0.03f, 0.08f, 0.78f));
        bottomHudPanel = MakePanel("BottomHudPanel", hudLayout.panelAnchor, hudLayout.panelSize, new Color(0.02f, 0.03f, 0.08f, 0.78f));
        topHudPanel.transform.SetAsFirstSibling();
        bottomHudPanel.gameObject.SetActive(false);
        title = MakeText("Title", hudLayout.titleAnchor, hudLayout.titleSize, hudLayout.titleFontSize, TextAnchor.MiddleLeft, new Color(0.9f, 0.95f, 1f));
        actionHint = MakeText("ActionHint", hudLayout.actionAnchor, hudLayout.actionSize, hudLayout.actionFontSize, TextAnchor.UpperLeft, new Color(1f, 0.86f, 0.46f));
        progressText = MakeText("Progress", hudLayout.progressAnchor, hudLayout.progressSize, hudLayout.progressFontSize, TextAnchor.MiddleLeft, new Color(0.95f, 0.95f, 1f));
        prompt = MakeText("Prompt", hudLayout.promptAnchor, hudLayout.promptSize, hudLayout.promptFontSize, TextAnchor.MiddleLeft, Color.white);
        inputCueIcon = MakeText("InputCueIcon", new Vector2(0.36f, hudLayout.promptAnchor.y), new Vector2(0.055f, 0.075f), 23, TextAnchor.MiddleCenter, new Color(1f, 0.86f, 0.46f));
        BuildWorldCue();
        BuildEscapeGauge();
        status = MakeText("Status", hudLayout.statusAnchor, hudLayout.statusSize, hudLayout.statusFontSize, TextAnchor.UpperLeft, new Color(0.72f, 0.85f, 0.92f));
        feedback = MakeText("Feedback", hudLayout.feedbackAnchor, hudLayout.feedbackSize, hudLayout.feedbackFontSize, TextAnchor.MiddleLeft, Color.white);
        feedback.text = "";
        // Top-center beat response: the circle sprite is stretched into a
        // long line as the beat progresses, like an audio-reactive wallpaper.
        beatFlash = MakePanel("BeatFlash", new Vector2(0.50f, 0.915f), new Vector2(0.052f, 0.052f), new Color(1f, 0.75f, 0.18f, 0.08f));
        beatFlash.sprite = CreateCircleSprite();
        beatFlash.raycastTarget = false;
        beatFlash.transform.SetAsLastSibling();
        beatFlash.gameObject.SetActive(beatAssistEnabled);
        languageButton = MakeLanguageButton();
        difficultyButton = MakeDifficultyButton();
        MakeBeatAssistButton();
        vignette = MakePanel("Vignette", new Vector2(0, 0), new Vector2(1, 1), new Color(0.05f, 0.02f, 0.12f, 0.0f));
        vignette.rectTransform.anchorMin = Vector2.zero;
        vignette.rectTransform.anchorMax = Vector2.one;
        vignette.rectTransform.offsetMin = vignette.rectTransform.offsetMax = Vector2.zero;
        vignette.transform.SetAsFirstSibling();
    }

    void BuildWorldCue()
    {
        var go = new GameObject("CurtainWorldCue");
        worldCueCanvas = go.AddComponent<Canvas>();
        worldCueCanvas.renderMode = RenderMode.WorldSpace;
        worldCueCanvas.worldCamera = cam;
        worldCueCanvas.sortingOrder = 100;
        var root = go.GetComponent<RectTransform>();
        root.sizeDelta = new Vector2(100f, 100f);
        go.transform.localScale = Vector3.one * 0.006f;

        var bg = new GameObject("WhiteCircle");
        bg.transform.SetParent(go.transform, false);
        var bgRt = bg.AddComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one; bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;
        worldCueCircle = bg.AddComponent<Image>();
        worldCueCircle.sprite = CreateCircleSprite();
        worldCueCircle.color = Color.white;
        worldCueCircle.raycastTarget = false;

        var label = new GameObject("CueLabel");
        label.transform.SetParent(go.transform, false);
        var labelRt = label.AddComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero; labelRt.anchorMax = Vector2.one; labelRt.offsetMin = labelRt.offsetMax = Vector2.zero;
        worldCueText = label.AddComponent<Text>();
        worldCueText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        worldCueText.alignment = TextAnchor.MiddleCenter;
        worldCueText.fontSize = 34;
        worldCueText.fontStyle = FontStyle.Bold;
        worldCueText.color = Color.black;
        worldCueText.raycastTarget = false;
        // A separate sibling canvas prevents the preview inheriting the beat pop.
        var next = Instantiate(go);
        next.name = "CurtainNextBeatCue";
        nextWorldCueCanvas = next.GetComponent<Canvas>();
        nextWorldCueCanvas.sortingOrder = 99;
        nextWorldCueText = next.GetComponentInChildren<Text>();
        nextWorldCueText.fontSize = 41;
        var nextGroup = next.AddComponent<CanvasGroup>();
        nextGroup.alpha = 0.7f;
        nextGroup.interactable = false;
        nextGroup.blocksRaycasts = false;
        next.SetActive(false);
        go.SetActive(false);
    }

    void BuildEscapeGauge()
    {
        var go = new GameObject("GuestEscapeGauge");
        escapeGaugeCanvas = go.AddComponent<Canvas>();
        escapeGaugeCanvas.renderMode = RenderMode.WorldSpace;
        escapeGaugeCanvas.worldCamera = cam;
        escapeGaugeCanvas.sortingOrder = 100;
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(80f, 80f);
        go.transform.localScale = Vector3.one * 0.006f;
        Sprite ring = CreateEscapeRingSprite();
        foreach (bool foreground in new[] { false, true })
        {
            var part = new GameObject(foreground ? "Progress" : "Track");
            part.transform.SetParent(go.transform, false);
            var rect = part.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = part.AddComponent<Image>();
            image.sprite = ring;
            image.raycastTarget = false;
            image.color = foreground ? new Color(0.35f, 1f, 0.28f, 1f) : new Color(0.08f, 0.20f, 0.10f, 0.85f);
            if (foreground)
            {
                escapeGaugeFill = image;
                image.type = Image.Type.Filled;
                image.fillMethod = Image.FillMethod.Radial360;
                image.fillOrigin = (int)Image.Origin360.Top;
                image.fillClockwise = true;
                image.fillAmount = 0f;
            }
        }
        var label = new GameObject("EscapeCount");
        label.transform.SetParent(go.transform, false);
        var labelRect = label.AddComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
        escapeGaugeText = label.AddComponent<Text>();
        escapeGaugeText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        escapeGaugeText.fontSize = 22;
        escapeGaugeText.fontStyle = FontStyle.Bold;
        escapeGaugeText.alignment = TextAnchor.MiddleCenter;
        escapeGaugeText.color = new Color(0.55f, 1f, 0.45f);
        escapeGaugeText.raycastTarget = false;
        escapeGaugeText.text = "0/4";
        go.SetActive(false);
    }

    void UpdateEscapeGauge()
    {
        if (escapeGaugeCanvas == null || guest == null || cam == null) return;
        bool visible = mode == Mode.Closeup && (stage == PrankStage.CurtainSequence || stage == PrankStage.Escaped);
        if (escapeGaugeCanvas.gameObject.activeSelf != visible) escapeGaugeCanvas.gameObject.SetActive(visible);
        if (!visible) return;
        escapeGaugeCanvas.transform.position = guest.position + cam.transform.up * 1.3f + cam.transform.right * 0.45f;
        escapeGaugeCanvas.transform.rotation = Quaternion.LookRotation(escapeGaugeCanvas.transform.position - cam.transform.position, Vector3.up);
        escapeGaugeFill.fillAmount = Mathf.MoveTowards(escapeGaugeFill.fillAmount, escapeProgress / 4f, Time.deltaTime * 2f);
    }

    void ResetEscapeProgress()
    {
        escapeProgress = 0;
        consecutiveEmptyBeats = 0;
        lastEscapeCountedBeat = double.NaN;
        if (escapeGaugeFill != null) escapeGaugeFill.fillAmount = 0f;
        if (escapeGaugeText != null) escapeGaugeText.text = "0/4";
    }

    bool RegisterEmptyBeat()
    {
        if (stage != PrankStage.CurtainSequence || curtainStep >= PlanCount) return true;
        CurtainAction action = PlanAction(curtainStep);
        if (IsAutomaticAction(action)) return true;
        if (lastEscapeCountedBeat == curtainTargetBeat) return true;
        lastEscapeCountedBeat = curtainTargetBeat;
        // Account for whole empty beat windows skipped by an unusually long frame.
        int missed = 1 + Math.Max(0, (int)Math.Floor(beatClock.BeatIndex - curtainTargetBeat - BeatTolerance));
        consecutiveEmptyBeats = Math.Min(MissesPerEscapeSegment * 4, consecutiveEmptyBeats + missed);
        escapeProgress = consecutiveEmptyBeats / MissesPerEscapeSegment;
        if (escapeGaugeText != null) escapeGaugeText.text = escapeProgress + "/4";
        if (escapeProgress < 4) return true;
        EscapeCurtain();
        return false;
    }

    void EscapeCurtain()
    {
        guestLaunchOrigin = guest.position;
        guestLaunchRotation = guest.rotation;
        guestLaunchScale = guest.localScale;
        curtainLaunchOrigin = curtain.position;
        curtainLaunchRotation = curtain.rotation;
        curtainLaunchScale = curtain.localScale;
        ResetGestureState();
        stage = PrankStage.Escaped;
        stageStartedAt = Time.time;
        guestLeaveAt = Time.time + 0.6f;
        retryMessage = "";
        if (inputCueIcon != null) inputCueIcon.text = "";
        ShowFeedback(T("NPC 挣脱了！", "NPC broke free!"), new Color(1f, 0.65f, 0.3f));
        UpdateStageHud();
    }

    void UpdateWorldCue()
    {
        if (worldCueCanvas == null || curtain == null || cam == null) return;
        bool visible = false;
        string icon = "";
        if (mode == Mode.Explore)
        {
            Vector2 ghostFloor = new Vector2(ghost.position.x, ghost.position.z);
            Vector2 curtainFloor = new Vector2(curtain.position.x, curtain.position.z);
            visible = Vector2.Distance(ghostFloor, curtainFloor) < interactionRange;
            icon = "E";
        }
        else if (mode == Mode.Closeup && stage == PrankStage.CurtainSequence && curtainStep < PlanCount)
        {
            visible = true;
            icon = CueIcon(PlanAction(curtainStep));
        }

        if (worldCueCanvas.gameObject.activeSelf != visible) worldCueCanvas.gameObject.SetActive(visible);
        bool nextVisible = visible && mode == Mode.Closeup && stage == PrankStage.CurtainSequence
            && curtainStep < PlanCount && !IsAutomaticAction(PlanAction(curtainStep))
            && curtainStep + 1 < PlanCount && !IsAutomaticAction(PlanAction(curtainStep + 1));
        if (nextWorldCueCanvas != null && nextWorldCueCanvas.gameObject.activeSelf != nextVisible)
            nextWorldCueCanvas.gameObject.SetActive(nextVisible);
        if (!visible) return;
        // Follow the framing transition, not individual swipes or tap zooms.
        // Camera-up keeps this extra displacement downward on screen.
        worldCueCanvas.transform.position = curtainHome + new Vector3(-0.92f, -0.18f, -0.32f)
            - cam.transform.up * (0.22f * worldCueCloseupBlend);
        // Face the camera with the canvas front side; the opposite billboard
        // direction makes text and arrows appear horizontally mirrored.
        worldCueCanvas.transform.rotation = Quaternion.LookRotation(worldCueCanvas.transform.position - cam.transform.position, Vector3.up);
        worldCueText.fontSize = mode == Mode.Explore ? 51 : 41;
        worldCueCircle.color = Color.white;
        // Keep the action readable throughout the beat; the whole badge pops
        // on the musical pulse, independently of whether the player acts.
        float beatPhase = beatClock == null ? 1f : Mathf.Repeat((float)beatClock.BeatIndex, 1f);
        float pop = beatPhase < 0.20f ? 1f - Mathf.SmoothStep(0f, 1f, beatPhase / 0.20f) : 0f;
        worldCueCanvas.transform.localScale = Vector3.one * (0.006f * (1f + 0.55f * pop));
        if (worldCueText.text != icon) worldCueText.text = icon;
        if (nextVisible && nextWorldCueCanvas != null)
        {
            nextWorldCueCanvas.transform.position = worldCueCanvas.transform.position
                - cam.transform.right * 0.62f - cam.transform.up * 0.55f;
            nextWorldCueCanvas.transform.rotation = worldCueCanvas.transform.rotation;
            nextWorldCueCanvas.transform.localScale = Vector3.one * 0.0042f;
            string nextIcon = CueIcon(PlanAction(curtainStep + 1));
            if (nextWorldCueText.text != nextIcon) nextWorldCueText.text = nextIcon;
        }
    }

    void UpdateExplore()
    {
        UpdateCurtainInvitation();
        Vector3 input = new Vector3(Input.GetAxisRaw("Horizontal"), 0, Input.GetAxisRaw("Vertical"));
        if (input.sqrMagnitude > 1) input.Normalize();
        ghost.position += input * moveSpeed * Time.deltaTime;
        ghost.position = new Vector3(Mathf.Clamp(ghost.position.x, -6.6f, 6.6f), 0.38f, Mathf.Clamp(ghost.position.z, -3.0f, 3.7f));
        Vector2 ghostFloor = new Vector2(ghost.position.x, ghost.position.z);
        Vector2 curtainFloor = new Vector2(curtain.position.x, curtain.position.z);
        if (Vector2.Distance(ghostFloor, curtainFloor) < interactionRange && (Input.GetKeyDown(KeyCode.E) || GameplayMousePressed())) BeginPrank();
        if (guestLeaveAt > 0 && Time.time > guestLeaveAt) { guest.position = Vector3.MoveTowards(guest.position, new Vector3(8f, guest.position.y, guest.position.z), Time.deltaTime * 2.5f); }
        if (guestHead != null) guestHead.position = guest.position + new Vector3(0, 0.88f, 0);
        if (guest.position.x > 7.5f)
        {
            guest.position = guestHome;
            guest.rotation = Quaternion.identity;
            guest.localScale = guestScaleHome;
            if (guestHead != null) guestHead.localScale = guestHeadScaleHome;
            guestLeaveAt = -1;
        }
        title.text = T("客厅 · 探索", "Living Room · Explore");
        actionHint.text = T("移动：WASD / 方向键\n互动：靠近窗帘后按 E", "Move: WASD / Arrow Keys\nInteract: press E near the curtain");
        progressText.text = T("当前目标：找到窗帘和客人", "Objective: find the curtain and guest");
        status.text = T("靠近右侧窗帘开始恶作剧", "Approach the curtain to start the prank");
    }

    void UpdateCurtainInvitation()
    {
        if (curtain == null || beatClock == null) return;
        float phase = Mathf.Repeat((float)beatClock.BeatIndex, 1f);
        // A small beat-synced lift suggests an interactable prank, without
        // moving its interaction origin or affecting the close-up sequence.
        float tease = phase < 0.32f ? ActionEnvelope(phase / 0.32f) : 0f;
        curtain.position = curtainHome + Vector3.up * (0.065f * tease);
        curtain.rotation = Quaternion.Euler(0f, 0f, -2.5f * tease);
        curtain.localScale = Vector3.Scale(curtainScaleHome, new Vector3(1f, 1f - 0.015f * tease, 1f));
    }

    void BeginPrank()
    {
        StartMotionSession();
        ResetEscapeProgress();
        mode = Mode.Closeup; stage = PrankStage.CurtainSequence; stageStartedAt = Time.time; retryMessage = "";
        curtainStep = 0;
        renderedHudStep = -1;
        renderedCueStep = -1;
        curtainTargetBeat = beatClock.NextWholeBeat(firstInputLeadBeats);
        ResetGestureState();
        curtain.position = curtainHome;
        curtain.rotation = Quaternion.identity;
        curtain.localScale = curtainScaleHome;
        guest.position = guestHome;
        guest.rotation = Quaternion.identity;
        guest.localScale = guestScaleHome;
        ghost.position = new Vector3(-2.7f, 0.2f, -1f); vignette.color = new Color(0.05f, 0.02f, 0.12f, 0.35f);
        title.text = T("窗帘恶作剧 · 特写", "Curtain Prank · Close-up");
        ShowFeedback(T("进入特写", "Entering close-up"), new Color(0.55f, 0.95f, 1f));
    }

    void UpdateCloseup()
    {
        closeupPulse += Time.deltaTime;
        UpdateStageHud();
        if (stage == PrankStage.CurtainSequence) HandleCurtainSequence();
        else if ((stage == PrankStage.Complete || stage == PrankStage.Escaped) && Time.time - stageStartedAt > 1.8f) ReturnToExplore();
        UpdatePrankVisuals();
        if (Input.GetKeyDown(KeyCode.Escape)) ReturnToExplore();
    }

    void HandleCurtainSequence()
    {
        if (curtainStep >= PlanCount) { CompletePrank(); return; }
        CurtainAction action = PlanAction(curtainStep);
        UpdateCurtainCue(action);

        if (action == CurtainAction.Hold)
        {
            // Hold is a real one-beat manual step. It inherits the press from
            // the successful lower-left wind-up and must remain held until the
            // next target beat; releasing starts the wind-up step over.
            if (gestureRetryPending)
            {
                if (Time.time < gestureRetryReadyAt) return;
                gestureRetryPending = false;
                curtainTargetBeat = Math.Max(curtainTargetBeat, Math.Floor(beatClock.BeatIndex) + 1.0);
            }
            bool holdInput = curtainHeld && (Input.GetMouseButton(0) || Input.GetKey(KeyCode.Space) ||
                Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.W));
            if (!holdInput || Input.GetMouseButtonUp(0))
            {
                FailHoldAndRestartWind();
                return;
            }
            if (beatClock.BeatIndex >= curtainTargetBeat) TryCurtainAction(true);
            return;
        }

        if (IsAutomaticAction(action))
        {
            // Taps have already been released by the time the automatic beats run.
            // Do not let that earlier click count as the later drag's held state.
            curtainHeld = false;
            gestureArmed = false;
            if (action == CurtainAction.AutoApproach)
            {
                double approachStart = curtainTargetBeat - PlanDuration(curtainStep);
                float approach = Mathf.SmoothStep(0f, 1f,
                    Mathf.Clamp01((float)((beatClock.BeatIndex - approachStart) / PlanDuration(curtainStep))));
                guest.position = Vector3.Lerp(guestHome, curtainGuestSpot, approach);
                if (guestHead != null) guestHead.position = guest.position + new Vector3(0f, 0.88f, 0f);
            }
            if (beatClock.BeatIndex >= curtainTargetBeat) AdvanceCurtainStep();
            return;
        }

        bool mousePressed = GameplayMousePressed();
        if (mousePressed)
        {
            // Every gesture is measured from this press position, never from a
            // world-space curtain point or a previous mouse position.
            curtainHeld = true;
            gestureStart = Input.mousePosition;
            gestureArmed = false;
            cameraGestureStarted = false;
        }
        if (gestureRetryPending)
        {
            // Recovery lasts one full beat. No movement during this interval is
            // cached; after recovery, the retry beat and its 0.3-beat start
            // sample are established fresh.
            gestureArmed = false;
            gestureStartCaptured = false;
            if (Time.time < gestureRetryReadyAt) return;
            gestureRetryPending = false;
            curtainTargetBeat = Math.Max(curtainTargetBeat, Math.Floor(beatClock.BeatIndex) + 1.0);
        }
        bool directional = IsDirectionalAction(action);
        if (directional && !gestureStartCaptured &&
            beatClock.BeatIndex >= curtainTargetBeat - gestureStartLeadBeats)
        {
            gestureStart = Input.mousePosition;
            gestureStartCaptured = true;
            gestureArmed = false;
        }
        bool gestureInputReady = !directional || gestureStartCaptured;
        Vector3 delta = Input.mousePosition - gestureStart;
        float xThreshold = GestureThreshold * Screen.width;
        float yThreshold = GestureThreshold * Screen.height;
        bool left = Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A);
        bool right = Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D);
        bool down = Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S);
        bool leftHeld = Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A);
        bool rightHeld = Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D);
        bool downHeld = Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S);
        bool upHeld = Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W);
        bool keyboardHeld = leftHeld || rightHeld || downHeld || upHeld || Input.GetKey(KeyCode.Space);
        if (IsDirectionalAction(action) && keyboardHeld) curtainHeld = true;

        // Visual response starts with the drag, independently of rhythm judgement.
        // A tiny pixel dead zone prevents a stationary click from starting a pan.
        if (gestureInputReady && curtainHeld && IsDirectionalAction(action) && !cameraGestureStarted &&
            (HasReachedGestureDistance(action, delta, 4f, 4f) ||
             IsKeyboardGesture(action, left, right, down, leftHeld, rightHeld, downHeld, upHeld)))
        {
            cameraPanAction = action;
            cameraPanStartedAt = Time.time;
            cameraReboundStartedAt = -1f;
            cameraGestureStarted = true;
            motionEvents |= (int)MotionEvent.PanStart;
        }

        if (action == CurtainAction.Tap && (mousePressed || Input.GetKeyDown(KeyCode.Space)))
            TryCurtainAction();
        else if (gestureInputReady && curtainHeld && IsDirectionalAction(action) &&
                 (HasReachedGestureDistance(action, delta, xThreshold, yThreshold) ||
                  IsKeyboardGesture(action, left, right, down, leftHeld, rightHeld, downHeld, upHeld)))
        {
            // Direction and distance can be prepared before the beat. Once
            // armed, simply holding through the bright beat completes it.
            if (!gestureArmed) motionEvents |= (int)MotionEvent.Armed;
            gestureArmed = true;
        }

        // Prepared gestures land on the actual beat boundary. The old
        // symmetric window could fire up to 0.3 beat early, making a swipe
        // look like it skipped a frame before the visible beat arrived.
        if (gestureInputReady && gestureArmed && curtainHeld && beatClock.BeatIndex >= curtainTargetBeat)
            TryCurtainAction(true);

        // A late input can fill the escape ring and end the sequence above.
        // Do not process its expired beat again or overwrite the escape feedback.
        if (stage != PrankStage.CurtainSequence) return;

        if (Input.GetMouseButtonUp(0))
        {
            if (cameraGestureStarted) TriggerCameraFailure();
            curtainHeld = false;
            gestureArmed = false;
            gestureStartCaptured = false;
            cameraGestureStarted = false;
        }
        if (beatClock.BeatIndex > curtainTargetBeat + BeatTolerance)
        {
            TriggerCameraFailure();
            if (!RegisterEmptyBeat()) return;
            curtainTargetBeat = Math.Floor(beatClock.BeatIndex) + 1.0;
            retryMessage = T("漏拍了，当前操作留到下一拍重试。", "Missed; retry this action on the next beat.");
            ShowFeedback(T("漏拍 · 下一拍重试", "MISS · Retry next beat"), new Color(1f, 0.45f, 0.35f));
        }
    }

    void FailHoldAndRestartWind()
    {
        TriggerCameraFailure();
        int windStep = curtainStep > 0 && PlanAction(curtainStep - 1) == CurtainAction.WindLeft
            ? curtainStep - 1 : curtainStep;
        curtainStep = windStep;
        curtainTargetBeat = Math.Floor(beatClock.BeatIndex) + 1.0;
        ResetGestureState();
        gestureRetryPending = true;
        gestureRetryReadyAt = Time.time + BeatDurationSeconds;
        retryMessage = T("蓄力中断，从左下重新开始。", "Wind-up interrupted; restart from lower-left.");
        ShowFeedback(T("蓄力失败 · 下一拍重来", "WIND-UP FAILED · Restart next beat"), new Color(1f, 0.45f, 0.35f));
        UpdateCurtainCue(PlanAction(curtainStep));
    }

    void TryCurtainAction(bool keepHolding = false)
    {
        double delta = beatClock.BeatIndex - curtainTargetBeat;
        if (Math.Abs(delta) <= BeatTolerance)
        {
            ResetEscapeProgress();
            motionEvents |= (int)MotionEvent.Success;
            retryMessage = "";
            ShowFeedback(T("卡点！", "ON BEAT!"), new Color(1f, 0.84f, 0.3f));
            lastPerformedAction = PlanAction(curtainStep);
            actionImpactUntil = Time.time + 0.24f;
            TriggerCameraImpact(lastPerformedAction);
            if (keepHolding)
            {
                gestureStartCaptured = false;
                gestureStart = Input.mousePosition;
            }
            gestureArmed = false;
            cameraGestureStarted = false;
            AdvanceCurtainStep();
        }
        else if (delta < 0)
        {
            TriggerCameraFailure();
            retryMessage = T("太早了，等这一拍落下。", "Too early; wait for the beat.");
            ShowFeedback(T("太早", "TOO EARLY"), new Color(1f, 0.55f, 0.32f));
        }
        else
        {
            TriggerCameraFailure();
            if (!RegisterEmptyBeat()) return;
            curtainTargetBeat = Math.Floor(beatClock.BeatIndex) + 1.0;
            retryMessage = T("太晚了，当前操作下一拍重试。", "Too late; retry this action next beat.");
            ShowFeedback(T("太晚 · 下一拍重试", "TOO LATE · Retry next beat"), new Color(1f, 0.45f, 0.35f));
        }
    }

    static bool IsDirectionalAction(CurtainAction action)
    {
        return action == CurtainAction.DragRight || action == CurtainAction.DragDown ||
               action == CurtainAction.SwipeLeft || action == CurtainAction.SwipeRight ||
               action == CurtainAction.WindLeft || action == CurtainAction.ThrowRightRelease;
    }

    bool HasReachedGestureDistance(CurtainAction action, Vector3 delta, float xThreshold, float yThreshold)
    {
        switch (action)
        {
            case CurtainAction.DragRight:
            case CurtainAction.SwipeRight: return delta.x >= xThreshold;
            case CurtainAction.DragDown: return delta.y <= -yThreshold;
            case CurtainAction.SwipeLeft: return delta.x <= -xThreshold;
            case CurtainAction.WindLeft: return delta.x <= -xThreshold && delta.y <= -yThreshold;
            case CurtainAction.ThrowRightRelease: return delta.x >= xThreshold && delta.y >= yThreshold;
            default: return false;
        }
    }

    static bool IsKeyboardGesture(CurtainAction action, bool left, bool right, bool down,
        bool leftHeld, bool rightHeld, bool downHeld, bool upHeld)
    {
        switch (action)
        {
            case CurtainAction.DragRight:
            case CurtainAction.SwipeRight: return right;
            case CurtainAction.DragDown: return down;
            case CurtainAction.SwipeLeft: return left;
            case CurtainAction.WindLeft: return leftHeld && downHeld;
            case CurtainAction.ThrowRightRelease: return rightHeld && upHeld;
            default: return false;
        }
    }

    void AdvanceCurtainStep()
    {
        curtainStep++;
        if (curtainStep >= PlanCount)
        {
            CompletePrank();
            UpdateStageHud();
            return;
        }
        // Target beat is the end of the next configured step. This keeps the
        // two-beat approach ending on beat 5 instead of accidentally ending on 4.
        curtainTargetBeat += PlanDuration(curtainStep);
        // Publish the next operation before the next rendered frame. The
        // previous successful animation can finish independently of the cue.
        UpdateStageHud();
        UpdateCurtainCue(PlanAction(curtainStep));
    }

    void CompletePrank()
    {
        stage = PrankStage.Complete; stageStartedAt = Time.time; ShowFeedback(T("成功命中！", "Perfect hit!"), new Color(0.35f, 1f, 0.52f)); prompt.text = T("成功！客人被吓跑了", "Success! The guest runs away"); status.text = T("返回探索视角……", "Returning to exploration…"); guestLeaveAt = Time.time + 0.6f;
        if (inputCueIcon != null) inputCueIcon.text = "";
        guestLaunchOrigin = guest.position;
        guestLaunchRotation = guest.rotation;
        guestLaunchScale = guest.localScale;
        curtainLaunchOrigin = curtain.position;
        curtainLaunchRotation = curtain.rotation;
        curtainLaunchScale = curtain.localScale;
        vignette.color = new Color(0.05f, 0.02f, 0.12f, 0.18f);
    }

    void ReturnToExplore()
    {
        motionFinishPending = motionSessionActive;
        ResetGestureState();
        mode = Mode.Explore; title.text = "客厅 · 探索"; prompt.text = ""; actionHint.text = T("移动：WASD / 方向键\n互动：靠近窗帘后按 E", "Move: WASD / Arrow Keys\nInteract: press E near the curtain"); vignette.color = new Color(0.05f, 0.02f, 0.12f, 0f); ghost.position = exploreGhostPos;
        if (inputCueIcon != null) inputCueIcon.text = "";
        if (guestHead != null) guestHead.localScale = guestHeadScaleHome;
    }

    void ResetGestureState()
    {
        gestureStart = Vector3.zero;
        curtainHeld = false;
        gestureArmed = false;
        gestureStartCaptured = false;
        gestureRetryPending = false;
        gestureRetryReadyAt = 0f;
        actionImpactUntil = -1f;
        cameraImpactStartedAt = -1f;
        cameraPanStartedAt = -1f;
        cameraGestureStarted = false;
        cameraReboundStartedAt = -1f;
        cameraPanVelocity = Vector3.zero;
        // Bake the current visual pose into the framing before clearing effects.
        // The framing filter then returns it smoothly instead of snapping home.
        appliedCameraActionOffset = Vector3.zero;
        appliedCameraZoomOffset = 0f;
    }

    void ResetPrank()
    {
        StartMotionSession();
        ResetEscapeProgress();
        mode = Mode.Closeup;
        stage = PrankStage.CurtainSequence;
        renderedHudStep = -1;
        renderedCueStep = -1;
        stageStartedAt = Time.time;
        retryMessage = "";
        curtainStep = 0;
        curtainTargetBeat = beatClock.NextWholeBeat(firstInputLeadBeats);
        ResetGestureState();
        curtain.position = curtainHome;
        curtain.rotation = Quaternion.identity;
        guest.position = guestHome;
        guest.rotation = Quaternion.identity;
        guest.localScale = guestScaleHome;
        guestHead.position = guestHeadHome;
        guestHead.rotation = Quaternion.identity;
        guestHead.localScale = guestHeadScaleHome;
        vignette.color = new Color(0.05f, 0.02f, 0.12f, 0.35f);
        ShowFeedback(T("恶作剧已重置", "Prank reset"), new Color(0.55f, 0.95f, 1f));
    }

    void HandleDeveloperShortcuts()
    {
        if (mode != Mode.Closeup) return;
        if (Input.GetKeyDown(KeyCode.R)) ResetPrank();
        if (Input.GetKeyDown(KeyCode.F2)) { curtainStep = 4; curtainTargetBeat = beatClock.NextWholeBeat(1); ResetGestureState(); }
        if (Input.GetKeyDown(KeyCode.F3)) { curtainStep = 7; curtainTargetBeat = beatClock.NextWholeBeat(1); ResetGestureState(); }
        if (Input.GetKeyDown(KeyCode.F4)) { curtainStep = 15; curtainTargetBeat = beatClock.NextWholeBeat(1); ResetGestureState(); }
    }

    void UpdatePrankVisuals()
    {
        if (mode != Mode.Closeup || curtain == null || guest == null) return;
        float elapsed = Time.time - stageStartedAt;
        curtain.position = curtainHome;
        curtain.rotation = Quaternion.identity;
        curtain.localScale = curtainScaleHome;

        if (stage == PrankStage.CurtainSequence && curtainStep < PlanCount)
        {
            CurtainAction action = PlanAction(curtainStep);
            if (Time.time < actionImpactUntil) action = lastPerformedAction;
            // Manual actions use their own full rise-and-fall curve, ending at
            // rest exactly when the impact window closes. Tying this pose to
            // the looped beat phase made it snap back midway through a swipe.
            float impactProgress = Mathf.Clamp01((Time.time - (actionImpactUntil - 0.24f)) / 0.24f);
            float impactArc = ActionEnvelope(impactProgress);
            float hit = Time.time < actionImpactUntil ? impactArc : 0f;
            double stepStartBeat = curtainTargetBeat - CurrentStepDuration;
            float stepProgress = Mathf.Clamp01((float)((beatClock.BeatIndex - stepStartBeat) / CurrentStepDuration));
            // Leave room for the preceding tap's animation before lifting.
            float pull = action == CurtainAction.AutoLift
                ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, stepProgress)) : hit;
            Vector3 guestBase = curtainStep >= 4 ? curtainGuestSpot : guestHome;
            float guestTilt = 0f;
            float guestScale = 1f;
            Vector3 guestOffset = Vector3.zero;

            // The approach is driven by HandleCurtainSequence so the guest
            // actually reaches the curtain. Other steps pose around that spot.
            if (action != CurtainAction.AutoApproach) guest.position = guestBase;

            bool automatic = IsAutomaticAction(action);
            bool confirmed = Time.time < actionImpactUntil;
            if (automatic || confirmed || action == CurtainAction.Hold) switch (action)
            {
                case CurtainAction.Tap:
                    curtain.position += new Vector3(Mathf.Sin(closeupPulse * 18f) * 0.10f * hit, 0f, 0f);
                    curtain.rotation = Quaternion.Euler(0f, 0f, Mathf.Sin(closeupPulse * 18f) * 5f * hit);
                    break;
                case CurtainAction.AutoLift:
                    curtain.position += new Vector3(-0.12f * pull, 0.20f * pull, 0f);
                    curtain.rotation = Quaternion.Euler(0f, 0f, -11f * pull);
                    curtain.localScale = Vector3.Scale(curtainScaleHome, new Vector3(1f, 1f - 0.10f * pull, 1f));
                    break;
                case CurtainAction.AutoApproach:
                    float liftReturn = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(stepProgress / 0.35f));
                    curtain.position += new Vector3(-0.12f, 0.20f, 0f) * liftReturn;
                    curtain.rotation = Quaternion.Euler(0f, 0f, -11f * liftReturn);
                    curtain.localScale = Vector3.Scale(curtainScaleHome, new Vector3(1f, 1f - 0.10f * liftReturn, 1f));
                    guestTilt = Mathf.Sin(closeupPulse * 9f) * 4f * ActionEnvelope(stepProgress);
                    break;
                case CurtainAction.AutoWrapComplete:
                    curtain.position += new Vector3(-0.48f, -0.16f, 0f);
                    curtain.localScale = Vector3.Scale(curtainScaleHome, new Vector3(1.12f, 0.94f, 1f));
                    guestScale = 0.90f;
                    guestTilt = Mathf.Sin(closeupPulse * 8f) * 3f;
                    break;
                case CurtainAction.DragRight:
                    curtain.position += new Vector3(-0.48f * pull, 0.04f * pull, 0f);
                    curtain.rotation = Quaternion.Euler(0f, 0f, -8f * pull);
                    guestOffset = new Vector3(-0.07f * pull, 0f, 0f);
                    break;
                case CurtainAction.DragDown:
                    curtain.position += new Vector3(-0.48f * pull, -0.16f * pull, 0f);
                    curtain.localScale = Vector3.Scale(curtainScaleHome, new Vector3(1f + 0.12f * pull, 1f - 0.06f * pull, 1f));
                    guestScale = 0.90f + 0.10f * (1f - pull);
                    break;
                case CurtainAction.SwipeLeft:
                case CurtainAction.SwipeRight:
                    float side = action == CurtainAction.SwipeLeft ? -1f : 1f;
                    curtain.position += new Vector3(side * 0.26f * hit, -0.10f * hit, 0f);
                    curtain.rotation = Quaternion.Euler(0f, 0f, side * 8f * hit);
                    guestOffset = new Vector3(side * 0.18f * hit, 0.04f * hit, 0f);
                    guestTilt = side * 16f * hit;
                    guestScale = 0.93f + 0.07f * (1f - hit);
                    break;
                case CurtainAction.WindLeft:
                    curtain.position += new Vector3(-0.50f * pull, -0.18f * pull, 0f);
                    curtain.rotation = Quaternion.Euler(0f, 0f, -15f * pull);
                    guestOffset = new Vector3(-0.20f * pull, -0.07f * pull, 0f);
                    guestTilt = -10f * pull;
                    break;
                case CurtainAction.Hold:
                    curtain.position += new Vector3(-0.50f, -0.18f, 0f);
                    curtain.rotation = Quaternion.Euler(0f, 0f, -15f);
                    guestOffset = new Vector3(-0.20f, -0.07f, 0f);
                    guestTilt = -10f + Mathf.Sin(closeupPulse * 5f) * 2f;
                    break;
                case CurtainAction.ThrowRightRelease:
                    curtain.position += new Vector3(0.40f * pull, 0.10f * pull, 0f);
                    curtain.rotation = Quaternion.Euler(0f, 0f, 18f * pull);
                    guestOffset = new Vector3(0.42f * pull, 0.18f * pull, 0f);
                    guestTilt = 22f * pull;
                    break;
            }

            if (action != CurtainAction.AutoApproach) guest.position += guestOffset;
            guest.rotation = Quaternion.Euler(0f, 0f, guestTilt);
            guest.localScale = guestScaleHome * guestScale;
        }
        else if (stage == PrankStage.Complete)
        {
            float launch = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / 1.15f));
            Vector3 exit = new Vector3(7.4f, guestLaunchOrigin.y, guestLaunchOrigin.z);
            guest.position = Vector3.Lerp(guestLaunchOrigin, exit, launch);
            guest.position += Vector3.up * Mathf.Sin(launch * Mathf.PI) * 1.0f;
            guest.rotation = Quaternion.Slerp(guestLaunchRotation, Quaternion.Euler(0f, 0f, 55f), launch);
            guest.localScale = Vector3.Lerp(guestLaunchScale, guestScaleHome * 0.72f, launch);
            // The final slap still plays while the guest launches, starting
            // from the actual pose instead of resetting at the stage boundary.
            float finish = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / 0.24f));
            float slap = ActionEnvelope(Mathf.Clamp01(elapsed / 0.24f));
            curtain.position = Vector3.Lerp(curtainLaunchOrigin, curtainHome, finish)
                + new Vector3(0.40f, 0.10f, 0f) * slap;
            curtain.rotation = Quaternion.Slerp(curtainLaunchRotation, Quaternion.identity, finish)
                * Quaternion.Euler(0f, 0f, 18f * slap);
            curtain.localScale = Vector3.Lerp(curtainLaunchScale, curtainScaleHome, finish);
        }
        else if (stage == PrankStage.Escaped)
        {
            float release = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / 0.24f));
            float leave = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / 1.15f));
            curtain.position = Vector3.Lerp(curtainLaunchOrigin, curtainHome, release);
            curtain.rotation = Quaternion.Slerp(curtainLaunchRotation, Quaternion.identity, release);
            curtain.localScale = Vector3.Lerp(curtainLaunchScale, curtainScaleHome, release);
            guest.position = Vector3.Lerp(guestLaunchOrigin, new Vector3(7.4f, guestLaunchOrigin.y, guestLaunchOrigin.z), leave);
            guest.rotation = Quaternion.Slerp(guestLaunchRotation, Quaternion.identity, release);
            guest.localScale = Vector3.Lerp(guestLaunchScale, guestScaleHome, release);
        }

        if (guestHead != null)
        {
            guestHead.position = guest.position + new Vector3(0f, 0.88f, 0f);
            guestHead.rotation = guest.rotation;
            float headRatio = guestScaleHome.x > 0.001f ? guest.localScale.x / guestScaleHome.x : 1f;
            guestHead.localScale = guestHeadScaleHome * headRatio;
        }
    }

    static float ActionEnvelope(float progress)
    {
        // Fast attack, gentler recovery; zero velocity at start, apex and end.
        return progress < 0.38f
            ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress / 0.38f))
            : 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((progress - 0.38f) / 0.62f));
    }

    void UpdateStageHud()
    {
        if (mode != Mode.Closeup) return;
        if (renderedHudStep == curtainStep && renderedHudStage == stage && renderedHudLanguage == language) return;
        renderedHudStep = curtainStep;
        renderedHudStage = stage;
        renderedHudLanguage = language;
        switch (stage)
        {
            case PrankStage.CurtainSequence:
                title.text = T("窗帘恶作剧 · 节拍操作", "Curtain Prank · Beat Actions");
                actionHint.text = T("所有手动操作都要落在亮拍；拖拽与挥打保持按住左键", "Manual actions land on the bright beat; hold left mouse through drags and swipes");
                progressText.text = T("流程拍位　", "Beat progress　") + CurrentPlanBeatNumber() + "/" + TotalPlanBeats;
                break;
            case PrankStage.Complete:
                title.text = T("窗帘恶作剧 · 完成", "Curtain Prank · Complete");
                actionHint.text = T("客人受惊离开 · 即将返回探索", "Guest is leaving · returning to exploration");
                progressText.text = T("流程进度　完成", "Progress　Complete");
                break;
            case PrankStage.Escaped:
                title.text = T("窗帘恶作剧 · 挣脱", "Curtain Prank · Escape");
                actionHint.text = T($"每漏{MissesPerEscapeSegment}拍涨一格，连续漏{MissesPerEscapeSegment * 4}拍后挣脱", $"One segment per {MissesPerEscapeSegment} empty beats; {MissesPerEscapeSegment * 4} consecutive misses break the restraint");
                progressText.text = T("挣脱进度　4/4", "Escape progress　4/4");
                prompt.text = T("客人挣脱了，可以重新开始恶作剧", "The guest broke free; try the prank again");
                status.text = T("返回探索视角……", "Returning to exploration…");
                break;
        }
    }

    void UpdateCurtainCue(CurtainAction action)
    {
        double beatDelta = beatClock.BeatIndex - curtainTargetBeat;
        Color cueColor = Math.Abs(beatDelta) <= BeatTolerance ? new Color(1f, 0.86f, 0.25f) : Color.white;
        if (prompt.color != cueColor) prompt.color = cueColor;
        if (inputCueIcon != null && inputCueIcon.color != cueColor) inputCueIcon.color = cueColor;

        bool cueLanguageChanged = renderedCueLanguage != language;
        if (renderedCueStep != curtainStep || cueLanguageChanged)
        {
            string instruction;
            string icon;
            switch (action)
            {
                case CurtainAction.Tap: icon = T("点", "L"); instruction = T("单击窗帘", "CLICK THE CURTAIN"); break;
                case CurtainAction.AutoLift: icon = ""; instruction = T("窗帘自己抬起……", "THE CURTAIN LIFTS…"); break;
                case CurtainAction.AutoApproach: icon = ""; instruction = T("客人走向窗帘……", "THE GUEST APPROACHES…"); break;
                case CurtainAction.AutoWrapComplete: icon = ""; instruction = T("包裹完成……", "WRAP COMPLETE…"); break;
                case CurtainAction.DragRight: icon = "→"; instruction = T("按住左键向右拉", "HOLD + DRAG RIGHT"); break;
                case CurtainAction.DragDown: icon = "↓"; instruction = T("保持按住，向下拉紧", "KEEP HOLDING + DRAG DOWN"); break;
                case CurtainAction.SwipeLeft: icon = "←"; instruction = T("按住左键向左挥", "HOLD + SWIPE LEFT"); break;
                case CurtainAction.SwipeRight: icon = "→"; instruction = T("保持按住向右挥", "KEEP HOLDING + SWIPE RIGHT"); break;
                case CurtainAction.WindLeft: icon = "↙"; instruction = T("保持按住，向左下蓄力", "KEEP HOLDING + WIND LOWER-LEFT"); break;
                case CurtainAction.Hold: icon = "-"; instruction = T("保持按住，维持蓄力", "KEEP HOLDING THE WIND-UP"); break;
                default: icon = "↗"; instruction = T("保持按住，向右上甩出后松开", "KEEP HOLDING + SWIPE UPPER-RIGHT, THEN RELEASE"); break;
            }
            if (inputCueIcon != null) inputCueIcon.text = icon;
            prompt.text = T($"第 {CurrentPlanBeatNumber()}/{TotalPlanBeats} 拍　{instruction}",
                            $"Beat {CurrentPlanBeatNumber()}/{TotalPlanBeats}　{instruction}");
            renderedCueStep = curtainStep;
            renderedCueLanguage = language;
        }
        if (renderedTargetBeat != curtainTargetBeat || renderedGestureArmed != gestureArmed ||
            renderedRetryMessage != retryMessage || cueLanguageChanged)
        {
            string timingStatus = gestureArmed
                ? T("已蓄好 · 保持按住等亮拍", "ARMED · HOLD FOR THE BRIGHT BEAT")
                : T($"目标拍：{curtainTargetBeat:0}　看亮拍操作", $"Target beat: {curtainTargetBeat:0}　act on the bright beat");
            status.text = (retryMessage.Length > 0 ? retryMessage + "　" : "") + timingStatus;
            renderedTargetBeat = curtainTargetBeat;
            renderedGestureArmed = gestureArmed;
            renderedRetryMessage = retryMessage;
        }
    }

    string CueIcon(CurtainAction action)
    {
        switch (action)
        {
            case CurtainAction.Tap: return T("点", "L");
            case CurtainAction.AutoLift:
            case CurtainAction.AutoApproach:
            case CurtainAction.AutoWrapComplete: return "";
            case CurtainAction.DragRight: return "→";
            case CurtainAction.DragDown: return "↓";
            case CurtainAction.WindLeft: return "↙";
            case CurtainAction.SwipeLeft: return "←";
            case CurtainAction.SwipeRight: return "→";
            case CurtainAction.Hold: return "-";
            default: return "↗";
        }
    }

    void ShowFeedback(string message, Color color)
    {
        if (feedback == null) return;
        feedback.text = message;
        feedback.color = color;
        feedbackUntil = Time.time + 0.72f;
    }

    void UpdateBeatCue()
    {
        if (!beatAssistEnabled || beatClock == null || beatFlash == null) return;
        // Drive the pulse from the same compensated clock used for judgement.
        // This avoids a frame-triggered flash drifting away from the audible beat.
        float phase = Mathf.Repeat((float)beatClock.BeatIndex, 1f);
        // The beat marker has two clear states: a bright circle at the hit,
        // then a quiet line for the remainder of the beat. Do not squash the
        // circle gradually; that made the timing feel mushy.
        bool hitCircle = phase < 0.15f;
        float pulse = Mathf.Exp(-phase * 18f);
        int beat = Mathf.FloorToInt((float)beatClock.BeatIndex);
        Color c = beat % 4 == 0 ? new Color(1f, 0.86f, 0.25f) : new Color(0.35f, 0.75f, 1f);
        c.a = hitCircle ? Mathf.Lerp(0.72f, 1f, pulse) : 0.14f;
        beatFlash.color = c;
        // The circle has a quick scale-up beat before it cuts to a hairline.
        // The base rect is 700px wide x 34px high at a 1000px reference width;
        // matching X/Y scales below keeps the first state truly circular.
        float circleProgress = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.12f, phase));
        float circleScale = Mathf.Lerp(0.34f, 1f, circleProgress);
        // The base is square: scaling it uniformly keeps the circle round.
        // After the hit window it cuts to a 700px line with ~2px thickness.
        float width = hitCircle ? circleScale : 13.46f;
        float height = hitCircle ? circleScale : 0.035f;
        beatFlash.rectTransform.localScale = new Vector3(width, height, 1f);
    }

    void UpdateFeedback()
    {
        if (feedback == null) return;
        if (Time.time > feedbackUntil) feedback.text = "";
    }

    string T(string chinese, string english) { return language == Language.Chinese ? chinese : english; }

    void ToggleLanguage()
    {
        language = language == Language.Chinese ? Language.English : Language.Chinese;
        if (languageButtonText != null) languageButtonText.text = language == Language.Chinese ? "EN" : "中";
        if (difficultyButtonText != null) difficultyButtonText.text = DifficultyLabel();
        if (beatAssistButtonText != null) beatAssistButtonText.text = BeatAssistLabel();
        if (mode == Mode.Explore) SetExplorePrompt();
        else UpdateStageHud();
        ShowFeedback(language == Language.Chinese ? "已切换中文" : "Switched to English", new Color(0.55f, 0.95f, 1f));
    }

    void ToggleDifficulty()
    {
        difficulty = difficulty == Difficulty.Normal ? Difficulty.Easy : Difficulty.Normal;
        if (difficultyButtonText != null) difficultyButtonText.text = DifficultyLabel();
        if (mode == Mode.Closeup) UpdateStageHud();
        ShowFeedback(difficulty == Difficulty.Easy ? T("简单难度：判定放宽", "Easy: wider timing window") : T("普通难度", "Normal difficulty"), new Color(0.55f, 0.95f, 1f));
    }

    string DifficultyLabel()
    {
        return difficulty == Difficulty.Easy ? T("简单", "Easy") : T("普通", "Normal");
    }

    float BeatTolerance { get { return difficulty == Difficulty.Easy ? easyBeatTolerance : beatTolerance; } }
    float GestureThreshold { get { return difficulty == Difficulty.Easy ? easyGestureThreshold : curtainGestureThreshold; } }
    int MissesPerEscapeSegment { get { return difficulty == Difficulty.Easy ? easyMissesPerEscapeSegment : normalMissesPerEscapeSegment; } }
    float BeatDurationSeconds { get { return 60f / Mathf.Max(1f, beatBpm); } }

    Button MakeLanguageButton()
    {
        var go = new GameObject("LanguageButton");
        go.transform.SetParent(canvas.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.31f, 0.945f); rt.anchorMax = new Vector2(0.35f, 0.98f); rt.offsetMin = rt.offsetMax = Vector2.zero;
        var image = go.AddComponent<Image>(); image.color = new Color(0.15f, 0.22f, 0.32f, 0.95f);
        var button = go.AddComponent<Button>(); button.targetGraphic = image; button.onClick.AddListener(ToggleLanguage);
        var label = new GameObject("Label"); label.transform.SetParent(go.transform, false); var textRt = label.AddComponent<RectTransform>(); textRt.anchorMin = Vector2.zero; textRt.anchorMax = Vector2.one; textRt.offsetMin = textRt.offsetMax = Vector2.zero;
        languageButtonText = label.AddComponent<Text>(); languageButtonText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); languageButtonText.fontSize = 12; languageButtonText.alignment = TextAnchor.MiddleCenter; languageButtonText.color = Color.white; languageButtonText.text = language == Language.Chinese ? "EN" : "中";
        return button;
    }

    Button MakeDifficultyButton()
    {
        var go = new GameObject("DifficultyButton");
        go.transform.SetParent(canvas.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.255f, 0.945f); rt.anchorMax = new Vector2(0.305f, 0.98f); rt.offsetMin = rt.offsetMax = Vector2.zero;
        var image = go.AddComponent<Image>(); image.color = new Color(0.15f, 0.22f, 0.32f, 0.95f);
        var button = go.AddComponent<Button>(); button.targetGraphic = image; button.onClick.AddListener(ToggleDifficulty);
        var label = new GameObject("Label"); label.transform.SetParent(go.transform, false);
        var textRt = label.AddComponent<RectTransform>(); textRt.anchorMin = Vector2.zero; textRt.anchorMax = Vector2.one; textRt.offsetMin = textRt.offsetMax = Vector2.zero;
        difficultyButtonText = label.AddComponent<Text>(); difficultyButtonText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); difficultyButtonText.fontSize = 11; difficultyButtonText.alignment = TextAnchor.MiddleCenter; difficultyButtonText.color = Color.white; difficultyButtonText.text = DifficultyLabel();
        return button;
    }

    bool GameplayMousePressed()
    {
        return Input.GetMouseButtonDown(0) &&
            (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject());
    }

    string BeatAssistLabel() { return T("节拍辅助：", "Beat assist: ") + (beatAssistEnabled ? T("开", "ON") : T("关", "OFF")); }

    void MakeBeatAssistButton()
    {
        var go = new GameObject("BeatAssistButton");
        go.transform.SetParent(canvas.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.80f, 0.945f); rt.anchorMax = new Vector2(0.98f, 0.985f); rt.offsetMin = rt.offsetMax = Vector2.zero;
        var bg = go.AddComponent<Image>(); bg.color = new Color(0.15f, 0.22f, 0.32f, 0.95f);
        var button = go.AddComponent<Button>(); button.targetGraphic = bg;
        button.onClick.AddListener(() =>
        {
            beatAssistEnabled = !beatAssistEnabled;
            beatFlash.gameObject.SetActive(beatAssistEnabled);
            beatAssistButtonText.text = BeatAssistLabel();
            if (beatAssistEnabled) UpdateBeatCue();
        });
        var label = new GameObject("Label"); label.transform.SetParent(go.transform, false);
        var textRt = label.AddComponent<RectTransform>(); textRt.anchorMin = Vector2.zero; textRt.anchorMax = Vector2.one; textRt.offsetMin = textRt.offsetMax = Vector2.zero;
        beatAssistButtonText = label.AddComponent<Text>();
        beatAssistButtonText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        beatAssistButtonText.fontSize = 13; beatAssistButtonText.alignment = TextAnchor.MiddleCenter;
        beatAssistButtonText.color = Color.white; beatAssistButtonText.text = BeatAssistLabel();
        beatAssistButtonText.raycastTarget = false;
    }

    void UpdateCamera()
    {
        if (cam == null) return;
        // Escape starts the map-camera return immediately; keep the escape
        // animation and input lock running independently until it finishes.
        bool mapFraming = mode == Mode.Explore || stage == PrankStage.Escaped;
        Vector3 target;
        Vector3 position;
        float size;
        if (mapFraming)
        {
            target = new Vector3(0, 0.35f, 0.8f);
            position = new Vector3(9.0f, 10.5f, -12.0f);
            size = 6.4f;
        }
        else
        {
            target = new Vector3(1.0f, 1.0f, 2.0f);
            position = new Vector3(5.5f, 6.4f, -7.0f);
            size = 4.0f;
        }

        // Derive the view angle only from the scene framing. Panning must never
        // change this rotation, even while the camera is easing into position.
        Quaternion rotation = Quaternion.LookRotation(target - position, Vector3.up);

        // Ease only the scene framing. Apply the action curve directly so it
        // is not filtered a second time or carried into the next operation.
        float moveSpeed = mapFraming ? 3.5f : 18f;
        float moveBlend = 1f - Mathf.Exp(-moveSpeed * Time.deltaTime);
        worldCueCloseupBlend = Mathf.Lerp(worldCueCloseupBlend, mapFraming ? 0f : 1f, moveBlend);
        Vector3 basePosition = cam.transform.position - appliedCameraActionOffset;
        float rotationBlend = 1f - Mathf.Exp(-4f * Time.deltaTime);
        cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, rotation, rotationBlend);
        appliedCameraActionOffset = GetCameraActionOffset(cam.transform.rotation);
        cam.transform.position = Vector3.Lerp(basePosition, position, moveBlend) + appliedCameraActionOffset;
        float baseSize = cam.orthographicSize - appliedCameraZoomOffset - appliedPressureZoomOffset;
        appliedCameraZoomOffset = GetCameraZoomOffset();
        appliedPressureZoomOffset = GetPressureZoomOffset(mapFraming);
        float sizeBlend = 1f - Mathf.Exp(-3.5f * Time.deltaTime);
        cam.orthographicSize = Mathf.Lerp(baseSize, size, sizeBlend)
            + appliedCameraZoomOffset + appliedPressureZoomOffset;
    }

    float GetPressureZoomOffset(bool mapFraming)
    {
        float target = mapFraming ? 0f : -Mathf.Min(1.5f, escapeProgress * Mathf.Max(0f, escapeZoomPerSegment));
        if (target != pressureZoomTarget)
        {
            pressureZoomStart = appliedPressureZoomOffset;
            pressureZoomTarget = target;
            pressureZoomStartedAt = Time.time;
        }
        float age = pressureZoomStartedAt < 0f ? 1f : Time.time - pressureZoomStartedAt;
        if (pressureZoomTarget < pressureZoomStart)
        {
            // Fast 120% attack, then recover the extra 20% and hold the target.
            // Apply directly, without the slower scene-framing zoom filter.
            float peak = pressureZoomStart + (pressureZoomTarget - pressureZoomStart) * 1.2f;
            return age < 0.055f
                ? Mathf.Lerp(pressureZoomStart, peak, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / 0.055f)))
                : Mathf.Lerp(peak, pressureZoomTarget, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((age - 0.055f) / 0.12f)));
        }
        return Mathf.Lerp(pressureZoomStart, pressureZoomTarget,
            Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / 0.16f)));
    }

    float GetCameraZoomOffset()
    {
        if (mode != Mode.Closeup || cameraImpactStartedAt < 0f) return 0f;
        float duration = cameraImpactAction == CurtainAction.Tap ? 0.24f : 0.42f;
        float t = Mathf.Clamp01((Time.time - cameraImpactStartedAt) / duration);
        if (cameraImpactAction == CurtainAction.Tap)
            return t < 0.4f
                ? Mathf.Lerp(cameraZoomStartOffset, -0.28f, Mathf.SmoothStep(0f, 1f, t / 0.4f))
                : Mathf.Lerp(-0.28f, 0f, Mathf.SmoothStep(0f, 1f, (t - 0.4f) / 0.6f));
        if (t < 0.3f)
            return Mathf.Lerp(cameraZoomStartOffset, -0.52f, Mathf.SmoothStep(0f, 1f, t / 0.3f));
        if (t < 0.68f)
            return Mathf.Lerp(-0.52f, 0.20f, Mathf.SmoothStep(0f, 1f, (t - 0.3f) / 0.38f));
        return Mathf.Lerp(0.20f, 0f, Mathf.SmoothStep(0f, 1f, (t - 0.68f) / 0.32f));
    }

    Vector3 GetCameraActionOffset(Quaternion viewRotation)
    {
        if (mode != Mode.Closeup) return Vector3.zero;
        if (cameraReboundStartedAt >= 0f)
        {
            float t = (Time.time - cameraReboundStartedAt) / BeatDurationSeconds;
            if (t >= 1f)
            {
                cameraPanVelocity = Vector3.zero;
                return Vector3.zero;
            }
            // Return through centre with one small overshoot, then settle.
            float spring = (1f + 5f * t) * Mathf.Exp(-5f * t) * Mathf.Cos(3f * Mathf.PI * t);
            float settle = 1f - Mathf.SmoothStep(0f, 1f, t);
            Vector3 rebound = cameraReboundOffset * spring * settle;
            Vector3 previous = Quaternion.Inverse(viewRotation) * appliedCameraActionOffset;
            cameraPanVelocity = Time.deltaTime > 0f ? (rebound - previous) / Time.deltaTime : Vector3.zero;
            return viewRotation * rebound;
        }
        float age = Time.time - cameraPanStartedAt;
        if (cameraPanStartedAt < 0f || age < 0f)
            return Vector3.zero;
        Vector2 direction;
        // Latch the action when dragging starts; advancing the cue cannot redirect it.
        switch (cameraPanAction)
        {
            case CurtainAction.DragRight:
            case CurtainAction.SwipeRight: direction = Vector2.right; break;
            case CurtainAction.SwipeLeft: direction = Vector2.left; break;
            case CurtainAction.DragDown: direction = Vector2.down; break;
            case CurtainAction.WindLeft: direction = new Vector2(-1f, -1f).normalized; break;
            case CurtainAction.ThrowRightRelease: direction = new Vector2(1f, 1f).normalized; break;
            default: return Vector3.zero;
        }
        // Camera translation moves the visible scene in the opposite direction.
        // Negate it so the image travels with the arrow/gesture on screen.
        // Ease from the existing offset and hold there. Only failure rebounds;
        // the next gesture smoothly moves from here to its own bounded offset.
        Vector3 destination = new Vector3(-direction.x * 0.18f, -direction.y * 0.14f, 0f);
        Vector3 current = Quaternion.Inverse(viewRotation) * appliedCameraActionOffset;
        // Keep velocity when a gesture interrupts another pan or rebound.
        Vector3 offset = Vector3.SmoothDamp(current, destination, ref cameraPanVelocity,
            0.06f, Mathf.Infinity, Time.deltaTime);
        return viewRotation * offset;
    }

    void TriggerCameraFailure()
    {
        motionEvents |= (int)MotionEvent.Failure;
        bool hadCameraGesture = cameraGestureStarted;
        // Every failure path clears gameplay state, even if no camera pan started.
        gestureArmed = false;
        gestureStart = Input.mousePosition;
        cameraGestureStarted = false;
        bool directionalRetry = stage == PrankStage.CurtainSequence && curtainStep < PlanCount
            && IsDirectionalAction(PlanAction(curtainStep));
        gestureRetryPending = directionalRetry;
        gestureRetryReadyAt = directionalRetry ? Time.time + BeatDurationSeconds : 0f;
        if (!hadCameraGesture || cam == null) return;
        cameraReboundOffset = Quaternion.Inverse(cam.transform.rotation) * appliedCameraActionOffset;
        cameraReboundStartedAt = Time.time;
        cameraPanStartedAt = -1f;
    }


    void TriggerCameraImpact(CurtainAction action)
    {
        if (action != CurtainAction.Tap && action != CurtainAction.ThrowRightRelease) return;
        cameraZoomStartOffset = appliedCameraZoomOffset;
        cameraImpactAction = action;
        cameraImpactStartedAt = Time.time;
    }
    void SetExplorePrompt() { title.text = T("客厅 · 隐形鬼魂", "Living Room · Invisible Ghost"); prompt.text = T("找到窗帘和客人，靠近后开始恶作剧", "Find the curtain and guest to start the prank"); if (inputCueIcon != null) inputCueIcon.text = ""; }
    void ResetGuestScale() { if (guest != null) guest.localScale = Vector3.one; }

    GameObject MakeRect(string name, Vector3 pos, Vector3 scale, Color color) { var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.position = pos; go.transform.localScale = scale; SetColor(go, color); return go; }
    GameObject MakeCircle(string name, Vector3 pos, float radius, Color color) { var go = GameObject.CreatePrimitive(PrimitiveType.Sphere); go.name = name; go.transform.position = pos; go.transform.localScale = Vector3.one * radius * 2f; SetColor(go, color); return go; }
    void SetColor(GameObject go, Color color) { var r = go.GetComponent<Renderer>(); r.material = new Material(Shader.Find("Unlit/Color")); r.material.color = color; }
    void SetLayerRecursive(GameObject go, int layer) { go.layer = layer; foreach (Transform child in go.transform) SetLayerRecursive(child.gameObject, layer); }
    Sprite CreateEscapeRingSprite()
    {
        const int size = 128;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color[size * size];
        float center = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float dx = x - center, dy = y - center;
            float radius = Mathf.Sqrt(dx * dx + dy * dy);
            float rim = Mathf.Min(60f - radius, radius - 44f);
            float gap = Mathf.Min(Mathf.Abs(dx), Mathf.Abs(dy)) - 1.5f;
            float alpha = Mathf.SmoothStep(0f, 1f, Mathf.Min(rim, gap) / 1.5f);
            pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
        }
        texture.SetPixels(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    Sprite CreateCircleSprite()
    {
        const int size = 128;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color[size * size];
        float center = (size - 1) * 0.5f;
        float radius = center - 1f;
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float dx = x - center, dy = y - center;
            float edge = radius - Mathf.Sqrt(dx * dx + dy * dy);
            float alpha = Mathf.SmoothStep(0f, 1f, edge / 2.0f);
            pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
        }
        texture.SetPixels(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
    Text MakeText(string name, Vector2 anchor, Vector2 size, int fontSize, TextAnchor alignment, Color color) { var go = new GameObject(name); go.transform.SetParent(canvas.transform, false); var rt = go.AddComponent<RectTransform>(); rt.anchorMin = anchor + new Vector2(-size.x / 2, -size.y / 2); rt.anchorMax = anchor + new Vector2(size.x / 2, size.y / 2); rt.offsetMin = rt.offsetMax = Vector2.zero; var t = go.AddComponent<Text>(); t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); t.fontSize = fontSize; t.alignment = alignment; t.color = color; t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow; t.raycastTarget = false; return t; }
    Image MakePanel(string name, Vector2 anchor, Vector2 size, Color color) { var go = new GameObject(name); go.transform.SetParent(canvas.transform, false); var rt = go.AddComponent<RectTransform>(); rt.anchorMin = anchor - size / 2; rt.anchorMax = anchor + size / 2; rt.offsetMin = rt.offsetMax = Vector2.zero; var image = go.AddComponent<Image>(); image.color = color; image.raycastTarget = false; return image; }

    sealed class BeatClock
    {
        readonly double secondsPerBeat;
        readonly double heardOutputCompensation;
        double start;
        public double BeatIndex { get; private set; }
        public BeatClock(double secondsPerBeat, double heardOutputCompensation)
        {
            this.secondsPerBeat = Math.Max(0.0001, secondsPerBeat);
            this.heardOutputCompensation = Math.Max(0.0, heardOutputCompensation);
        }
        public void Start(double dspStart) { start = dspStart; }
        public void Tick() { BeatIndex = Math.Max(0, (AudioSettings.dspTime - start - heardOutputCompensation) / secondsPerBeat); }
        public double NextWholeBeat(double minimumLeadBeats)
        {
            return Math.Ceiling(BeatIndex + Math.Max(0, minimumLeadBeats) - 0.000001);
        }
        public bool WithinBeat(float tolerance) { double phase = BeatIndex - Math.Round(BeatIndex); return Math.Abs(phase) <= tolerance; }
    }
}
