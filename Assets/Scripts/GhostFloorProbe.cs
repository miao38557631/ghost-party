using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Shared "where is the floor" query for anything that has to sit on the level floor
/// (the floating ghost, NPC placement, NPC ground contact). Floor sources come from GhostFloorSurface.</summary>
public static class GhostFloorProbe
{
    static Scene cachedScene;
    static float nextRefresh;
    static readonly List<Collider> floorColliders = new List<Collider>();
    static readonly RaycastHit[] hits = new RaycastHit[24];

    /// <summary>Floor roots for a scene: inspector references, markers, then floor-looking names.</summary>
    public static List<Transform> Roots(Scene scene)
    {
        return GhostFloorSurface.ResolveRoots(scene, null);
    }

    /// <summary>Highest upward-facing floor surface under a point. False when nothing floor-like is below.</summary>
    public static bool TryGetSurface(Vector3 worldPosition, float probeAbove, float probeDepth, float minNormalY, out float surface)
    {
        surface = 0f;
        Refresh(SceneManager.GetActiveScene());
        if (floorColliders.Count == 0) return false;

        Vector3 origin = worldPosition + Vector3.up * probeAbove;
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, hits, probeAbove + probeDepth,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
        bool found = false;
        float best = float.NegativeInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.normal.y < minNormalY) continue;        // walls and pit sides are not floor
            if (!floorColliders.Contains(hit.collider)) continue;
            if (hit.point.y <= best) continue;
            best = hit.point.y;
            found = true;
        }
        if (!found) return false;
        surface = best;
        return true;
    }

    /// <summary>Drops cached colliders so a re-authored floor is picked up without restarting.</summary>
    public static void Invalidate()
    {
        cachedScene = default(Scene);
        nextRefresh = 0f;
        floorColliders.Clear();
    }

    static void Refresh(Scene scene)
    {
        if (scene == cachedScene && Time.time < nextRefresh && floorColliders.Count > 0) return;
        cachedScene = scene;
        nextRefresh = Time.time + 2f;
        floorColliders.Clear();

        List<Transform> roots = Roots(scene);
        for (int i = 0; i < roots.Count; i++)
        {
            if (roots[i] == null) continue;
            AddQueryColliders(roots[i]);
            Collider[] colliders = roots[i].GetComponentsInChildren<Collider>(true);
            for (int c = 0; c < colliders.Length; c++)
            {
                Collider collider = colliders[c];
                if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
                floorColliders.Add(collider);
            }
        }
    }

    static void AddQueryColliders(Transform root)
    {
        MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < filters.Length; i++)
        {
            MeshFilter filter = filters[i];
            if (filter == null || filter.sharedMesh == null || !filter.gameObject.activeInHierarchy) continue;
            if (filter.GetComponent<Collider>() != null) continue;
            MeshCollider collider = filter.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
            // Query-only: floor geometry must never block movement that is meant to glide over it.
            collider.isTrigger = true;
        }
    }
}
