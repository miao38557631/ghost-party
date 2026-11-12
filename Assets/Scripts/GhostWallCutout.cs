using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Low foreground walls, selected by real wall planes rather than a moving hole in the image.</summary>
[DisallowMultipleComponent]
[AddComponentMenu("Ghost Party/Ghost Wall Cutout")]
[DefaultExecutionOrder(250)]
public sealed class GhostWallCutout : MonoBehaviour
{
    [Tooltip("Height left above each wall mesh's base. Rear walls always keep their full height.")]
    [SerializeField, Min(0f)] float remainingHeight = 0.22f;
    [Tooltip("Distance across a wall before changing sides, to prevent flicker while touching it.")]
    [SerializeField, Min(0.01f)] float sideHysteresis = 0.2f;
    [Tooltip("Seconds to lower an obstructing wall. A whole wall section moves together.")]
    [SerializeField, Min(0f)] float lowerDuration = 0.18f;
    [SerializeField, Min(0f)] float restoreDuration = 0.3f;
    [Tooltip("Clearance around the visible ghost silhouette before lowering a wall.")]
    [SerializeField, Min(0f)] float silhouettePadding = 0.15f;
    [SerializeField] Transform player;
    [SerializeField] Camera view;

    const string DecorationShaderName = "GhostParty/DecorationTransparent";
    static readonly string[] WallNameHints = { "墙", "wall" };
    static readonly int EnabledId = Shader.PropertyToID("_GhostCutawayEnabled");
    static readonly int TopId = Shader.PropertyToID("_GhostWallTop");

    sealed class Wall
    {
        public MeshRenderer renderer;
        public MeshFilter filter;
        public Mesh originalMesh;
        public Mesh runtimeMesh;
        public Material[] originalMaterials;
        public readonly List<WallCutawaySections.Section> sections = new List<WallCutawaySections.Section>();
        public bool initialized;
    }
    readonly List<Wall> walls = new List<Wall>();
    readonly List<Material> ownedMaterials = new List<Material>();
    readonly List<Collider> wallColliders = new List<Collider>();
    readonly List<MeshCollider> ownedColliders = new List<MeshCollider>();
    MaterialPropertyBlock block;
    bool collected;
    Renderer playerArtwork;

    public float RemainingHeight { get { return remainingHeight; } }
    public int RendererCount { get { return walls.Count; } }
    public int SectionCount
    {
        get { int count = 0; foreach (Wall wall in walls) count += wall.sections.Count; return count; }
    }
    public int LoweredSectionCount
    {
        get { int count = 0; foreach (Wall wall in walls) foreach (var section in wall.sections) if (section.lowered) count++; return count; }
    }

    public void Initialize(Transform targetPlayer)
    {
        player = targetPlayer;
        playerArtwork = null;
        if (Application.isPlaying && !collected) Collect();
        IgnorePlayerCollision();
    }

    void Awake() { if (Application.isPlaying) Collect(); }

