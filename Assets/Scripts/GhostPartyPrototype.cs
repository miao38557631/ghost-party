using System;
using System.Collections.Generic;
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
    public enum PrankStage { Attract, Wrap, Slap, Charge, Throw, Complete }
    public enum Language { Chinese, English }

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

    [Header("Tuning")]
    [SerializeField] float moveSpeed = 4.5f;
    [SerializeField] float interactionRange = 2.0f;
    [SerializeField] float beatBpm = 112f;
    [SerializeField, Range(0.05f, 1f)] float bgmVolume = 0.34f;
    [SerializeField, Range(0.08f, 0.40f)] float beatTolerance = 0.20f;
    [SerializeField] float directionalThreshold = 0.35f;
    [SerializeField] float holdDuration = 0.78f;
    [SerializeField] int slapCount = 4;

    Mode mode = Mode.Explore;
    PrankStage stage = PrankStage.Attract;
    Transform ghost, curtain, guest, guestHead;
    Camera cam;
    Canvas canvas;
    Text prompt, status, title, actionHint, progressText, feedback, languageButtonText;
    Button languageButton;
    Image vignette, beatFlash, topHudPanel, bottomHudPanel;
    float feedbackUntil;
    AudioSource music;
    BeatClock beatClock;
    Vector3 exploreGhostPos = new Vector3(-4.6f, 0.38f, -1.5f);
    Vector3 guestHome = new Vector3(2.6f, 0.62f, 1.2f);
    Vector3 curtainHome;
    Vector3 guestHeadHome;
    Vector3 guestScaleHome = Vector3.one;
    float slapVisualUntil;
    float slapVisualDirection;
    double slapNextBeat;
    int lastBeatCue = -1;
    float stageStartedAt;
    float holdStartedAt = -1f;
    Vector3 gestureStart;
    Vector3 lastMouse;
    bool pointerWasDown;
    int slapStep;
    bool pathLowerLeft;
    float closeupPulse;
    float guestLeaveAt = -1f;
    string retryMessage = "";

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
        Application.targetFrameRate = 60;
        BuildWorld();
        BuildAudio();
        BuildHud();
        SetExplorePrompt();
    }

    void Update()
    {
        if (beatClock != null) beatClock.Tick();
        UpdateFeedback();
        UpdateBeatCue();
        if (Input.GetKeyDown(KeyCode.L)) ToggleLanguage();
        HandleDeveloperShortcuts();
        if (mode == Mode.Explore) UpdateExplore();
        else UpdateCloseup();
        UpdateCamera();
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
        guestHeadHome = guestHead.position;
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
        double secondsPerBeat = 60.0 / Math.Max(1f, beatBpm);
        int samples = Mathf.CeilToInt((float)(secondsPerBeat * beatsPerLoop * sampleRate));
        var clip = AudioClip.Create("Prototype_BGM_8bar_112BPM", samples, 1, sampleRate, false);
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
        beatClock = new BeatClock(beatBpm);
        double startDsp = AudioSettings.dspTime + 0.1;
        music.PlayScheduled(startDsp);
        beatClock.Start(startDsp);
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
        status = MakeText("Status", hudLayout.statusAnchor, hudLayout.statusSize, hudLayout.statusFontSize, TextAnchor.UpperLeft, new Color(0.72f, 0.85f, 0.92f));
        feedback = MakeText("Feedback", hudLayout.feedbackAnchor, hudLayout.feedbackSize, hudLayout.feedbackFontSize, TextAnchor.MiddleLeft, Color.white);
        feedback.text = "";
        // Slim beat indicator sits below the text stack; it never covers feedback text.
        beatFlash = MakePanel("BeatFlash", new Vector2(0.19f, 0.565f), new Vector2(0.32f, 0.014f), new Color(1f, 0.75f, 0.18f, 0f));
        beatFlash.transform.SetAsLastSibling();
        languageButton = MakeLanguageButton();
        vignette = MakePanel("Vignette", new Vector2(0, 0), new Vector2(1, 1), new Color(0.05f, 0.02f, 0.12f, 0.0f));
        vignette.rectTransform.anchorMin = Vector2.zero;
        vignette.rectTransform.anchorMax = Vector2.one;
        vignette.rectTransform.offsetMin = vignette.rectTransform.offsetMax = Vector2.zero;
        vignette.transform.SetAsFirstSibling();
    }

    void UpdateExplore()
    {
        Vector3 input = new Vector3(Input.GetAxisRaw("Horizontal"), 0, Input.GetAxisRaw("Vertical"));
        if (input.sqrMagnitude > 1) input.Normalize();
        ghost.position += input * moveSpeed * Time.deltaTime;
        ghost.position = new Vector3(Mathf.Clamp(ghost.position.x, -6.6f, 6.6f), 0.38f, Mathf.Clamp(ghost.position.z, -3.0f, 3.7f));
        Vector2 ghostFloor = new Vector2(ghost.position.x, ghost.position.z);
        Vector2 curtainFloor = new Vector2(curtain.position.x, curtain.position.z);
        if (Vector2.Distance(ghostFloor, curtainFloor) < interactionRange && (Input.GetKeyDown(KeyCode.E) || Input.GetMouseButtonDown(0))) BeginPrank();
        if (guestLeaveAt > 0 && Time.time > guestLeaveAt) { guest.position = Vector3.MoveTowards(guest.position, new Vector3(8f, guest.position.y, guest.position.z), Time.deltaTime * 2.5f); }
        if (guestHead != null) guestHead.position = guest.position + new Vector3(0, 0.88f, 0);
        if (guest.position.x > 7.5f) { guest.position = guestHome; guestLeaveAt = -1; }
        title.text = T("客厅 · 探索", "Living Room · Explore");
        actionHint.text = T("移动：WASD / 方向键\n互动：靠近窗帘后按 E", "Move: WASD / Arrow Keys\nInteract: press E near the curtain");
        progressText.text = T("当前目标：找到窗帘和客人", "Objective: find the curtain and guest");
        status.text = T("靠近右侧窗帘开始恶作剧", "Approach the curtain to start the prank");
    }
    void BeginPrank()
    {
        mode = Mode.Closeup; stage = PrankStage.Attract; stageStartedAt = Time.time; retryMessage = ""; slapStep = 0; pathLowerLeft = false;
        ghost.position = new Vector3(-2.7f, 0.2f, -1f); vignette.color = new Color(0.05f, 0.02f, 0.12f, 0.35f);
        title.text = T("窗帘恶作剧 · 特写", "Curtain Prank · Close-up"); prompt.text = T("吸引注意……", "Attract attention…"); status.text = T("自动演出，音乐与节拍继续", "Automatic performance; music and beat continue");
        ShowFeedback(T("进入特写", "Entering close-up"), new Color(0.55f, 0.95f, 1f));
    }

    void UpdateCloseup()
    {
        closeupPulse += Time.deltaTime;
        float elapsed = Time.time - stageStartedAt;
        UpdateStageHud();
        UpdatePrankVisuals();
        // Keyboard fallback: Space advances the current close-up step.
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (stage == PrankStage.Attract) SetStage(PrankStage.Wrap);
            else if (stage == PrankStage.Wrap) SetStage(PrankStage.Slap);
            else if (stage == PrankStage.Slap) RegisterSlapInput((slapStep % 2 == 0) ? -1 : 1);
            else if (stage == PrankStage.Charge) { if (!pathLowerLeft) pathLowerLeft = true; else SetStage(PrankStage.Throw); }
            else if (stage == PrankStage.Throw) CompletePrank();
        }
        if (stage == PrankStage.Attract)
        {
            prompt.text = T("吸引注意……", "Attract attention…"); status.text = T("自动演出：不用按键，等待提示变为下一步", "Automatic: no input, wait for the next step");
            if (elapsed > 1.6f) SetStage(PrankStage.Wrap);
        }
        else if (stage == PrankStage.Wrap) HandleWrap();
        else if (stage == PrankStage.Slap) HandleSlaps();
        else if (stage == PrankStage.Charge) HandleCharge();
        else if (stage == PrankStage.Throw)
        {
            prompt.text = T("重抽成功！客人飞出去……", "Big pull! The guest flies away…"); status.text = T("音乐继续，准备返回客厅", "Music continues; returning to the room");
            if (elapsed > 0.65f) CompletePrank();
        }
        else if (stage == PrankStage.Complete && elapsed > 1.8f) ReturnToExplore();
        if (Input.GetKeyDown(KeyCode.Escape)) ReturnToExplore();
    }

    void HandleWrap()
    {
        prompt.text = T("裹住客人：按住鼠标左键，向下拖动，再松开", "Wrap the guest: hold left mouse, drag down, release");
        status.text = retryMessage.Length > 0 ? retryMessage : T("主要：鼠标左键拖动　备用：Space 直接完成", "Main: left-mouse drag　Backup: Space");
        bool down = Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S);
        bool mouse = Input.GetMouseButton(0);
        if (mouse && !pointerWasDown) { gestureStart = Input.mousePosition; holdStartedAt = Time.time; }
        if ((mouse || down) && holdStartedAt < 0) { holdStartedAt = Time.time; gestureStart = Input.mousePosition; }
        if (mouse && Input.mousePosition.y < gestureStart.y - directionalThreshold * Screen.height) { holdStartedAt = Time.time - holdDuration; }
        if (Input.GetMouseButtonUp(0) || (!mouse && pointerWasDown))
        {
            bool ok = holdStartedAt > 0 && (Time.time - holdStartedAt) >= holdDuration && (Input.mousePosition.y < gestureStart.y - directionalThreshold * Screen.height || down);
            if (ok) SetStage(PrankStage.Slap); else Fail(T("太早或方向不对，重试：按住后向下拖动。", "Too early or wrong direction. Hold and drag down again."));
            holdStartedAt = -1f;
        }
        pointerWasDown = mouse;
    }

    void HandleSlaps()
    {
        // Rhythm Heaven style: the target is always one upcoming beat. The cue
        // changes before the beat, becomes a clear "hit now" window, then swaps
        // to the opposite hand immediately after a successful hit.
        while (beatClock.BeatIndex > slapNextBeat + beatTolerance) slapNextBeat += 1.0;

        int expected = slapStep % 2 == 0 ? -1 : 1;
        string dir = expected < 0 ? T("左", "LEFT") : T("右", "RIGHT");
        string arrow = expected < 0 ? "◀" : "▶";
        double beatDelta = beatClock.BeatIndex - slapNextBeat;
        string timingCue;
        Color cueColor;
        if (beatDelta < -beatTolerance)
        {
            timingCue = T("下一拍准备", "GET READY");
            cueColor = new Color(0.65f, 0.85f, 1f);
        }
        else if (Math.Abs(beatDelta) <= beatTolerance)
        {
            timingCue = T("现在按！", "HIT NOW!");
            cueColor = new Color(1f, 0.86f, 0.25f);
        }
        else
        {
            timingCue = T("换拍，等下一拍", "SWITCH — next beat");
            cueColor = new Color(1f, 0.45f, 0.35f);
        }

        prompt.text = T($"第 {slapStep + 1}/{slapCount} 拍　{arrow} 向{dir}　{timingCue}",
                        $"Hit {slapStep + 1}/{slapCount}　{arrow} {dir}　{timingCue}");
        prompt.color = cueColor;
        string retry = retryMessage.Length > 0 ? retryMessage + "　" : "";
        status.text = retry + T($"目标拍：{slapNextBeat + 1:0}　容差 ±{beatTolerance:0.00} 拍　成功后交换方向",
                                $"Target beat: {slapNextBeat + 1:0}　window ±{beatTolerance:0.00} beat　direction swaps on hit");
        Vector3 delta = Vector3.zero;
        if (Input.GetMouseButtonDown(0)) { gestureStart = Input.mousePosition; pointerWasDown = true; }
        if (Input.GetMouseButton(0) && pointerWasDown) delta = Input.mousePosition - gestureStart;
        bool leftKey = Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A);
        bool rightKey = Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D);
        if (leftKey || delta.x < -directionalThreshold * Screen.width) RegisterSlapInput(-1);
        else if (rightKey || delta.x > directionalThreshold * Screen.width) RegisterSlapInput(1);
        if (Input.GetMouseButtonUp(0)) pointerWasDown = false;
    }

    void RegisterSlapInput(int direction)
    {
        int expected = slapStep % 2 == 0 ? -1 : 1;
        if (direction != expected)
        {
            Fail(T("方向错了：这一下要向" + (expected < 0 ? "左" : "右") + "。", "Wrong direction: swipe " + (expected < 0 ? "left" : "right") + "."), true);
            return;
        }

        double delta = beatClock.BeatIndex - slapNextBeat;
        if (Math.Abs(delta) <= beatTolerance)
        {
            slapStep++;
            slapVisualUntil = Time.time + 0.24f;
            slapVisualDirection = direction;
            retryMessage = "";
            ShowFeedback(T("卡点！交换方向 → " + (slapStep < slapCount ? (slapStep % 2 == 0 ? "左" : "右") : "重抽"),
                           "ON BEAT! Switch → " + (slapStep < slapCount ? (slapStep % 2 == 0 ? "LEFT" : "RIGHT") : "PULL")), new Color(1f, 0.84f, 0.3f));
            guest.localScale = new Vector3(1.08f, 0.94f, 1);
            Invoke("ResetGuestScale", 0.12f);
            slapNextBeat += 1.0;
            if (slapStep >= slapCount) SetStage(PrankStage.Charge);
        }
        else if (delta < 0)
        {
            Fail(T("太早了，等鼓点落下再挥。", "Too early; strike on the beat."), true);
        }
        else
        {
            Fail(T("太晚了，下一拍再挥。", "Too late; strike on the next beat."), true);
            while (slapNextBeat <= beatClock.BeatIndex) slapNextBeat += 1.0;
        }
    }

    void HandleCharge()
    {
        prompt.text = T("蓄力重抽：按住，先向左下 ↙，再向右上 ↗，最后松开", "Wind up: hold, move lower-left ↙, then upper-right ↗, release");
        status.text = retryMessage.Length > 0 ? retryMessage : T("主要：鼠标路径 左下 ↙ → 右上 ↗　备用：Space 两次", "Main: mouse path ↙ → ↗　Backup: Space twice");
        bool mouse = Input.GetMouseButton(0);
        if (mouse && !pointerWasDown) { gestureStart = Input.mousePosition; pointerWasDown = true; pathLowerLeft = false; }
        if (mouse)
        {
            Vector3 d = Input.mousePosition - gestureStart;
            if (!pathLowerLeft && d.x < -directionalThreshold * Screen.width * 0.45f && d.y < -directionalThreshold * Screen.height * 0.2f) { pathLowerLeft = true; gestureStart = Input.mousePosition; }
            else if (pathLowerLeft && d.x > directionalThreshold * Screen.width * 0.45f && d.y > directionalThreshold * Screen.height * 0.2f) { prompt.text = "松开！"; }
        }
        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.S)) pathLowerLeft = true;
        if (Input.GetKeyDown(KeyCode.RightArrow) && pathLowerLeft) { SetStage(PrankStage.Throw); pointerWasDown = false; }
        if (Input.GetKeyDown(KeyCode.Return) && pathLowerLeft) { SetStage(PrankStage.Throw); pointerWasDown = false; }
        if (Input.GetMouseButtonUp(0))
        {
            Vector3 d = Input.mousePosition - gestureStart;
            if (pathLowerLeft && d.x > directionalThreshold * Screen.width * 0.35f && d.y > directionalThreshold * Screen.height * 0.15f) SetStage(PrankStage.Throw);
            else Fail(T("路径未完成，按住后先左下再右上。", "Path incomplete. Hold and move lower-left, then upper-right."), false);
            pointerWasDown = false;
        }
    }

    void SetStage(PrankStage next)
    {
        stage = next; stageStartedAt = Time.time; retryMessage = "";
        if (prompt != null) prompt.color = Color.white;
        ResetGestureState();
        ShowFeedback(StageName(next), new Color(0.55f, 0.95f, 1f));
        if (next == PrankStage.Slap) { slapStep = 0; slapNextBeat = Math.Floor(beatClock.BeatIndex) + 1.0; prompt.text = T("准备：下一拍开始挥打", "Ready: start on the next beat"); }
        if (next == PrankStage.Throw) { prompt.text = "重抽！"; guest.localScale = new Vector3(1.16f, 0.86f, 1); }
    }

    void Fail(string message, bool keepStage = false)
    {
        retryMessage = message; ResetGestureState(); ShowFeedback(T("需要重试", "Retry"), new Color(1f, 0.35f, 0.32f)); if (!keepStage) { stageStartedAt = Time.time; holdStartedAt = -1f; pointerWasDown = false; pathLowerLeft = false; }
    }

    void CompletePrank()
    {
        stage = PrankStage.Complete; stageStartedAt = Time.time; ShowFeedback(T("成功命中！", "Perfect hit!"), new Color(0.35f, 1f, 0.52f)); prompt.text = T("成功！客人被吓跑了", "Success! The guest runs away"); status.text = T("返回探索视角……", "Returning to exploration…"); guestLeaveAt = Time.time + 0.6f;
        guest.localScale = new Vector3(0.85f, 1.15f, 1);
        if (guestHead != null) guestHead.localScale = new Vector3(0.85f, 1.15f, 1);
        vignette.color = new Color(0.05f, 0.02f, 0.12f, 0.18f);
    }

    void ReturnToExplore()
    {
        mode = Mode.Explore; title.text = "客厅 · 探索"; prompt.text = ""; actionHint.text = T("移动：WASD / 方向键\n互动：靠近窗帘后按 E", "Move: WASD / Arrow Keys\nInteract: press E near the curtain"); vignette.color = new Color(0.05f, 0.02f, 0.12f, 0f); ghost.position = exploreGhostPos;
        if (guestHead != null) guestHead.localScale = Vector3.one;
    }

    void ResetGestureState()
    {
        holdStartedAt = -1f;
        gestureStart = Vector3.zero;
        pointerWasDown = false;
        pathLowerLeft = false;
    }

    void ResetPrank()
    {
        mode = Mode.Closeup;
        stage = PrankStage.Attract;
        stageStartedAt = Time.time;
        retryMessage = "";
        slapStep = 0;
        ResetGestureState();
        curtain.position = curtainHome;
        curtain.rotation = Quaternion.identity;
        guest.position = guestHome;
        guest.rotation = Quaternion.identity;
        guest.localScale = guestScaleHome;
        guestHead.position = guestHeadHome;
        guestHead.rotation = Quaternion.identity;
        guestHead.localScale = Vector3.one;
        vignette.color = new Color(0.05f, 0.02f, 0.12f, 0.35f);
        ShowFeedback(T("恶作剧已重置", "Prank reset"), new Color(0.55f, 0.95f, 1f));
    }

    void HandleDeveloperShortcuts()
    {
        if (mode != Mode.Closeup) return;
        if (Input.GetKeyDown(KeyCode.R)) ResetPrank();
        if (Input.GetKeyDown(KeyCode.F2)) SetStage(PrankStage.Wrap);
        if (Input.GetKeyDown(KeyCode.F3)) SetStage(PrankStage.Slap);
        if (Input.GetKeyDown(KeyCode.F4)) SetStage(PrankStage.Charge);
        if (Input.GetKeyDown(KeyCode.F5)) SetStage(PrankStage.Throw);
    }

    void UpdatePrankVisuals()
    {
        if (mode != Mode.Closeup || curtain == null || guest == null) return;
        float elapsed = Time.time - stageStartedAt;
        float beatWave = Mathf.Sin(closeupPulse * Mathf.PI * 2f);
        Vector3 curtainOffset = Vector3.zero;
        if (stage == PrankStage.Attract) curtainOffset.x = beatWave * 0.22f;
        else if (stage == PrankStage.Wrap) curtainOffset.x = Mathf.Sin(closeupPulse * 7f) * 0.10f;
        else if (stage == PrankStage.Slap) curtainOffset.x = Mathf.Sin(closeupPulse * 10f) * 0.16f;
        else if (stage == PrankStage.Charge) curtainOffset.x = Mathf.Lerp(0f, -0.28f, pathLowerLeft ? 1f : 0f) + beatWave * 0.05f;
        else if (stage == PrankStage.Throw) curtainOffset.x = Mathf.Lerp(-0.28f, 0.45f, Mathf.Clamp01(elapsed / 0.65f));
        curtain.position = curtainHome + curtainOffset;

        Vector3 guestOffset = Vector3.zero;
        if (stage == PrankStage.Wrap) guestOffset.y = Mathf.Sin(closeupPulse * 5f) * 0.08f;
        if (stage == PrankStage.Slap) guestOffset.x = slapVisualUntil > Time.time ? slapVisualDirection * 0.22f : Mathf.Sin(closeupPulse * 8f) * 0.03f;
        if (stage == PrankStage.Charge) guestOffset.x = pathLowerLeft ? -0.18f : 0f;
        if (stage == PrankStage.Throw) guestOffset = Vector3.Lerp(new Vector3(-0.18f, 0.05f, 0), new Vector3(2.0f, 0.5f, 0), Mathf.Clamp01(elapsed / 0.65f));
        if (stage == PrankStage.Complete) guestOffset.x = Mathf.Clamp01(elapsed / 1.8f) * 0.8f;
        guest.position = guestHome + guestOffset;
        float tilt = stage == PrankStage.Slap && slapVisualUntil > Time.time ? slapVisualDirection * 18f : 0f;
        guest.rotation = Quaternion.Euler(0f, 0f, tilt);
        guest.localScale = stage == PrankStage.Throw ? Vector3.Lerp(new Vector3(1.16f, 0.86f, 1f), new Vector3(0.72f, 1.2f, 1f), Mathf.Clamp01(elapsed / 0.65f)) : guestScaleHome;
        if (guestHead != null)
        {
            guestHead.position = guest.position + new Vector3(0f, 0.88f, 0f);
            guestHead.rotation = guest.rotation;
            guestHead.localScale = guest.localScale;
        }
    }

    void UpdateStageHud()
    {
        if (mode != Mode.Closeup) return;
        switch (stage)
        {
            case PrankStage.Attract:
                title.text = T("窗帘恶作剧 · 1/5 吸引注意", "Curtain Prank · 1/5 Attract");
                actionHint.text = T("本阶段不用按键 · 等待演出结束", "No input · wait for the performance");
                progressText.text = T("流程进度　■□□□□", "Progress　■□□□□");
                break;
            case PrankStage.Wrap:
                title.text = T("窗帘恶作剧 · 2/5 裹住客人", "Curtain Prank · 2/5 Wrap");
                actionHint.text = T("主要操作：鼠标左键按住并向下拖动后松开　|　备用：Space", "Main: hold left mouse, drag down, release　|　Backup: Space");
                progressText.text = T("流程进度　■■□□□", "Progress　■■□□□");
                break;
            case PrankStage.Slap:
                title.text = T("窗帘恶作剧 · 3/5 左右挥打", "Curtain Prank · 3/5 Slap");
                actionHint.text = T("节奏天国式：每拍一次，命中后交换左右　|　Space 也要卡点", "Rhythm Heaven style: one hit per beat, swap sides on hit　|　Space is timed too");
                progressText.text = T("流程进度　■■■□□　当前：", "Progress　■■■□□　Step: ") + slapStep + "/" + slapCount;
                break;
            case PrankStage.Charge:
                title.text = T("窗帘恶作剧 · 4/5 蓄力重抽", "Curtain Prank · 4/5 Wind up");
                actionHint.text = T("主要操作：按住鼠标走左下 ↙ → 右上 ↗　|　备用：Space 两次", "Main: mouse path ↙ → ↗　|　Backup: Space twice");
                progressText.text = T("流程进度　■■■■□　", "Progress　■■■■□　") + (pathLowerLeft ? T("已蓄力，完成右上方向", "Wind-up ready; complete upper-right") : T("等待左下蓄力", "Waiting for lower-left wind-up"));
                break;
            case PrankStage.Throw:
                title.text = T("窗帘恶作剧 · 5/5 重抽反馈", "Curtain Prank · 5/5 Pull");
                actionHint.text = T("输入已完成 · 观看客人飞出", "Input complete · watch the guest fly");
                progressText.text = T("流程进度　■■■■■", "Progress　■■■■■");
                break;
            case PrankStage.Complete:
                title.text = T("窗帘恶作剧 · 完成", "Curtain Prank · Complete");
                actionHint.text = T("客人受惊离开 · 即将返回探索", "Guest is leaving · returning to exploration");
                progressText.text = T("流程进度　完成", "Progress　Complete");
                break;
        }
    }

    string StageName(PrankStage value)
    {
        switch (value)
        {
            case PrankStage.Wrap: return T("进入：裹住客人", "Now: Wrap the guest");
            case PrankStage.Slap: return T("进入：左右挥打", "Now: Slap left/right");
            case PrankStage.Charge: return T("进入：蓄力重抽", "Now: Wind up and pull");
            case PrankStage.Throw: return T("重抽命中", "Pull hit");
            case PrankStage.Complete: return T("流程完成", "Prank complete");
            default: return T("吸引注意", "Attract attention");
        }
    }

    void ShowFeedback(string message, Color color)
    {
        if (feedback == null) return;
        feedback.text = message;
        feedback.color = color;
        feedbackUntil = Time.time + 0.72f;
        if (beatFlash != null) beatFlash.color = new Color(color.r, color.g, color.b, 0.14f);
    }

    void UpdateBeatCue()
    {
        if (beatClock == null || beatFlash == null) return;
        int beat = Mathf.FloorToInt((float)beatClock.BeatIndex);
        if (beat != lastBeatCue && beat >= 0)
        {
            lastBeatCue = beat;
            Color c = beat % 4 == 0 ? new Color(1f, 0.86f, 0.25f, 0.16f) : new Color(0.35f, 0.75f, 1f, 0.09f);
            beatFlash.color = c;
        }
    }

    void UpdateFeedback()
    {
        if (feedback == null) return;
        if (Time.time > feedbackUntil) feedback.text = "";
        if (beatFlash != null)
        {
            Color c = beatFlash.color;
            c.a = Mathf.MoveTowards(c.a, 0f, Time.deltaTime * 2.6f);
            beatFlash.color = c;
        }
    }

    string T(string chinese, string english) { return language == Language.Chinese ? chinese : english; }

    void ToggleLanguage()
    {
        language = language == Language.Chinese ? Language.English : Language.Chinese;
        if (languageButtonText != null) languageButtonText.text = language == Language.Chinese ? "EN" : "中";
        if (mode == Mode.Explore) SetExplorePrompt();
        else UpdateStageHud();
        ShowFeedback(language == Language.Chinese ? "已切换中文" : "Switched to English", new Color(0.55f, 0.95f, 1f));
    }

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

    void UpdateCamera()
    {
        if (cam == null) return;
        Vector3 target;
        Vector3 position;
        float size;
        if (mode == Mode.Explore)
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
        cam.transform.position = Vector3.Lerp(cam.transform.position, position, Time.deltaTime * 3.5f);
        Quaternion rotation = Quaternion.LookRotation(target - cam.transform.position, Vector3.up);
        cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, rotation, Time.deltaTime * 4.0f);
        cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, size, Time.deltaTime * 3.5f);
    }
    void SetExplorePrompt() { title.text = T("客厅 · 隐形鬼魂", "Living Room · Invisible Ghost"); prompt.text = T("找到窗帘和客人，靠近后开始恶作剧", "Find the curtain and guest to start the prank"); }
    void ResetGuestScale() { if (guest != null) guest.localScale = Vector3.one; }

    GameObject MakeRect(string name, Vector3 pos, Vector3 scale, Color color) { var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.position = pos; go.transform.localScale = scale; SetColor(go, color); return go; }
    GameObject MakeCircle(string name, Vector3 pos, float radius, Color color) { var go = GameObject.CreatePrimitive(PrimitiveType.Sphere); go.name = name; go.transform.position = pos; go.transform.localScale = Vector3.one * radius * 2f; SetColor(go, color); return go; }
    void SetColor(GameObject go, Color color) { var r = go.GetComponent<Renderer>(); r.material = new Material(Shader.Find("Unlit/Color")); r.material.color = color; }
    void SetLayerRecursive(GameObject go, int layer) { go.layer = layer; foreach (Transform child in go.transform) SetLayerRecursive(child.gameObject, layer); }
    Text MakeText(string name, Vector2 anchor, Vector2 size, int fontSize, TextAnchor alignment, Color color) { var go = new GameObject(name); go.transform.SetParent(canvas.transform, false); var rt = go.AddComponent<RectTransform>(); rt.anchorMin = anchor + new Vector2(-size.x / 2, -size.y / 2); rt.anchorMax = anchor + new Vector2(size.x / 2, size.y / 2); rt.offsetMin = rt.offsetMax = Vector2.zero; var t = go.AddComponent<Text>(); t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); t.fontSize = fontSize; t.alignment = alignment; t.color = color; t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow; return t; }
    Image MakePanel(string name, Vector2 anchor, Vector2 size, Color color) { var go = new GameObject(name); go.transform.SetParent(canvas.transform, false); var rt = go.AddComponent<RectTransform>(); rt.anchorMin = anchor - size / 2; rt.anchorMax = anchor + size / 2; rt.offsetMin = rt.offsetMax = Vector2.zero; var image = go.AddComponent<Image>(); image.color = color; return image; }

    sealed class BeatClock
    {
        readonly double secondsPerBeat; double start; public double BeatIndex { get; private set; }
        public BeatClock(float bpm) { secondsPerBeat = 60.0 / Math.Max(1f, bpm); }
        public void Start(double dspStart) { start = dspStart; }
        public void Tick() { BeatIndex = Math.Max(0, (AudioSettings.dspTime - start) / secondsPerBeat); }
        public bool WithinBeat(float tolerance) { double phase = BeatIndex - Math.Round(BeatIndex); return Math.Abs(phase) <= tolerance; }
    }
}




