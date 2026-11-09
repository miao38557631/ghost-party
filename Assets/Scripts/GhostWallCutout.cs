using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

/// <summary>Reveals the floating ghost through a whole-mesh wall.
/// Hides a vertical wall section with a small feathered edge when it blocks the ghost.</summary>
[DisallowMultipleComponent]
[AddComponentMenu("Ghost Party/Ghost Wall Cutout")]
[DefaultExecutionOrder(110)]
public sealed class GhostWallCutout : MonoBehaviour
{
    [Tooltip("Wall hidden on each side of the player, measured as a fraction of screen height.")]
    [FormerlySerializedAs("screenRadius")]
    [SerializeField, Min(0.01f)] float screenHalfWidth = 0.30f;
    [Tooltip("Small soft edge, measured as a fraction of screen height.")]
    [SerializeField, Min(0f)] float edgeFeather = 0.025f;
    [Tooltip("Optional player transform. Empty: uses the scene's InvisibleGhost.")]
    [SerializeField] Transform player;
    [Tooltip("Optional camera. Empty: uses Camera.main.")]
    [SerializeField] Camera view;

    static readonly int CenterId = Shader.PropertyToID("_GhostCutPos");
    static readonly int HalfWidthId = Shader.PropertyToID("_GhostCutHalfWidth");
    static readonly int FeatherId = Shader.PropertyToID("_GhostCutFeather");
    static readonly int PlayerId = Shader.PropertyToID("_GhostCutPlayer");
    static readonly int GroundForwardId = Shader.PropertyToID("_GhostCutGroundForward");
    static readonly string[] WallNameHints = { "墙", "wall" };
    const string DecorationShaderName = "GhostParty/DecorationTransparent";

    Renderer[] renderers;
    readonly List<Collider> wallColliders = new List<Collider>();
    readonly List<MeshCollider> ownedColliders = new List<MeshCollider>();
    MaterialPropertyBlock block;
    readonly List<Material> ownedMaterials = new List<Material>();
    bool collected;
    bool warned;

    public float ScreenHalfWidth { get { return screenHalfWidth; } }
    public int RendererCount { get { if (!collected) Collect(); return renderers == null ? 0 : renderers.Length; } }

    public void Initialize(Transform targetPlayer)
    {
        player = targetPlayer;
        IgnorePlayerCollision();
    }

    void Awake()
    {
        Collect();
    }

