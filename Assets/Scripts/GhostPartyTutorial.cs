using GhostParty.LevelWhitebox;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>The playable opening in TutorialScene, from the quiet corner to the first curtain hit.</summary>
[DisallowMultipleComponent]
public sealed class GhostPartyTutorial : MonoBehaviour
{
    enum Phase { Move, Rattle, FindClock, Straighten, Jump, Fall, Angry, Curtain, Hit, Loading }

    const float AngerDelay = 0.62f;
    const float RiseTime = 0.28f;
    const float WalkHalfWidth = 3.35f;
    const float WalkHalfDepth = 3.3f;
    const string FirstFloor = "Assets/LevelWhitebox/Scenes/Level01_FirstFloor.unity";
    static bool firstTapForFirstFloor;
    static Pose? fallenClockPose;

    [SerializeField] Transform player;
    [SerializeField] Transform clockProp;
    [SerializeField] Transform clockApproach;
    [SerializeField] Transform clockLanding;
    [SerializeField] Transform guest;
    [SerializeField] Transform mapFocus;
    [SerializeField] Transform curtainApproach;
    [SerializeField] Camera view;
    [SerializeField] LevelCurtainEncounter curtain;
    [SerializeField, Min(0.1f)] float clockRange = 0.95f;
    [SerializeField, Min(0.1f)] float curtainRange = 1.3f;

    GhostPartyPrototype prank;
    GhostPlayerVisual playerVisual;
    PrankCameraRig rig;
    Phase phase;
    Vector3 spawn, clockHome, guestHome, settledGuestPosition;
    Quaternion clockUpright, clockTilted;
    float elapsed, tickCountdown, musicBlend;
    AudioSource foley;
    AudioClip tick, bump, crash, knock;
    TutorialHud hud;
    TutorialBoundary boundary;
    float boundaryNoticeUntil;
    float restingHeight;
    bool wasBlockedAtBoundary;
    bool loading;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetHandoff()
    {
        firstTapForFirstFloor = false;
        fallenClockPose = null;
    }

    internal static bool ApplyFirstFloorHandoff()
    {
        bool completed = firstTapForFirstFloor;
        firstTapForFirstFloor = false;
        if (fallenClockPose is Pose pose)
        {
            var clock = GameObject.Find("Clock_5bf1f5ae");
            if (clock != null) clock.transform.SetPositionAndRotation(pose.position, pose.rotation);
        }
        return completed;
    }

    void Awake()
    {
        // The authored spawn rests on the seat. The motor snaps the controller to its normal hover height in Start.
        if (player != null) restingHeight = player.position.y;
    }

    void Start()
    {
        if (player == null || clockProp == null || clockApproach == null || clockLanding == null ||
            guest == null || mapFocus == null || curtainApproach == null || view == null || curtain == null)
        {
            Debug.LogError("TutorialScene is missing a required scene reference.", this);
            enabled = false;
            return;
        }
        prank = curtain.GetComponent<GhostPartyPrototype>();
        if (prank == null)
        {
            Debug.LogError("TutorialScene needs the authored curtain encounter.", curtain);
            enabled = false;
            return;
        }
        spawn = player.position;
        clockHome = clockProp.position;
        clockUpright = clockProp.rotation;
        clockTilted = clockUpright * Quaternion.Euler(0f, 0f, -22f);
        guestHome = guest.position;
        settledGuestPosition = guestHome;
        playerVisual = player.GetComponentInChildren<GhostPlayerVisual>();

        rig = view.GetComponent<PrankCameraRig>();
        if (rig == null) rig = view.gameObject.AddComponent<PrankCameraRig>();
        rig.Initialize(player, null, 0f, 0f, mapFocus);

        foley = gameObject.AddComponent<AudioSource>();
        foley.playOnAwake = false;
        tick = MakeSound("Clock tick", 0.045f, 950f, 0.14f, 0.025f);
        bump = MakeSound("Guest landing", 0.22f, 100f, 0.48f, 0.08f);
        crash = MakeSound("Clock clatter", 0.33f, 382f, 0.33f, 0.36f);
        knock = MakeSound("Window knock", 0.16f, 230f, 0.5f, 0.12f);
        hud = new TutorialHud(transform);
        boundary = new TutorialBoundary(transform, player.GetComponent<GhostPlayerMotor>());
    }

