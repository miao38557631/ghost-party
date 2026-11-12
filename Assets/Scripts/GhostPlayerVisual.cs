using UnityEngine;

/// <summary>Presentation only: receives existing locomotion, never reads input or moves the player root.
/// Drives a Spine SkeletonAnimation when the spine visual prefab is available, otherwise the sprite artwork.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer), typeof(Animator))]
[DefaultExecutionOrder(150)]
public sealed class GhostPlayerVisual : MonoBehaviour
{
    [Header("Size and floating position")]
    [SerializeField, Min(0.1f)] float worldHeight = 1.35f;
    [Tooltip("Union height of the sprite artwork, in sprite units. Do not measure each animation frame independently.")]
    [SerializeField, Min(0.01f)] float referenceSpriteHeight = 1.97265625f;
    [SerializeField] Vector3 localOffset = new Vector3(0f, -0.18f, 0f);
    [Header("Animation playback")]
    [SerializeField, Range(0.1f, 3f)] float idlePlaybackSpeed = 1f;
    [SerializeField, Range(0.1f, 3f)] float walkPlaybackSpeed = 1f;
    [SerializeField, Min(0f)] float movementThreshold = 0.01f;
    [Header("Horizontal facing")]
    [Tooltip("The supplied Ghost Idle/Walk artwork faces screen-right before mirroring.")]
    [SerializeField] bool artworkFacesRight = true;
    [SerializeField, Min(0f)] float facingDeadzone = 0.01f;
    [Header("Camera and depth")]
    [SerializeField] bool faceCamera = true;

    [Header("Spine ghost")]
    [Tooltip("Use the Spine skeleton instead of the sprite when Skeleton Data is assigned.")]
    [SerializeField] bool useSpineVisual = true;
    [SerializeField] Spine.Unity.SkeletonDataAsset spineSkeletonData;
    [SerializeField] string spineIdleAnimation = "idle";
    [SerializeField] string spineWalkAnimation = "walk";
    [Tooltip("Vertical nudge for the Spine artwork, in world units (negative drops it).")]
    [SerializeField] float spineVerticalOffset = 0f;
    [Tooltip("How quickly the artwork catches up when the collision body steps up or down. Increase for softer stair motion; 0 follows the body exactly.")]
    [SerializeField, Min(0f)] float stairVisualSmoothTime = 0.15f;
    [Tooltip("Spine playback multiplier on top of the animation's own speed.")]
    [SerializeField, Range(0.1f, 3f)] float spinePlaybackSpeed = 1f;

    static readonly int MovingId = Animator.StringToHash("IsMoving");
    Animator animator;
    SpriteRenderer spriteRenderer;
    Spine.Unity.SkeletonAnimation spine;
    Transform spineTransform;
    Renderer spineRenderer;
    bool spineHeightMeasured;
    string spineCurrentAnimation;
    Spine.TrackEntry spineTrack;
    int syncedClockRevision = -1;
    float spineNaturalHeight = 1f;
    float displayedWorldY;
    float visualVerticalVelocity;
    bool visualHeightInitialized;
    public bool FacingRight { get; private set; } = true;
    public bool IsMoving { get; private set; }
    public bool UsingSpine { get { return spine != null; } }

    void Awake()
    {
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (useSpineVisual) CreateSpineVisual();
        ApplyFacing();
    }
    void OnEnable() { Camera.onPreCull += BeforeCameraRender; }
    void OnDisable() { Camera.onPreCull -= BeforeCameraRender; }

    void CreateSpineVisual()
    {
        if (spineSkeletonData == null) return;
        GameObject instance = new GameObject("SpineVisual");
        instance.SetActive(false); // build components before Awake so the data asset is already assigned
        instance.transform.SetParent(transform, false);
        // Spine 4.3 keeps rendering and animation in separate components.
        var components = Spine.Unity.SkeletonAnimation.AddToGameObject(instance, spineSkeletonData);
        Spine.Unity.SkeletonAnimation animation = components.skeletonAnimation;
        instance.SetActive(true);
        if (!animation.IsValid || animation.Skeleton == null)
        {
            Debug.LogWarning("GhostPlayerVisual: Spine skeleton failed to initialize; keeping the sprite artwork.", this);
            Destroy(instance);
            return;
        }
        spine = animation;
        spineTransform = instance.transform;
        // Natural world height of the skeleton at local scale 1 (the asset import scale affects it).
        float dataHeight = Mathf.Max(0.0001f, spine.Skeleton.Data.Height);
        float importScale = Mathf.Max(0.00001f, Mathf.Abs(spineSkeletonData.scale));
        spineNaturalHeight = Mathf.Max(0.0001f, dataHeight * importScale);
        if (spriteRenderer != null) spriteRenderer.enabled = false;
        if (animator != null) animator.enabled = false;
        PlaySpine(spineIdleAnimation);
    }