    void OnDisable()
    {
        if (renderers == null) return;
        if (block == null) block = new MaterialPropertyBlock();
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            renderers[i].GetPropertyBlock(block);
            block.SetFloat(HalfWidthId, 0f);
            renderers[i].SetPropertyBlock(block);
        }
    }

    void OnDestroy()
    {
        for (int i = 0; i < ownedColliders.Count; i++)
            if (ownedColliders[i] != null) Destroy(ownedColliders[i]);
        for (int i = 0; i < ownedMaterials.Count; i++)
            if (ownedMaterials[i] != null) Destroy(ownedMaterials[i]);
        ownedMaterials.Clear();
    }

    void LateUpdate()
    {
        if (!collected) Collect();
        if (renderers == null || renderers.Length == 0) return;
        if (player == null)
        {
            GameObject found = GameObject.Find("InvisibleGhost");
            if (found == null) return;
            player = found.transform;
            IgnorePlayerCollision();
        }
        if (view == null) view = Camera.main;
        if (view == null) return;

        // Screen X makes the opening symmetric in the player's current view. Ground-plane depth ignores
        // wall height, so a tall wall behind the ghost cannot be mistaken for a foreground wall.
        Vector3 viewport = view.WorldToViewportPoint(player.position);
        Vector3 groundForward = Vector3.ProjectOnPlane(view.transform.forward, Vector3.up).normalized;
        bool occluded = viewport.z > 0f && WallBlocksPlayer();
        if (block == null) block = new MaterialPropertyBlock();
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            renderers[i].GetPropertyBlock(block);
            block.SetVector(CenterId, new Vector4(viewport.x, viewport.y, 0f, 0f));
            block.SetVector(PlayerId, player.position);
            block.SetVector(GroundForwardId, groundForward);
            block.SetFloat(HalfWidthId, occluded ? screenHalfWidth : 0f);
            block.SetFloat(FeatherId, edgeFeather);
            renderers[i].SetPropertyBlock(block);
        }
    }

    bool WallBlocksPlayer()
    {
        return WallBlocksPoint(player.position + Vector3.up * 0.25f) ||
               WallBlocksPoint(player.position + Vector3.up * 0.65f);
    }

    bool WallBlocksPoint(Vector3 target)
    {
        Vector3 viewport = view.WorldToViewportPoint(target);
        if (viewport.z <= 0f) return false;
        // ViewportPointToRay starts at the correct pixel for an orthographic camera.
        Ray forwardRay = view.ViewportPointToRay(viewport);
        float distance = Vector3.Dot(target - forwardRay.origin, forwardRay.direction);
        if (distance <= 0.05f) return false;
        Ray reverseRay = new Ray(target, -forwardRay.direction);
        for (int i = 0; i < wallColliders.Count; i++)
        {
            Collider wall = wallColliders[i];
            if (wall == null || !wall.enabled) continue;
            RaycastHit hit;
            if (wall.Raycast(forwardRay, out hit, distance - 0.05f) ||
                wall.Raycast(reverseRay, out hit, distance - 0.05f)) return true;
        }
        return false;
    }

    void IgnorePlayerCollision()
    {
        if (player == null) return;
        CharacterController controller = player.GetComponent<CharacterController>();
        if (controller == null) return;
        for (int i = 0; i < wallColliders.Count; i++)
            if (wallColliders[i] != null) Physics.IgnoreCollision(controller, wallColliders[i], true);
    }

    void Collect()
    {
        collected = true;
        Renderer[] found = GetComponentsInChildren<Renderer>(true);
        var kept = new List<Renderer>(found.Length);
        for (int i = 0; i < found.Length; i++)
        {
            if (found[i] == null) continue;
            // Never cut the floor, even if the wall object happens to contain it.
            if (found[i].GetComponentInParent<GhostFloorSurface>() != null) continue;
            kept.Add(found[i]);
            Collider collider = found[i].GetComponent<Collider>();
            if (collider == null)
            {
                MeshFilter filter = found[i].GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                {
                    MeshCollider queryCollider = found[i].gameObject.AddComponent<MeshCollider>();
                    queryCollider.sharedMesh = filter.sharedMesh;
                    collider = queryCollider;
                    ownedColliders.Add(queryCollider);
                }
            }
            if (collider != null) wallColliders.Add(collider);
        }
        IgnorePlayerCollision();
        renderers = kept.ToArray();
        if (renderers.Length == 0) return;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            Material[] materials = renderer.sharedMaterials;
            bool changed = false;
            for (int m = 0; m < materials.Length; m++)
            {
                Material material = materials[m];
                if (material == null || material.HasProperty(HalfWidthId)) continue;
                // The wall may still sit on Standard. Swap in a runtime copy of the decoration shader so the
                // cutout works without touching the imported asset; texture and tint names survive the swap.
                Shader cutout = Shader.Find(DecorationShaderName);
                if (cutout == null) { WarnOnce(material); continue; }
                Material upgraded = new Material(material);
                upgraded.name = material.name + " (wall cutout)";
                upgraded.shader = cutout;
                materials[m] = upgraded;
                ownedMaterials.Add(upgraded);
                changed = true;
                Debug.Log("GhostWallCutout: '" + material.shader.name + "' has no cutout support; " + renderer.name +
                          " slot " + m + " now uses " + DecorationShaderName + " at runtime.", this);
            }
            if (changed) renderer.sharedMaterials = materials;
        }
    }

    void WarnOnce(Material material)
    {
        if (warned) return;
        warned = true;
        Debug.LogWarning("GhostWallCutout: wall material '" + material.name + "' uses shader '" + material.shader.name +
                         "' and " + DecorationShaderName + " was not found. Assign a material using that shader.", this);
    }

    /// <summary>Wall object for a scene: an existing cutout component, then a root whose name says wall.</summary>
    public static Transform ResolveWallRoot(Scene scene)
    {
        if (!scene.IsValid()) scene = SceneManager.GetActiveScene();
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            GhostWallCutout existing = roots[i].GetComponentInChildren<GhostWallCutout>(true);
            if (existing != null) return existing.transform;
        }
        Transform best = null;
        int bestRenderers = 0;
        for (int i = 0; i < roots.Length; i++)
        {
            if (!roots[i].activeInHierarchy || !NameLooksLikeWall(roots[i].name)) continue;
            int count = roots[i].GetComponentsInChildren<Renderer>(true).Length;
            if (count <= bestRenderers) continue;
            best = roots[i].transform;
            bestRenderers = count;
        }
        return best;
    }

    /// <summary>Adds the cutout to the scene's wall object and points it at the player. Safe to call twice.</summary>
    public static GhostWallCutout EnsureForScene(Scene scene, Transform player)
    {
        Transform wall = ResolveWallRoot(scene);
        if (wall == null)
        {
            Debug.LogWarning("GhostWallCutout: no wall object found in the scene. Name the wall object with 墙/wall, " +
                             "or attach the component yourself (Ghost Party > Level > Attach Wall Cutout To Selection).");
            return null;
        }
        GhostWallCutout cutout = wall.GetComponent<GhostWallCutout>();
        if (cutout == null) cutout = wall.gameObject.AddComponent<GhostWallCutout>();
        cutout.Initialize(player);
        return cutout;
    }

    public static bool NameLooksLikeWall(string objectName)
    {
        if (string.IsNullOrEmpty(objectName)) return false;
        for (int i = 0; i < WallNameHints.Length; i++)
            if (objectName.IndexOf(WallNameHints[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }
}
