using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>Replaces hand-placed NPC placeholders with randomly spawned first-floor guests.
/// Put this on an empty GameObject: the inspector exposes the count, spacing, ground offset and roaming.</summary>
[AddComponentMenu("Ghost Party/NPC Spawner")]
[DisallowMultipleComponent]
public sealed class NpcSpawner : MonoBehaviour
{
    [Header("Spawn")]
    [SerializeField, Min(0)] int npcCount = 6;
    [Tooltip("0 = a new layout every Play; any other value is reproducible.")]
    [SerializeField] int randomSeed = 0;

    [Header("Placement")]
    [Tooltip("Optional area root. Empty: every floor root found by GhostFloorSurface.")]
    [SerializeField] Transform spawnArea = null;
    [SerializeField, Min(0.5f)] float minSpacing = 2.5f;
    [SerializeField, Min(0f)] float edgeMargin = 1.5f;
    [Tooltip("Keeps NPCs away from the ghost's spawn point. 0 = off.")]
    [SerializeField, Min(0f)] float playerClearance = 3f;
    [SerializeField, Range(0.1f, 1f)] float minFloorNormalY = 0.7f;
    [SerializeField, Min(0.1f)] float probeAbove = 4f;
    [SerializeField, Min(0.5f)] float probeDepth = 10f;

    [Header("Grounding")]
    [Tooltip("0 = the lowest animation point touches the floor; positive lifts the NPC, negative sinks it.")]
    [SerializeField] float groundOffset = 0f;
    [Tooltip("Adds ground contact to NPCs already placed in the scene (for example the curtain guest).")]
    [SerializeField] bool groundSceneActors = true;

    [Header("Scene cleanup")]
    [Tooltip("Hides the hand-placed PartyGuest_* placeholders so only the spawned NPCs remain.")]
    [SerializeField] bool hideSceneGuests = true;

    [Header("Size")]
    [Tooltip("Spawned NPC scale. Ignored while Match Reference Scale is on.")]
    [SerializeField, Min(0.05f)] float npcScale = 1.5f;
    [Tooltip("Keeps spawned NPCs the same size as an NPC already in the scene instead of using Npc Scale.")]
    [SerializeField] bool matchReferenceScale = false;
    [Tooltip("Optional reference NPC. Empty: uses CurtainGuest, else any NPC in the scene.")]
    [SerializeField] Transform sizeReference = null;

    [Header("Roaming")]
    [SerializeField] bool enableRoaming = true;
    [SerializeField, Min(1f)] float roamRadius = 18f;

    readonly List<GameObject> spawned = new List<GameObject>();
    readonly List<Vector2> usedPoints = new List<Vector2>();

    public int SpawnedCount { get { return spawned.Count; } }
    public float GroundOffset { get { return groundOffset; } set { groundOffset = value; } }

    void Start()
    {
        SpawnNow();
    }