    public bool IsCurrentPossessionTarget(LevelCurtainEncounter candidate)
    {
        return isActiveAndEnabled && phase == Phase.Curtain && candidate == curtain && curtain.CanStart &&
            !GhostInput.Suspended && Near(curtainApproach, curtainRange);
    }

    void Update()
    {
        if (GhostPartyOpeningFade.IsActive || GhostInput.Suspended) return;
        float dt = Time.deltaTime;
        elapsed += dt;
        musicBlend = Mathf.MoveTowards(musicBlend, phase == Phase.Move ? 0f : 1f, dt * 1.1f);
        prank.SetTutorialMusicBlend(musicBlend);
        UpdateTick(dt);

        bool walking = phase == Phase.Move || phase == Phase.Rattle || phase == Phase.FindClock ||
            (phase == Phase.Curtain && !curtain.IsActive);
        MovePlayer(walking);
        switch (phase)
        {
            case Phase.Move:
                if (FlatDistance(player.position, spawn) > 0.18f) Enter(Phase.Rattle);
                break;
            case Phase.Rattle:
                guest.position = guestHome + Vector3.up * Hop(elapsed, 0.44f, 0.52f);
                if (elapsed >= 0.44f)
                    clockProp.rotation = Quaternion.Slerp(clockUpright, clockTilted, Ease((elapsed - 0.44f) / 0.20f));
                if (elapsed >= 0.69f)
                {
                    foley.PlayOneShot(bump, 0.72f);
                    Enter(Phase.FindClock);
                }
                break;
            case Phase.FindClock:
                if (elapsed >= 0.42f && Near(clockApproach, clockRange)) Enter(Phase.Straighten);
                break;
            case Phase.Straighten:
                clockProp.rotation = Quaternion.Slerp(clockTilted, clockUpright, Ease(elapsed / 0.4f));
                if (elapsed >= 0.78f) Enter(Phase.Jump);
                break;
            case Phase.Jump:
                float travel = Ease(elapsed / 0.66f);
                var stepTowardClock = new Vector3(-0.38f, 0f, 0.30f);
                guest.position = guestHome + stepTowardClock * travel + Vector3.up * Hop(elapsed, 0.66f, 1.12f);
                if (elapsed >= 0.66f)
                {
                    settledGuestPosition = guest.position;
                    if (rig != null) rig.PulseImpact(0.72f);
                    foley.PlayOneShot(bump);
                    Enter(Phase.Fall);
                }
                break;
            case Phase.Fall:
                float progress = Mathf.Clamp01(elapsed / 0.53f);
                clockProp.position = Vector3.Lerp(clockHome, clockLanding.position, progress * progress) +
                    Vector3.up * Hop(elapsed, 0.53f, 0.12f);
                clockProp.rotation = Quaternion.Slerp(clockUpright, clockLanding.rotation, progress);
                if (progress >= 1f)
                {
                    foley.PlayOneShot(crash);
                    Enter(Phase.Angry);
                }
                break;
            case Phase.Angry:
                if (playerVisual != null && elapsed >= AngerDelay && elapsed - dt < AngerDelay)
                    playerVisual.FaceScreenRight(Vector3.Dot(guest.position - player.position, view.transform.right) > 0f);
                DanceInPlace();
                if (elapsed >= 2.25f) Enter(Phase.Curtain);
                break;
            case Phase.Curtain:
                DanceInPlace();
                if (prank.TutorialTapConfirmed)
                {
                    foley.PlayOneShot(knock);
                    settledGuestPosition = new Vector3(guest.position.x, guestHome.y, guest.position.z);
                    Enter(Phase.Hit);
                }
                else if (GhostInput.InteractPressed && IsCurrentPossessionTarget(curtain))
                {
                    curtain.TryStart();
                }
                break;
            case Phase.Hit:
                // The first knock interrupts the same guest who knocked the clock down.
                guest.position = settledGuestPosition + Vector3.up * Hop(elapsed, 0.38f, 0.32f);
                if (elapsed >= 1.25f) Enter(Phase.Loading);
                break;
            case Phase.Loading:
                if (!loading && elapsed >= 0.3f)
                {
                    loading = true;
                    fallenClockPose = new Pose(clockProp.position, clockProp.rotation);
                    firstTapForFirstFloor = true;
                    SceneManager.LoadSceneAsync(FirstFloor);
                }
                break;
        }
    }

