using UnityEngine;

/// <summary>Reusable NPC presentation: placeholder body/head plus idle sprite animation.</summary>
[DisallowMultipleComponent]
public sealed class PrankNpcActor : MonoBehaviour
{
    public Transform Head { get; private set; }
    Transform artwork;
    Animator idleAnimator;
    AnimationClip idleClip;
    Camera viewCamera;
    long lastAccentCycle = long.MinValue;

    void OnEnable() { Camera.onPreCull += AlignBeforeRender; }
    void OnDisable() { Camera.onPreCull -= AlignBeforeRender; }
    void Start() { EnsureInitialized(Camera.main); }

    public void EnsureInitialized(Camera viewCamera)
    {
        this.viewCamera = viewCamera;
        if (Head != null) return;
        var headObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        headObject.name = name + "Head";
        Head = headObject.transform;
        Head.position = transform.position + Vector3.up * 0.88f;
        Head.localScale = Vector3.one * 0.84f;
        var bodyRenderer = GetComponent<MeshRenderer>();
        var headRenderer = headObject.GetComponent<MeshRenderer>();
        var controller = Resources.Load<RuntimeAnimatorController>("NPC1/NPC1_Idle");
        if (controller == null)
        {
            Debug.LogWarning("NPC1 idle controller is missing; showing the NPC placeholders.");
            return;
        }
        if (bodyRenderer != null) bodyRenderer.enabled = false;
        headRenderer.enabled = false;
        var art = new GameObject("NPC1_Idle_Artwork");
        art.transform.SetParent(transform, false);
        art.transform.localPosition = new Vector3(0f, 0.39f, 0f);
        artwork = art.transform;
        art.AddComponent<SpriteRenderer>().sortingOrder = 2;
        idleAnimator = art.AddComponent<Animator>();
        idleAnimator.runtimeAnimatorController = controller;
        foreach (var clip in controller.animationClips)
            if (clip != null && clip.name == "NPC1_Idle") { idleClip = clip; break; }
        FaceCamera();
    }

    /// <summary>Locks one idle loop to the BGM's two-beat clap/snare accent.</summary>
    public void SyncIdleToBeat(double beatIndex, double secondsPerBeat)
    {
        if (idleAnimator == null || idleClip == null || secondsPerBeat <= 0.0) return;
        idleAnimator.speed = (float)(idleClip.length / (RhythmAccent.BeatsPerCycle * secondsPerBeat));
        long cycle = RhythmAccent.CycleAtBeat(beatIndex);
        if (cycle == lastAccentCycle) return;
        lastAccentCycle = cycle;
        double phase = RhythmAccent.PhaseInCycle(beatIndex);
        idleAnimator.Play("Idle", 0, (float)phase);
    }

    void LateUpdate()
    {
        if (RhythmAccent.TryGetBeat(out double beatIndex, out double secondsPerBeat))
            SyncIdleToBeat(beatIndex, secondsPerBeat);
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