    [ContextMenu("Spawn NPCs Now")]
    public void SpawnNow()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("NpcSpawner spawns in Play mode only; the scene keeps its authored objects untouched.", this);
            return;
        }
        ClearSpawned();
        if (npcCount <= 0) return;

        Scene scene = gameObject.scene;
        if (!scene.IsValid()) scene = SceneManager.GetActiveScene();

        if (hideSceneGuests) HideSceneGuests(scene);
        if (groundSceneActors) GroundSceneActors();

        var roots = new List<Transform>();
        if (spawnArea != null) roots.Add(spawnArea);
        else roots.AddRange(GhostFloorProbe.Roots(scene));
        if (roots.Count == 0)
        {
            Debug.LogError("NpcSpawner: no floor root found. Name the floor with 'floor' or add a GhostFloorSurface.", this);
            return;
        }

        Bounds area;
        if (!TryGetAreaBounds(roots, out area))
        {
            Debug.LogError("NpcSpawner: the referenced spawn area has no renderer or collider bounds.", this);
            return;
        }

        Vector3 playerPosition = Vector3.zero;
        bool hasPlayer = false;
        GameObject player = GameObject.Find("InvisibleGhost");
        if (player != null) { playerPosition = player.transform.position; hasPlayer = true; }

        System.Random random = new System.Random(randomSeed != 0 ? randomSeed : Environment.TickCount);
        float minX = area.min.x + edgeMargin, maxX = area.max.x - edgeMargin;
        float minZ = area.min.z + edgeMargin, maxZ = area.max.z - edgeMargin;
        usedPoints.Clear();
        float spawnScale = ResolveSpawnScale();

        int placed = 0;
        int attempts = 0;
        int maxAttempts = Mathf.Max(64, npcCount * 120);
        while (placed < npcCount && attempts++ < maxAttempts)
        {
            float x = Mathf.Lerp(minX, maxX, (float)random.NextDouble());
            float z = Mathf.Lerp(minZ, maxZ, (float)random.NextDouble());
            float surface;
            if (!GhostFloorProbe.TryGetSurface(new Vector3(x, area.center.y, z), probeAbove, probeDepth, minFloorNormalY, out surface)) continue;

            Vector2 point = new Vector2(x, z);
            bool tooClose = false;
            for (int i = 0; i < usedPoints.Count; i++)
                if ((usedPoints[i] - point).sqrMagnitude < minSpacing * minSpacing) { tooClose = true; break; }
            if (tooClose) continue;
            if (hasPlayer && playerClearance > 0f &&
                (new Vector2(playerPosition.x, playerPosition.z) - point).sqrMagnitude < playerClearance * playerClearance) continue;

            SpawnOne(placed, new Vector3(x, surface, z), spawnScale);
            usedPoints.Add(point);
            placed++;
        }

        if (placed < npcCount)
            Debug.LogWarning("NpcSpawner: placed " + placed + "/" + npcCount + " NPCs; the floor may be too small for the requested spacing.", this);
        Debug.Log("NPC_SPAWNED: " + placed + " on " + scene.name + " (roaming=" + enableRoaming +
                  ", groundOffset=" + groundOffset + ", scale=" + spawnScale.ToString("0.00") + ")");
    }

    [ContextMenu("Clear Spawned NPCs")]
    public void ClearSpawned()
    {
        for (int i = 0; i < spawned.Count; i++)
            if (spawned[i] != null) Destroy(spawned[i]);
        spawned.Clear();
    }

    void SpawnOne(int index, Vector3 position, float spawnScale)
    {
        GameObject npc = new GameObject("NPC_Random_" + index.ToString("00"));
        npc.transform.SetParent(transform, true);
        npc.transform.position = position;
        // Match the authored NPCs (the curtain guest is scaled up) instead of the prefab default of 1.
        float parentScale = Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.y));
        npc.transform.localScale = Vector3.one * (spawnScale / parentScale);

        PrankNpcActor actor = npc.AddComponent<PrankNpcActor>();
        actor.CreatePlaceholderBody = false; // no primitive sphere stand-ins; the Spine artwork is the body

        NpcGroundContact contact = npc.AddComponent<NpcGroundContact>();
        contact.Initialize(null, groundOffset); // artwork is resolved from the actor once Spine loads

        if (enableRoaming) AddRoaming(npc);
        spawned.Add(npc);
    }

    /// <summary>World scale for spawned NPCs: the authoring reference (curtain guest by default), or the manual value.</summary>
    float ResolveSpawnScale()
    {
        if (!matchReferenceScale) return npcScale;
        if (sizeReference == null)
        {
            GameObject curtainGuest = GameObject.Find("CurtainGuest");
            if (curtainGuest != null) sizeReference = curtainGuest.transform;
        }
        if (sizeReference == null)
        {
            PrankNpcActor[] actors = UnityEngine.Object.FindObjectsOfType<PrankNpcActor>();
            for (int i = 0; i < actors.Length; i++)
            {
                if (actors[i] == null || !actors[i].gameObject.activeInHierarchy) continue;
                sizeReference = actors[i].transform;
                break;
            }
        }
        return sizeReference != null ? Mathf.Abs(sizeReference.lossyScale.y) : npcScale;
    }

    void AddRoaming(GameObject npc)
    {
        NavMeshHit hit;
        if (!NavMesh.SamplePosition(npc.transform.position, out hit, 3f, NavMesh.AllAreas)) return;
        npc.transform.position = hit.position;

        NavMeshAgent agent = npc.AddComponent<NavMeshAgent>();
        agent.radius = 0.35f;
        agent.height = 1.6f;
        agent.baseOffset = 0f; // the ground contact plants the artwork, not the agent
        agent.speed = 2.2f;
        agent.acceleration = 8f;
        agent.angularSpeed = 360f;
        agent.stoppingDistance = 0.15f;
        agent.autoBraking = true;
        agent.updateRotation = false;

        NpcNavMover mover = npc.AddComponent<NpcNavMover>();
        mover.SetRandomRoam(true);
        mover.SetRoamRadius(roamRadius);
    }

    static void HideSceneGuests(Scene scene)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
            if (roots[i].name.StartsWith("PartyGuest_", StringComparison.Ordinal)) roots[i].SetActive(false);
    }

    void GroundSceneActors()
    {
        PrankNpcActor[] actors = UnityEngine.Object.FindObjectsOfType<PrankNpcActor>(true);
        for (int i = 0; i < actors.Length; i++)
        {
            if (actors[i] == null || !actors[i].gameObject.activeInHierarchy) continue;
            if (actors[i].GetComponent<NpcGroundContact>() != null) continue;
            NpcGroundContact contact = actors[i].gameObject.AddComponent<NpcGroundContact>();
            contact.Initialize(null, groundOffset);
        }
    }

    static bool TryGetAreaBounds(List<Transform> roots, out Bounds bounds)
    {
        bool any = false;
        bounds = new Bounds(Vector3.zero, Vector3.zero);
        for (int i = 0; i < roots.Count; i++)
        {
            if (roots[i] == null) continue;
            Renderer[] renderers = roots[i].GetComponentsInChildren<Renderer>();
            for (int r = 0; r < renderers.Length; r++)
            {
                if (renderers[r] == null || !renderers[r].enabled) continue;
                if (!any) { bounds = renderers[r].bounds; any = true; }
                else bounds.Encapsulate(renderers[r].bounds);
            }
            if (any) continue;
            Collider[] colliders = roots[i].GetComponentsInChildren<Collider>();
            for (int c = 0; c < colliders.Length; c++)
            {
                if (colliders[c] == null || !colliders[c].enabled) continue;
                if (!any) { bounds = colliders[c].bounds; any = true; }
                else bounds.Encapsulate(colliders[c].bounds);
            }
        }
        return any && bounds.size.x > 0.5f && bounds.size.z > 0.5f;
    }
}