    public void SetMovement(Vector3 existingMovement)
    {
        existingMovement.y = 0f;
        // Existing controls supply world X/Z movement. Pure W/S and stopping keep the last facing.
        var view = Camera.main;
        float screenHorizontal = view == null ? existingMovement.x
            : Vector3.Dot(existingMovement, view.transform.right);
        if (Mathf.Abs(screenHorizontal) > facingDeadzone) FacingRight = screenHorizontal > 0f;
        ApplyFacing();
        IsMoving = existingMovement.sqrMagnitude > movementThreshold * movementThreshold;
        if (spine != null)
        {
            PlaySpine(IsMoving ? spineWalkAnimation : spineIdleAnimation);
            return;
        }
        if (animator == null) animator = GetComponent<Animator>();
        if (animator.runtimeAnimatorController == null) return;
        animator.SetBool(MovingId, IsMoving);
        animator.speed = IsMoving ? walkPlaybackSpeed : idlePlaybackSpeed;
    }

    void LateUpdate()
    {
        float parentScale = transform.parent == null ? 1f : Mathf.Abs(transform.parent.lossyScale.y);
        Vector3 desiredLocalPosition = localOffset + Vector3.up * spineVerticalOffset;
        if (spine != null && transform.parent != null && stairVisualSmoothTime > 0f)
        {
            float targetWorldY = transform.parent.TransformPoint(desiredLocalPosition).y;
            if (!visualHeightInitialized || Mathf.Abs(targetWorldY - displayedWorldY) > 2.5f)
            {
                displayedWorldY = targetWorldY;
                visualVerticalVelocity = 0f;
                visualHeightInitialized = true;
            }
            else
            {
                displayedWorldY = Mathf.SmoothDamp(displayedWorldY, targetWorldY,
                    ref visualVerticalVelocity, stairVisualSmoothTime);
            }
            desiredLocalPosition.y += (displayedWorldY - targetWorldY) / Mathf.Max(0.0001f, parentScale);
        }
        transform.localPosition = desiredLocalPosition;
        if (spine != null)
        {
            if (syncedClockRevision != RhythmAccent.ClockRevision) AlignSpineToBeat();
            MeasureSpineHeight();
            transform.localScale = Vector3.one * (worldHeight / Mathf.Max(0.0001f, spineNaturalHeight * parentScale));
        }
        else
        {
            transform.localScale = Vector3.one * (worldHeight / (referenceSpriteHeight * Mathf.Max(0.0001f, parentScale)));
        }
        Align(Camera.main);
        // Run after Animator evaluation: changing frames/states must never reset facing.
        ApplyFacing();
    }

    /// <summary>The rendered mesh height is the ground truth; the data-height estimate is only the first-frame guess.</summary>
    void MeasureSpineHeight()
    {
        if (spineHeightMeasured) return;
        if (spineRenderer == null) spineRenderer = spine.GetComponent<Renderer>();
        if (spineRenderer == null) return;
        Bounds local = spineRenderer.localBounds;
        if (local.size.y <= 0.0001f) return;
        spineNaturalHeight = local.size.y;
        spineHeightMeasured = true;
    }

    void PlaySpine(string wanted)
    {
        if (spine == null || string.IsNullOrEmpty(wanted)) return;
        Spine.Animation animation = FindSpineAnimation(wanted);
        if (animation == null) return;
        if (spineCurrentAnimation == animation.Name) return;
        spineCurrentAnimation = animation.Name;
        spineTrack = spine.AnimationState.SetAnimation(0, animation.Name, true);
        spine.timeScale = spinePlaybackSpeed;
        syncedClockRevision = -1;
        AlignSpineToBeat();
    }

    void AlignSpineToBeat()
    {
        if (spineTrack == null ||
            !RhythmAccent.TryGetLoopTime(spineTrack.Animation.Duration, out float trackTime)) return;
        spineTrack.TrackTime = trackTime;
        syncedClockRevision = RhythmAccent.ClockRevision;
    }

    Spine.Animation FindSpineAnimation(string wanted)
    {
        if (spine == null || spine.Skeleton == null) return null;
        var animations = spine.Skeleton.Data.Animations;
        for (int i = 0; i < animations.Count; i++)
            if (string.Equals(animations.Items[i].Name, wanted, System.StringComparison.OrdinalIgnoreCase))
                return animations.Items[i];
        return null;
    }

    void ApplyFacing()
    {
        if (spine != null && spineTransform != null)
        {
            // Spine flips by mirroring the skeleton root; the sprite keeps using flipX.
            float sign = FacingRight == artworkFacesRight ? 1f : -1f;
            Vector3 scale = spineTransform.localScale;
            scale.x = Mathf.Abs(scale.x) * sign;
            spineTransform.localScale = scale;
            return;
        }
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null) spriteRenderer.flipX = FacingRight != artworkFacesRight;
    }

    void BeforeCameraRender(Camera view)
    {
        if (view == Camera.main) Align(view);
    }

    void Align(Camera view)
    {
        // Rotate only Visual; physics, movement axes and interaction references stay on InvisibleGhost.
        if (faceCamera && view != null)
            GroundPlaneBillboard.Align(transform, view, Mathf.Abs(transform.localScale.x));
    }
}