    void MovePlayer(bool walking)
    {
        if (!walking)
        {
            boundary.Hide();
            boundaryNoticeUntil = 0f;
            wasBlockedAtBoundary = false;
            GhostPlayerAvatar.SetMovement(player, Vector3.zero);
            return;
        }
        Vector3 input = GhostPlayerAvatar.ReadGroundInput();
        Vector3 right = Vector3.ProjectOnPlane(view.transform.right, Vector3.up).normalized;
        Vector3 forward = Vector3.ProjectOnPlane(view.transform.forward, Vector3.up).normalized;
        Vector3 movement = Vector3.ClampMagnitude(right * input.x + forward * input.z, 1f);

        // The movement limit and the painted dashes share one rectangle in world X/Z.
        Vector3 center = mapFocus.position;
        Rect bounds = Rect.MinMaxRect(center.x - WalkHalfWidth, center.z - WalkHalfDepth,
            center.x + WalkHalfWidth, center.z + WalkHalfDepth);
        Vector3 position = player.position;
        var blockedEdge = TutorialBoundary.Edge.None;
        if (position.x < bounds.xMin && movement.x < 0f) blockedEdge = TutorialBoundary.Edge.Left;
        else if (position.x > bounds.xMax && movement.x > 0f) blockedEdge = TutorialBoundary.Edge.Right;
        if (blockedEdge != TutorialBoundary.Edge.None) movement.x = 0f;
        if (position.z < bounds.yMin && movement.z < 0f)
        {
            blockedEdge = TutorialBoundary.Edge.Near;
            movement.z = 0f;
        }
        else if (position.z > bounds.yMax && movement.z > 0f)
        {
            blockedEdge = TutorialBoundary.Edge.Far;
            movement.z = 0f;
        }
        GhostPlayerAvatar.Move(player, movement, Time.deltaTime);

        bool blocked = blockedEdge != TutorialBoundary.Edge.None;
        if (!boundary.ShowNear(bounds, player.position, blockedEdge)) boundaryNoticeUntil = 0f;
        if (blocked && !wasBlockedAtBoundary) boundaryNoticeUntil = Time.time + 1.65f;
        wasBlockedAtBoundary = blocked;
    }

    void Enter(Phase value) { phase = value; elapsed = 0f; }

    void DanceInPlace()
    {
        if (RhythmAccent.TryGetBeat(out double beat, out _))
        {
            float lift = Mathf.Max(0f, 1f - 2f * (float)(beat % 1.0));
            guest.position = settledGuestPosition + Vector3.up * (lift * (1f - lift) * 1.1f);
        }
    }

    void UpdateTick(float dt)
    {
        if (phase >= Phase.Fall) return;
        tickCountdown -= dt;
        if (tickCountdown > 0f) return;
        tickCountdown = 0.66f;
        foley.PlayOneShot(tick, 0.6f);
    }

    static float FlatDistance(Vector3 from, Vector3 to)
    {
        Vector2 delta = new Vector2(from.x - to.x, from.z - to.z);
        return delta.magnitude;
    }

    bool Near(Transform target, float range) => FlatDistance(player.position, target.position) <= range;
    static float Ease(float t) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
    static float Hop(float time, float duration, float height)
    {
        float p = Mathf.Clamp01(time / duration);
        return 4f * height * p * (1f - p);
    }

