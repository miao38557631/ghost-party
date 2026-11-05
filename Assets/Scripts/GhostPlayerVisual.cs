using UnityEngine;

/// <summary>Presentation only: receives existing locomotion, never reads input or moves the player root.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer), typeof(Animator))]
[DefaultExecutionOrder(150)]
public sealed class GhostPlayerVisual : MonoBehaviour
{
    [Header("Size and floating position")]
    [SerializeField, Min(0.1f)] float worldHeight = 1.35f;
    [Tooltip("Union height of the artwork, in sprite units. Do not measure each animation frame independently.")]
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

    static readonly int MovingId = Animator.StringToHash("IsMoving");
    Animator animator;
    SpriteRenderer spriteRenderer;
    public bool FacingRight { get; private set; } = true;
    public bool IsMoving { get; private set; }

    void Awake()
    {
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        ApplyFacing();
    }
    void OnEnable() { Camera.onPreCull += BeforeCameraRender; }
    void OnDisable() { Camera.onPreCull -= BeforeCameraRender; }

    public void SetMovement(Vector3 existingMovement)
    {
        existingMovement.y = 0f;
        // Existing controls supply world X/Z movement. Pure W/S and stopping keep the last facing.
        if (Mathf.Abs(existingMovement.x) > facingDeadzone)
        {
            var view = Camera.main;
            float screenHorizontal = view == null ? existingMovement.x
                : Vector3.Dot(existingMovement, view.transform.right);
            if (Mathf.Abs(screenHorizontal) > facingDeadzone) FacingRight = screenHorizontal > 0f;
        }
        ApplyFacing();
        IsMoving = existingMovement.sqrMagnitude > movementThreshold * movementThreshold;
        if (animator == null) animator = GetComponent<Animator>();
        if (animator.runtimeAnimatorController == null) return;
        animator.SetBool(MovingId, IsMoving);
        animator.speed = IsMoving ? walkPlaybackSpeed : idlePlaybackSpeed;
    }

    void LateUpdate()
    {
        float parentScale = transform.parent == null ? 1f : Mathf.Abs(transform.parent.lossyScale.y);
        transform.localPosition = localOffset;
        transform.localScale = Vector3.one * (worldHeight / (referenceSpriteHeight * Mathf.Max(0.0001f, parentScale)));
        Align(Camera.main);
        // Run after Animator evaluation: changing frames/states must never reset facing.
        ApplyFacing();
    }

    void ApplyFacing()
    {
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        spriteRenderer.flipX = FacingRight != artworkFacesRight;
    }

    void BeforeCameraRender(Camera view)
    {
        if (view == Camera.main) Align(view);
    }

    void Align(Camera view)
    {
        // Rotate only Visual; physics, movement axes and interaction references stay on InvisibleGhost.
        if (faceCamera && view != null) transform.rotation = view.transform.rotation;
    }
}
