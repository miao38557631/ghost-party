using UnityEngine;

/// <summary>Reusable NPC presentation: placeholder body/head plus Spine idle animation.</summary>
[DisallowMultipleComponent]
public sealed class PrankNpcActor : MonoBehaviour
{
    public Transform Head { get; private set; }
    [Tooltip("Creates the placeholder sphere head until the Spine artwork loads. Turn off for spawned NPCs.")]
    [SerializeField] bool createPlaceholderBody = true;
    Transform artwork;
    Spine.Unity.SkeletonAnimation spineIdle;
    Spine.TrackEntry idleTrack;
    Spine.TrackEntry walkTrack;
    int syncedWalkClockRevision = -1;
    bool walking;
    bool initialized;
    float artworkScaleX;
    Camera viewCamera;
    long lastAccentCycle = long.MinValue;
    int syncedIdleClockRevision = -1;

    public bool CreatePlaceholderBody { get { return createPlaceholderBody; } set { createPlaceholderBody = value; } }
    public Transform Artwork { get { return artwork; } }

    void OnEnable() { Camera.onPreCull += AlignBeforeRender; }
    void OnDisable() { Camera.onPreCull -= AlignBeforeRender; }
    void Start() { EnsureInitialized(Camera.main); }

    public void EnsureInitialized(Camera viewCamera)
    {
        EnsureInitialized(viewCamera, createPlaceholderBody);
    }

    /// <summary>usePlaceholder = false skips the primitive sphere body/head entirely (spawned NPCs).</summary>
    public void EnsureInitialized(Camera viewCamera, bool usePlaceholder)
    {
        this.viewCamera = viewCamera;
        if (initialized) return;
        initialized = true;

        var headRenderer = (MeshRenderer)null;
        if (usePlaceholder)
        {
            var headObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            headObject.name = name + "Head";
            Head = headObject.transform;
            Head.position = transform.position + Vector3.up * 0.88f;
            Head.localScale = Vector3.one * 0.84f;
            headRenderer = headObject.GetComponent<MeshRenderer>();
        }
        var bodyRenderer = GetComponent<MeshRenderer>();
        var spinePrefab = Resources.Load<GameObject>("NPC1/NPC1_SpineIdle");
        if (spinePrefab == null)
        {
            Debug.LogWarning("NPC1 Spine idle prefab is missing; showing the NPC placeholders.");
            return;
        }
        var art = Instantiate(spinePrefab, transform);
        art.name = "NPC1_SpineIdle_Artwork";
        artwork = art.transform;
        artwork.localPosition = new Vector3(0f, -0.35f, 0f);
        artwork.localScale = Vector3.one * (100f / 750f);
        artworkScaleX = Mathf.Abs(artwork.localScale.x);
        spineIdle = art.GetComponent<Spine.Unity.SkeletonAnimation>();
        if (spineIdle == null || !spineIdle.IsValid)
        {
            Debug.LogError("NPC1 Spine idle failed to initialize; showing the NPC placeholders.");
            Destroy(art);
            artwork = null;
            return;
        }
        idleTrack = spineIdle.AnimationState.SetAnimation(0, "idle", true);
        if (bodyRenderer != null) bodyRenderer.enabled = false;
        if (headRenderer != null) headRenderer.enabled = false;
        FaceCamera();
    }

    /// <summary>Locks one idle loop to the BGM's two-beat clap/snare accent.</summary>
    public void SyncIdleToBeat(double beatIndex, double secondsPerBeat)
    {
        if (walking || spineIdle == null || idleTrack == null || secondsPerBeat <= 0.0) return;
        float duration = idleTrack.Animation.Duration;
        spineIdle.timeScale = (float)(duration / (RhythmAccent.BeatsPerCycle * secondsPerBeat));
        long cycle = RhythmAccent.CycleAtBeat(beatIndex);
        if (cycle == lastAccentCycle && syncedIdleClockRevision == RhythmAccent.ClockRevision) return;
        lastAccentCycle = cycle;
        syncedIdleClockRevision = RhythmAccent.ClockRevision;
        double phase = RhythmAccent.PhaseInCycle(beatIndex);
        idleTrack.TrackTime = (float)(phase * duration);
    }

    public void SetWalking(bool value)
    {
        if (spineIdle == null || !spineIdle.IsValid || walking == value) return;
        if (value && spineIdle.Skeleton.Data.FindAnimation("walk") == null) return;
        walking = value;
        if (walking)
        {
            spineIdle.timeScale = 1f;
            walkTrack = spineIdle.AnimationState.SetAnimation(0, "walk", true);
            syncedWalkClockRevision = -1;
            AlignWalkToBeat();
        }
        else
        {
            walkTrack = null;
            idleTrack = spineIdle.AnimationState.SetAnimation(0, "idle", true);
            lastAccentCycle = long.MinValue;
        }
    }

    void AlignWalkToBeat()
    {
        if (walkTrack == null ||
            !RhythmAccent.TryGetLoopTime(walkTrack.Animation.Duration, out float trackTime)) return;
        walkTrack.TrackTime = trackTime;
        syncedWalkClockRevision = RhythmAccent.ClockRevision;
    }

    public void SetFacingLeft(bool facingLeft)
    {
        if (artwork == null) return;
        Vector3 scale = artwork.localScale;
        scale.x = artworkScaleX * (facingLeft ? -1f : 1f);
        artwork.localScale = scale;
    }

    void LateUpdate()
    {
        if (RhythmAccent.TryGetBeat(out double beatIndex, out double secondsPerBeat))
        {
            if (walking)
            {
                if (syncedWalkClockRevision != RhythmAccent.ClockRevision) AlignWalkToBeat();
            }
            else SyncIdleToBeat(beatIndex, secondsPerBeat);
        }
        if (Head != null) Head.position = transform.position + Vector3.up * 0.88f;
        FaceCamera();
    }

    void AlignBeforeRender(Camera renderingCamera)
    {
        if (renderingCamera == viewCamera) FaceCamera();
    }

    void FaceCamera()
    {
        if (artwork == null || viewCamera == null) return;
        // Preserve the NPC's screen-space slap tilt while following map yaw.
        artwork.rotation = viewCamera.transform.rotation * Quaternion.Euler(0f, 0f, transform.eulerAngles.z);
    }
}
