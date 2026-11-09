using System.Collections.Generic;
using UnityEngine;

/// <summary>Single 3D collision-aware motor for the fixed-height 2.5D player. Input remains in existing gameplay.
/// The root keeps floating: no gravity, but its height follows the floor surface underneath (sunken areas included).</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public sealed class GhostPlayerMotor : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField, Min(0f)] float moveSpeed = 4.5f;

    [Header("Floating floor follow")]
    [Tooltip("Player root height above the floor. Raise this to make the ghost float higher; also gives the controller more clearance over stair edges.")]
    [SerializeField, Min(0f)] float hoverHeight = 0.9f;
    [Tooltip("How far above the root the downward floor probe starts. Must clear the highest nearby ledge the ghost may leave.")]
    [SerializeField, Min(0.2f)] float probeAbove = 2.5f;
    [SerializeField, Min(0.5f)] float probeDepth = 6f;
    [Tooltip("Highest surface the ghost rises onto, relative to the root. Anything higher is treated as a wall instead of a floor.")]
    [SerializeField, Min(0f)] float maxStepUp = 2.2f;
    [SerializeField, Range(0.1f, 1f)] float minFloorNormalY = 0.7f;
    [Tooltip("Seconds used to ease into a new floor height. Increase for a softer rise and descent on stairs.")]
    [SerializeField, Min(0.02f)] float followSmoothTime = 0.12f;
    [Tooltip("Maximum speed when following floor height, in world units per second.")]
    [SerializeField, Min(0f)] float maxVerticalSpeed = 5f;
    [Tooltip("Height error beyond this (e.g. after a teleport) snaps instead of gliding.")]
    [SerializeField, Min(0f)] float snapDistance = 2.5f;
    [Tooltip("Optional floor roots. Empty: uses GhostFloorSurface markers, then the legacy generated/imported root names.")]
    [SerializeField] Transform[] floorRoots = null;

    CharacterController controller;
    readonly HashSet<Collider> floorColliders = new HashSet<Collider>();
    readonly RaycastHit[] probeHits = new RaycastHit[24];
    float verticalVelocity;
    float nextScanAt;
    bool hasFloor;
    float surfaceHeight;

    public float MoveSpeed { get { return moveSpeed; } }
    public CharacterController Controller { get { if (controller == null) controller = GetComponent<CharacterController>(); return controller; } }
    /// <summary>True when a floor root with probe colliders was found in the current scene.</summary>
    public bool HasFloor { get { return hasFloor; } }
    /// <summary>Last floor surface height sampled under the root.</summary>
    public float FloorSurfaceHeight { get { return surfaceHeight; } }
    public float HoverHeight { get { return hoverHeight; } }
    public int FloorColliderCount { get { return floorColliders.Count; } }
    public float MaxStepUp { get { return maxStepUp; } }

    void Awake()
    {
        if (controller == null) controller = GetComponent<CharacterController>();
    }

    void Start()
    {
        // One scan per scene load; a failed scan (floor streamed in later) is retried lazily.
        if (ScanFloor()) SnapToFloor();
        else nextScanAt = Time.time + 1f;
    }

    public CollisionFlags Move(Vector3 direction, float deltaTime)
    {
        direction.y = 0f;
        CollisionFlags flags = Sweep(Vector3.ClampMagnitude(direction, 1f) * moveSpeed * Mathf.Max(0f, deltaTime));
        FollowFloor(deltaTime);
        return flags;
    }

    public CollisionFlags MoveDisplacement(Vector3 displacement)
    {
        displacement.y = 0f; // The root never falls under gravity; only the floor follow may change its height.
        CollisionFlags flags = Sweep(displacement);
        FollowFloor(Time.deltaTime);
        return flags;
    }

    /// <summary>Explicit scene arrival/return only. Ordinary movement must always use Move.</summary>
    public void Teleport(Vector3 destination)
    {
        bool wasEnabled = Controller.enabled;
        Controller.enabled = false;
        transform.position = destination;
        Controller.enabled = wasEnabled;
        verticalVelocity = 0f;
        Physics.SyncTransforms();
        SnapToFloor();
    }

    /// <summary>Instantly places the root at hover height over the floor beneath it. No-op when no floor is under it.</summary>
    public bool SnapToFloor()
    {
        float surface;
        if (!TryProbeSurface(transform.position, out surface)) return false;
        surfaceHeight = surface;
        SetRootHeight(surface + hoverHeight);
        return true;
    }

    /// <summary>Floor surface under an arbitrary point, using the same probe and filters as the follow.</summary>
    public bool TryGetSurfaceAt(Vector3 worldPosition, out float surface)
    {
        return TryProbeSurface(worldPosition, out surface);
    }

    CollisionFlags Sweep(Vector3 displacement)
    {
        if (!isActiveAndEnabled || !Controller.enabled) return CollisionFlags.None;
        // Bounded sweeps handle long frames and thin authored walls; CharacterController handles sliding.
        int steps = Mathf.Max(1, Mathf.CeilToInt(displacement.magnitude / 0.2f));
        Vector3 step = displacement / steps;
        CollisionFlags flags = CollisionFlags.None;
        for (int i = 0; i < steps; i++) flags |= Controller.Move(step);
        return flags;
    }

    void FollowFloor(float deltaTime)
    {
        if (!hasFloor)
        {
            if (Time.time >= nextScanAt && ScanFloor()) nextScanAt = Time.time;
            return;
        }
        if (deltaTime <= 0f) return;
        float surface;
        if (!TryProbeSurface(transform.position, out surface)) return;
        surfaceHeight = surface;

        float target = surface + hoverHeight;
        float current = transform.position.y;
        if (Mathf.Abs(target - current) > snapDistance) { SetRootHeight(target); return; }
        float next = Mathf.SmoothDamp(current, target, ref verticalVelocity, followSmoothTime, maxVerticalSpeed, deltaTime);
        MoveVertical(next - current);
    }

    void MoveVertical(float delta)
    {
        if (Mathf.Abs(delta) < 1e-5f) return;
        if (!isActiveAndEnabled || !Controller.enabled) return;
        Controller.Move(new Vector3(0f, delta, 0f));
    }

    void SetRootHeight(float y)
    {
        Vector3 position = transform.position;
        position.y = y;
        bool wasEnabled = Controller.enabled;
        Controller.enabled = false;
        transform.position = position;
        Controller.enabled = wasEnabled;
        verticalVelocity = 0f;
        Physics.SyncTransforms();
    }

    bool TryProbeSurface(Vector3 rootPosition, out float surface)
    {
        surface = 0f;
        if (floorColliders.Count == 0) return false;
        Vector3 origin = rootPosition + Vector3.up * probeAbove;
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, probeHits, probeAbove + probeDepth,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
        float limit = rootPosition.y + maxStepUp;
        bool found = false;
        float best = float.NegativeInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = probeHits[i];
            if (hit.normal.y < minFloorNormalY) continue;   // vertical ledges / walls are not floors
            if (hit.point.y > limit) continue;              // wall tops and ceilings sit above the hop range
            if (!floorColliders.Contains(hit.collider)) continue;
            if (hit.point.y <= best) continue;
            best = hit.point.y;
            found = true;
        }
        if (!found) return false;
        surface = best;
        return true;
    }

    bool ScanFloor()
    {
        floorColliders.Clear();
        // Floor is declared, never guessed per-collider: inspector references, then GhostFloorSurface
        // markers, then the legacy generated/imported root names. Walls are simply not part of it.
        List<Transform> roots = GhostFloorSurface.ResolveRoots(gameObject.scene, floorRoots);
        for (int i = 0; i < roots.Count; i++)
        {
            Transform root = roots[i];
            if (root == null || root == transform || root.IsChildOf(transform)) continue;
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            for (int c = 0; c < colliders.Length; c++)
            {
                Collider collider = colliders[c];
                if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
                if (collider.transform.IsChildOf(transform)) continue;
                floorColliders.Add(collider);
            }
        }
        hasFloor = floorColliders.Count > 0;
        if (!hasFloor)
            Debug.LogWarning("GhostPlayerMotor found no floor surface. Add a GhostFloorSurface component to the floor " +
                             "object (Ghost Party > Level > Mark Selection As Floor Surface) or assign Floor Roots.", this);
        return hasFloor;
    }

}
