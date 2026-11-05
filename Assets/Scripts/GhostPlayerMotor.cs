using UnityEngine;

/// <summary>Single 3D collision-aware motor for the fixed-height 2.5D player. Input remains in existing gameplay.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public sealed class GhostPlayerMotor : MonoBehaviour
{
    [SerializeField, Min(0f)] float moveSpeed = 4.5f;
    CharacterController controller;
    public float MoveSpeed { get { return moveSpeed; } }
    public CharacterController Controller { get { if (controller == null) controller = GetComponent<CharacterController>(); return controller; } }

    public CollisionFlags Move(Vector3 direction, float deltaTime)
    {
        direction.y = 0f;
        return MoveDisplacement(Vector3.ClampMagnitude(direction, 1f) * moveSpeed * Mathf.Max(0f, deltaTime));
    }

    public CollisionFlags MoveDisplacement(Vector3 displacement)
    {
        displacement.y = 0f; // Preserve the existing floating exploration plane; no second gravity/physics system.
        if (!isActiveAndEnabled || !Controller.enabled) return CollisionFlags.None;
        // Bounded sweeps handle long frames and thin authored walls; CharacterController handles sliding.
        int steps = Mathf.Max(1, Mathf.CeilToInt(displacement.magnitude / 0.2f));
        Vector3 step = displacement / steps;
        CollisionFlags flags = CollisionFlags.None;
        for (int i = 0; i < steps; i++) flags |= Controller.Move(step);
        return flags;
    }

    /// <summary>Explicit scene arrival/return only. Ordinary movement must always use Move.</summary>
    public void Teleport(Vector3 destination)
    {
        bool wasEnabled = Controller.enabled;
        Controller.enabled = false;
        transform.position = destination;
        Controller.enabled = wasEnabled;
        Physics.SyncTransforms();
    }
}
