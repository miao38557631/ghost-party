using UnityEngine;

/// <summary>Keeps an NPC's Spine artwork planted on the floor: the lowest point the idle animation reaches
/// sits exactly on the floor surface (it used to hover because the nav agent lifted the whole root).</summary>
[AddComponentMenu("Ghost Party/NPC Ground Contact")]
[DefaultExecutionOrder(200)]
[DisallowMultipleComponent]
public sealed class NpcGroundContact : MonoBehaviour
{
    [Tooltip("Artwork child to plant. Empty: uses the PrankNpcActor artwork.")]
    [SerializeField] Transform artwork;
    [Tooltip("0 = lowest animation point touches the floor. Positive lifts, negative sinks into the floor.")]
    [SerializeField] float groundOffset = 0f;
    [Tooltip("How quickly the artwork settles onto the measured floor height.")]
    [SerializeField, Min(0.05f)] float adaptSpeed = 6f;
    [SerializeField, Min(0.1f)] float probeAbove = 3f;
    [SerializeField, Min(0.5f)] float probeDepth = 8f;
    [SerializeField, Range(0.1f, 1f)] float minFloorNormalY = 0.7f;

    Renderer artworkRenderer;
    float lowestLocalBottom = float.PositiveInfinity;
    bool warnedMissingFloor;

    public float GroundOffset { get { return groundOffset; } set { groundOffset = value; } }

    public void Initialize(Transform targetArtwork, float offset)
    {
        artwork = targetArtwork;
        groundOffset = offset;
        artworkRenderer = null;
        lowestLocalBottom = float.PositiveInfinity;
    }

    void LateUpdate()
    {
        if (artwork == null)
        {
            PrankNpcActor actor = GetComponent<PrankNpcActor>();
            if (actor != null) artwork = actor.Artwork;
            if (artwork == null) return;
        }
        if (artworkRenderer == null) artworkRenderer = artwork.GetComponentInChildren<Renderer>();
        if (artworkRenderer == null) return;

        Bounds bounds = artworkRenderer.bounds;
        if (bounds.size.y <= 0.0001f) return; // mesh not generated yet

        // Distance from the artwork pivot down to its lowest drawn point, for this animation frame.
        float localBottom = bounds.min.y - artwork.position.y;
        if (localBottom < lowestLocalBottom) lowestLocalBottom = localBottom;

        float floorY;
        if (!GhostFloorProbe.TryGetSurface(transform.position, probeAbove, probeDepth, minFloorNormalY, out floorY))
        {
            if (!warnedMissingFloor)
            {
                warnedMissingFloor = true;
                Debug.LogWarning("NpcGroundContact: no floor under " + name + "; keeping the current height.", this);
            }
            return;
        }
        warnedMissingFloor = false;

        // lowestLocalBottom is the lowest the animation ever reaches, so the body may bob above the floor
        // while the lowest pose is the one actually touching it.
        float targetY = floorY + groundOffset - lowestLocalBottom;
        Vector3 position = artwork.position;
        position.y = Mathf.Lerp(position.y, targetY, 1f - Mathf.Exp(-adaptSpeed * Time.deltaTime));
        artwork.position = position;
    }
}