    void LateUpdate()
    {
        if (hud == null) return;
        float restWeight = phase == Phase.Move ? 1f : phase == Phase.Rattle ? 1f - Ease(elapsed / RiseTime) : 0f;
        float seatOffset = (restingHeight - player.position.y) * restWeight;
        if (playerVisual != null) playerVisual.SetVerticalPoseOffset(seatOffset);
        if (phase != Phase.Move) hud.HideSleep();
        hud.SetFade(phase == Phase.Loading ? Ease(elapsed / 0.28f) : 0f);
        string line = "";
        string reaction = "";
        Vector3 anchor = Vector3.zero;
        Color32 tint = TutorialPaper.Cream;
        Color32 reactionTint = TutorialPaper.Yellow;
        switch (phase)
        {
            case Phase.Move:
                line = GhostInput.UsingGamepad
                    ? GhostLanguage.Text("左摇杆移动", "Left stick to move")
                    : GhostLanguage.Text("WASD 移动", "WASD to move");
                hud.ShowSleep(view, player.position + Vector3.up * (.82f + seatOffset), elapsed);
                break;
            case Phase.FindClock:
                line = GhostLanguage.Text("靠近挂钟，把它扶正", "Float over to straighten the clock");
                reaction = "!";
                anchor = clockProp.position + Vector3.up * .55f;
                break;
            case Phase.Straighten:
                line = GhostLanguage.Text("这下能安静一会儿了吧。", "Maybe I can finally get some peace.");
                break;
            case Phase.Angry:
                bool angry = elapsed >= AngerDelay;
                line = angry ? GhostLanguage.Text("别在这儿蹦跶！", "Quit jumping around in my house!")
                    : GhostLanguage.Text("我的钟……", "My clock…");
                reaction = angry ? "!!" : "…";
                anchor = player.position + Vector3.up * 1.04f;
                if (angry) reactionTint = tint = TutorialPaper.Pink;
                break;
            case Phase.Curtain:
                if (!curtain.IsActive)
                {
                    line = GhostLanguage.Text("去窗帘那儿，让他回头", "Use the curtain to get his attention");
                    if (!Near(curtainApproach, curtainRange))
                    {
                        reaction = "!";
                        anchor = curtain.transform.position + Vector3.up * .85f;
                    }
                }
                break;
            case Phase.Hit:
                line = GhostLanguage.Text("第一拍命中！", "First beat hit!");
                reaction = "?!";
                anchor = guest.position + Vector3.up * 1.72f;
                tint = TutorialPaper.Yellow;
                break;
        }

        if (Time.time < boundaryNoticeUntil &&
            (phase == Phase.FindClock || (phase == Phase.Curtain && !curtain.IsActive)))
        {
            line = GhostLanguage.Text("先完成教学，再离开", "Finish the tutorial before leaving");
            tint = TutorialPaper.Yellow;
        }
        if (line.Length == 0) hud.HideCaption();
        else hud.ShowCaption(line, tint);
        if (reaction.Length == 0) hud.HideReaction();
        else hud.ShowReaction(view, anchor, reaction, reactionTint);
    }

    static AudioClip MakeSound(string title, float length, float tone, float strength, float rattle)
    {
        const int rate = 16000;
        var wave = new float[Mathf.CeilToInt(length * rate)];
        for (int i = 0; i < wave.Length; i++)
        {
            float t = i / (float)rate;
            float decay = 1f - i / (float)wave.Length;
            float vibration = Mathf.Sin(2f * Mathf.PI * tone * t);
            float grit = Mathf.Sin(i * 19.17f) * Mathf.Sin(i * 71.39f);
            wave[i] = (vibration * strength + grit * rattle) * decay * decay;
        }
        var clip = AudioClip.Create(title, wave.Length, 1, rate, false);
        clip.SetData(wave, 0);
        return clip;
    }

    void OnDestroy()
    {
        boundary?.Dispose();
        if (tick != null) Destroy(tick);
        if (bump != null) Destroy(bump);
        if (crash != null) Destroy(crash);
        if (knock != null) Destroy(knock);
    }
}