    void LateUpdate()
    {
        if (!collected) Collect();
        if (player == null)
        {
            GameObject found = GameObject.Find("InvisibleGhost");
            if (found == null) return;
            player = found.transform;
            IgnorePlayerCollision();
        }
        if (view == null) view = Camera.main;
        if (view == null) return;
        if (playerArtwork == null)
        {
            GhostPlayerVisual visual = player.GetComponentInChildren<GhostPlayerVisual>(true);
            if (visual != null) playerArtwork = visual.GetComponentInChildren<MeshRenderer>(true);
        }
        if (block == null) block = new MaterialPropertyBlock();
        foreach (Wall wall in walls)
        {
            if (wall.renderer == null) continue;
            Transform owner = wall.renderer.transform;
            Matrix4x4 normalMatrix = owner.localToWorldMatrix.inverse.transpose;
            float low = wall.renderer.bounds.min.y + remainingHeight;
            float high = wall.renderer.bounds.max.y + 0.02f;
            for (int i = 0; i < wall.sections.Count; i++)
            {
                var section = wall.sections[i];
                Vector3 normal = normalMatrix.MultiplyVector(section.localNormal).normalized;
                Vector3 point = owner.TransformPoint(section.localPoint);
                // Orthographic rays are parallel: using camera position would incorrectly classify
                // walls at the edge of a wide screen. Height/tilt does not enter this floor-plane test.
                Vector3 direction = view.orthographic ? view.transform.forward : point - view.transform.position;
                direction = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
                float facing = Vector3.Dot(normal, direction);
                float side = Vector3.Dot(normal, player.position - point) * Mathf.Sign(facing);
                if (playerArtwork != null && playerArtwork.enabled && playerArtwork.gameObject.activeInHierarchy)
                {
                    Bounds bounds = playerArtwork.bounds;
                    float radius = Vector3.Dot(new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z)), bounds.extents);
                    side = Mathf.Max(side, Vector3.Dot(normal, bounds.center - point) * Mathf.Sign(facing) + radius);
                }
                bool vertical = Mathf.Abs(normal.y) < 0.1f;
                if (!wall.initialized)
                    section.lowered = vertical && Mathf.Abs(facing) > 0.08f && side > -silhouettePadding;
                else if (!vertical || Mathf.Abs(facing) < 0.04f || side < -silhouettePadding - sideHysteresis)
                    section.lowered = false;
                else if (Mathf.Abs(facing) > 0.08f && side > -silhouettePadding)
                    section.lowered = true;
                float target = section.lowered ? low : high;
                float duration = section.lowered ? lowerDuration : restoreDuration;
                section.visibleTop = !wall.initialized || duration <= 0f ? target :
                    Mathf.MoveTowards(section.visibleTop, target, (high - low) * Time.unscaledDeltaTime / duration);
                wall.renderer.GetPropertyBlock(block, i);
                block.SetFloat(EnabledId, 1f);
                block.SetFloat(TopId, section.visibleTop);
                wall.renderer.SetPropertyBlock(block, i);
            }
            wall.initialized = true;
        }
    }

    void Collect()
    {
        if (collected) return;
        collected = true;
        Shader shader = Shader.Find(DecorationShaderName);
        foreach (MeshRenderer renderer in GetComponentsInChildren<MeshRenderer>(true))
        {
            if (renderer.GetComponentInParent<GhostFloorSurface>() != null) continue;
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null || shader == null) continue;
            var wall = new Wall { renderer = renderer, filter = filter, originalMesh = filter.sharedMesh,
                originalMaterials = renderer.sharedMaterials };
            wall.runtimeMesh = WallCutawaySections.Create(wall.originalMesh, renderer.transform, wall.sections);
            if (wall.runtimeMesh == null)
            {
                Debug.LogWarning("GhostWallCutout: enable Read/Write on the wall model: " + renderer.name +
                    ". Leaving the original wall intact.", renderer);
                continue;
            }
            Material[] materials = new Material[wall.sections.Count];
            Material[] supported = (Material[])wall.originalMaterials.Clone();
            for (int m = 0; m < supported.Length; m++)
            {
                if (supported[m] == null || supported[m].HasProperty(TopId)) continue;
                supported[m] = new Material(supported[m]) { shader = shader };
                ownedMaterials.Add(supported[m]);
            }
            for (int i = 0; i < materials.Length; i++)
                materials[i] = supported.Length == 0 ? null : supported[Mathf.Min(wall.sections[i].materialIndex, supported.Length - 1)];
            filter.sharedMesh = wall.runtimeMesh;
            renderer.sharedMaterials = materials;
            walls.Add(wall);
            Collider collider = renderer.GetComponent<Collider>();
            if (collider == null)
            {
                MeshCollider created = renderer.gameObject.AddComponent<MeshCollider>();
                created.sharedMesh = wall.originalMesh;
                ownedColliders.Add(created);
                collider = created;
            }
            wallColliders.Add(collider);
        }
        IgnorePlayerCollision();
    }

    void IgnorePlayerCollision()
    {
        if (player == null) return;
        CharacterController controller = player.GetComponent<CharacterController>();
        if (controller == null) return;
        foreach (Collider wall in wallColliders) if (wall != null) Physics.IgnoreCollision(controller, wall, true);
    }

    void OnDisable()
    {
        if (block == null) block = new MaterialPropertyBlock();
        foreach (Wall wall in walls)
        {
            wall.initialized = false;
            if (wall.renderer == null) continue;
            for (int i = 0; i < wall.sections.Count; i++)
            {
                wall.renderer.GetPropertyBlock(block, i);
                block.SetFloat(EnabledId, 0f);
                wall.renderer.SetPropertyBlock(block, i);
            }
        }
    }

    void OnDestroy()
    {
        if (block == null) block = new MaterialPropertyBlock();
        foreach (Wall wall in walls)
        {
            if (wall.renderer != null)
            {
                for (int i = 0; i < wall.sections.Count; i++) wall.renderer.SetPropertyBlock(null, i);
                wall.renderer.sharedMaterials = wall.originalMaterials;
            }
            if (wall.filter != null) wall.filter.sharedMesh = wall.originalMesh;
            if (wall.runtimeMesh != null) Destroy(wall.runtimeMesh);
        }
        foreach (Material material in ownedMaterials) if (material != null) Destroy(material);
        foreach (MeshCollider collider in ownedColliders) if (collider != null) Destroy(collider);
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
