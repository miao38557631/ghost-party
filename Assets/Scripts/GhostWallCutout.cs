using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>Fades foreground wall sections selected by their actual wall planes.</summary>
[DisallowMultipleComponent]
[AddComponentMenu("Ghost Party/Ghost Wall Cutout")]
[DefaultExecutionOrder(250)]
public sealed class GhostWallCutout : MonoBehaviour
{
    public enum ToonLook { Original, Sable, CultOfTheLamb, Hybrid }

    const float OccludedAlpha = 0.3f;
    [Tooltip("Play mode: F6 original, F7 Sable, F8 Cult of the Lamb, F9 Sable shadow with Cult colors.")]
    [SerializeField] ToonLook toonLook = ToonLook.Hybrid;
    [Tooltip("Temporarily angle the scene's Directional Light downward during toon previews; restored on F6 or exit.")]
    [SerializeField] bool usePreviewLight = true;
    // Legacy slide settings stay serialized so the old scene setup can be restored.
    [SerializeField, HideInInspector, Min(0f)] float remainingHeight = 0.22f;
    [Tooltip("Distance across a wall before changing sides, to prevent flicker while touching it.")]
    [SerializeField, Min(0.01f)] float sideHysteresis = 0.2f;
#pragma warning disable CS0414 // Values are retained for the commented-out sliding cutaway.
    [SerializeField, HideInInspector, Min(0f)] float lowerDuration = 0.18f;
    [SerializeField, HideInInspector, Min(0f)] float restoreDuration = 0.3f;
#pragma warning restore CS0414
    [Tooltip("Clearance around the visible ghost silhouette before lowering a wall.")]
    [SerializeField, Min(0f)] float silhouettePadding = 0.15f;
    [SerializeField] Transform player;
    [SerializeField] Camera view;

    const string DecorationShaderName = "GhostParty/DecorationTransparent";
    const string FadeShaderName = "GhostParty/WallOcclusionTransparent";
    const string ToonShaderName = "GhostParty/Toon/Surface";
    const string ToonFadeShaderName = "GhostParty/Toon/WallFade";
    static readonly string[] WallNameHints = { "墙", "wall" };
    static readonly int TopId = Shader.PropertyToID("_GhostWallTop");
    static readonly int ToonLookId = Shader.PropertyToID("_ToonLook");
    static readonly int SourceShaderId = Shader.PropertyToID("_GhostSourceShader");

    sealed class Wall
    {
        public MeshRenderer renderer;
        public MeshFilter filter;
        public Mesh originalMesh;
        public Mesh runtimeMesh;
        public Material[] originalMaterials;
        public Material[] opaqueMaterials;
        public Material[] fadedMaterials;
        public Material[] activeMaterials;
        public ShadowCastingMode originalShadowCastingMode;
        public readonly List<WallCutawaySections.Section> sections = new List<WallCutawaySections.Section>();
        public bool initialized;
    }
    sealed class SurfacePreview
    {
        public MeshRenderer renderer;
        public Material[] originalMaterials;
        public Material[] previewMaterials;
        public ShadowCastingMode originalShadowCastingMode;
        public bool originalReceiveShadows;
        public bool hasPaintedPlane;
    }
    sealed class PreviewLight
    {
        public Light light;
        public Quaternion rotation;
        public LightShadows shadows;
    }
    readonly List<Wall> walls = new List<Wall>();
    readonly List<Material> ownedMaterials = new List<Material>();
    readonly List<Material> ownedFadedMaterials = new List<Material>();
    readonly Dictionary<Material, Shader> originalWallShaders = new Dictionary<Material, Shader>();
    readonly List<SurfacePreview> surfacePreviews = new List<SurfacePreview>();
    readonly List<PreviewLight> previewLights = new List<PreviewLight>();
    readonly List<Collider> wallColliders = new List<Collider>();
    readonly List<MeshCollider> ownedColliders = new List<MeshCollider>();
    bool collected;
    ToonLook appliedLook = (ToonLook)(-1);
    bool appliedPreviewLight;
    Renderer playerArtwork;

    public float RemainingHeight { get { return remainingHeight; } }
    public ToonLook Look { get { return toonLook; } set { toonLook = value; } }
    public float OccludedOpacity { get { return OccludedAlpha; } }
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

    void Awake()
    {
        if (!Application.isPlaying) return;
        if (toonLook == ToonLook.Original) toonLook = ToonLook.Hybrid;
        Collect();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F6)) toonLook = ToonLook.Original;
        if (Input.GetKeyDown(KeyCode.F7)) toonLook = ToonLook.Sable;
        if (Input.GetKeyDown(KeyCode.F8)) toonLook = ToonLook.CultOfTheLamb;
        if (Input.GetKeyDown(KeyCode.F9)) toonLook = ToonLook.Hybrid;
    }

    void LateUpdate()
    {
        if (!collected) Collect();
        if (toonLook != appliedLook || usePreviewLight != appliedPreviewLight) ApplyLook();
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
        foreach (Wall wall in walls)
        {
            if (wall.renderer == null) continue;
            Transform owner = wall.renderer.transform;
            Matrix4x4 normalMatrix = owner.localToWorldMatrix.inverse.transpose;
            bool materialsChanged = false;
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
                // Former sliding cutaway, kept for a possible return to the height effect:
                // float low = wall.renderer.bounds.min.y + remainingHeight;
                // float high = wall.renderer.bounds.max.y + 0.02f;
                // float target = section.lowered ? low : high;
                // float duration = section.lowered ? lowerDuration : restoreDuration;
                // section.visibleTop = !wall.initialized || duration <= 0f ? target :
                //     Mathf.MoveTowards(section.visibleTop, target, (high - low) * Time.unscaledDeltaTime / duration);
                // wall.renderer.GetPropertyBlock(block, i);
                // block.SetFloat(EnabledId, 1f);
                // block.SetFloat(TopId, section.visibleTop);
                // wall.renderer.SetPropertyBlock(block, i);
                Material desired = section.lowered ? wall.fadedMaterials[i] : wall.opaqueMaterials[i];
                if (wall.activeMaterials[i] == desired) continue;
                wall.activeMaterials[i] = desired;
                materialsChanged = true;
            }
            if (materialsChanged) wall.renderer.sharedMaterials = wall.activeMaterials;
            wall.initialized = true;
        }
    }

    void Collect()
    {
        if (collected) return;
        Shader shader = Shader.Find(DecorationShaderName);
        Shader fadeShader = Shader.Find(FadeShaderName);
        if (shader == null || fadeShader == null)
        {
            Debug.LogError("GhostWallCutout: missing wall shader; leaving the wall unchanged.", this);
            return;
        }
        collected = true;
        foreach (MeshRenderer renderer in GetComponentsInChildren<MeshRenderer>(true))
        {
            if (renderer.GetComponentInParent<GhostFloorSurface>() != null) continue;
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;
            var wall = new Wall { renderer = renderer, filter = filter, originalMesh = filter.sharedMesh,
                originalMaterials = renderer.sharedMaterials,
                originalShadowCastingMode = renderer.shadowCastingMode };
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
                Material source = supported[m];
                if (source == null) continue;
                Material copy = new Material(source) { name = source.name + " (wall preview)" };
                if (!source.HasProperty(TopId)) copy.shader = shader;
                supported[m] = copy;
                originalWallShaders.Add(copy, shader);
                ownedMaterials.Add(copy);
            }
            for (int i = 0; i < materials.Length; i++)
                materials[i] = supported.Length == 0 ? null : supported[Mathf.Min(wall.sections[i].materialIndex, supported.Length - 1)];
            Material[] fadedMaterials = new Material[materials.Length];
            var fadedByOpaque = new Dictionary<Material, Material>();
            for (int i = 0; i < materials.Length; i++)
            {
                Material opaque = materials[i];
                if (opaque == null) continue;
                if (!fadedByOpaque.TryGetValue(opaque, out Material faded))
                {
                    faded = new Material(opaque) { shader = fadeShader, name = opaque.name + " (30% wall fade)" };
                    // Draw after the ghost and other ordinary transparencies so
                    // the backdrop capture includes them before fading the wall.
                    faded.renderQueue = (int)RenderQueue.Transparent + 100;
                    Color tint = faded.color;
                    tint.a *= OccludedAlpha;
                    faded.color = tint;
                    ownedMaterials.Add(faded);
                    ownedFadedMaterials.Add(faded);
                    fadedByOpaque.Add(opaque, faded);
                }
                fadedMaterials[i] = faded;
            }
            wall.opaqueMaterials = materials;
            wall.fadedMaterials = fadedMaterials;
            wall.activeMaterials = (Material[])materials.Clone();
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
        CollectSurfacePreviews();
        CollectPreviewLights();
        ApplyLook();
        IgnorePlayerCollision();
    }

    void CollectSurfacePreviews()
    {
        var copies = new Dictionary<Material, Material>();
        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
        foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (renderer.transform.IsChildOf(transform)) continue;
            Material[] originals = renderer.sharedMaterials;
            Material[] previews = (Material[])originals.Clone();
            bool eligible = false;
            bool paintedPlane = false;
            for (int i = 0; i < originals.Length; i++)
            {
                Material source = originals[i];
                if (source == null || source.shader == null) continue;
                string shaderName = source.shader.name;
                if (shaderName != "Standard" && shaderName != DecorationShaderName && shaderName != ToonShaderName) continue;
                if (OriginalShaderFor(source).name == DecorationShaderName) paintedPlane = true;
                if (!copies.TryGetValue(source, out Material copy))
                {
                    copy = new Material(source) { name = source.name + " (toon preview)" };
                    copies.Add(source, copy);
                    ownedMaterials.Add(copy);
                }
                previews[i] = copy;
                eligible = true;
            }
            if (eligible)
                surfacePreviews.Add(new SurfacePreview { renderer = renderer,
                    originalMaterials = originals, previewMaterials = previews,
                    originalShadowCastingMode = renderer.shadowCastingMode,
                    originalReceiveShadows = renderer.receiveShadows,
                    hasPaintedPlane = paintedPlane });
        }
    }

    void ApplyLook()
    {
        Shader surfaceShader = Shader.Find(ToonShaderName);
        Shader fadedShader = Shader.Find(ToonFadeShaderName);
        Shader originalFadeShader = Shader.Find(FadeShaderName);
        if (toonLook != ToonLook.Original && (surfaceShader == null || fadedShader == null))
        {
            Debug.LogError("GhostWallCutout: missing toon shaders; retaining the original look.", this);
            toonLook = ToonLook.Original;
        }
        foreach (var pair in originalWallShaders)
        {
            pair.Key.shader = toonLook == ToonLook.Original ? pair.Value : surfaceShader;
            if (toonLook != ToonLook.Original)
                pair.Key.SetFloat(ToonLookId, (float)toonLook - 1f);
        }
        foreach (Wall wall in walls)
            if (wall.renderer != null)
                wall.renderer.shadowCastingMode = toonLook == ToonLook.Original
                    ? wall.originalShadowCastingMode : ShadowCastingMode.Off;
        foreach (Material faded in ownedFadedMaterials)
        {
            faded.shader = toonLook == ToonLook.Original ? originalFadeShader : fadedShader;
            if (toonLook != ToonLook.Original)
                faded.SetFloat(ToonLookId, (float)toonLook - 1f);
        }
        foreach (SurfacePreview preview in surfacePreviews)
        {
            if (preview.renderer == null) continue;
            for (int i = 0; i < preview.previewMaterials.Length; i++)
            {
                Material material = preview.previewMaterials[i];
                if (material == preview.originalMaterials[i]) continue;
                material.shader = toonLook == ToonLook.Original
                    ? OriginalShaderFor(preview.originalMaterials[i]) : surfaceShader;
                if (toonLook != ToonLook.Original)
                    material.SetFloat(ToonLookId, (float)toonLook - 1f);
            }
            Material[] current = preview.renderer.sharedMaterials;
            if (!Matches(current, preview.originalMaterials) && !Matches(current, preview.previewMaterials)) continue;
            preview.renderer.sharedMaterials = preview.previewMaterials;
            // The painted props are single planes. Their prefabs disable shadow
            // casting, so turn on two-sided silhouette shadows only for preview.
            bool castPaintedShadow = preview.hasPaintedPlane && toonLook != ToonLook.Original;
            preview.renderer.shadowCastingMode = castPaintedShadow
                ? ShadowCastingMode.TwoSided : preview.originalShadowCastingMode;
            preview.renderer.receiveShadows = castPaintedShadow || preview.originalReceiveShadows;
        }
        SetPreviewLights();
        appliedLook = toonLook;
        appliedPreviewLight = usePreviewLight;
    }

    void CollectPreviewLights()
    {
        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
        foreach (Light light in root.GetComponentsInChildren<Light>(true))
            if (light.name == "Directional Light" && light.type == LightType.Directional)
                previewLights.Add(new PreviewLight { light = light,
                    rotation = light.transform.rotation, shadows = light.shadows });
    }

    void SetPreviewLights()
    {
        foreach (PreviewLight preview in previewLights)
        {
            if (preview.light == null) continue;
            bool sample = usePreviewLight && toonLook != ToonLook.Original;
            preview.light.transform.rotation = sample ? Quaternion.Euler(50f, -25f, 0f) : preview.rotation;
            preview.light.shadows = sample ? LightShadows.Soft : preview.shadows;
        }
    }

    void RestorePreviewLights()
    {
        foreach (PreviewLight preview in previewLights)
        {
            if (preview.light == null) continue;
            preview.light.transform.rotation = preview.rotation;
            preview.light.shadows = preview.shadows;
        }
    }

    static bool Matches(Material[] a, Material[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    static Shader OriginalShaderFor(Material source)
    {
        if (source == null || source.shader == null || source.shader.name != ToonShaderName || !source.HasProperty(SourceShaderId))
            return source != null ? source.shader : null;
        float originalKind = source.GetFloat(SourceShaderId);
        if (originalKind < -0.5f) return source.shader;
        return Shader.Find(originalKind > 0.5f ? "Standard" : DecorationShaderName) ?? source.shader;
    }

    void RestoreSurfacePreviews()
    {
        foreach (SurfacePreview preview in surfacePreviews)
            if (preview.renderer != null)
            {
                if (Matches(preview.renderer.sharedMaterials, preview.previewMaterials))
                    preview.renderer.sharedMaterials = preview.originalMaterials;
                preview.renderer.shadowCastingMode = preview.originalShadowCastingMode;
                preview.renderer.receiveShadows = preview.originalReceiveShadows;
            }
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
        RestoreSurfacePreviews();
        RestorePreviewLights();
        appliedLook = (ToonLook)(-1);
        foreach (Wall wall in walls)
        {
            wall.initialized = false;
            if (wall.renderer == null) continue;
            wall.renderer.shadowCastingMode = wall.originalShadowCastingMode;
            wall.renderer.sharedMaterials = wall.opaqueMaterials;
            Array.Copy(wall.opaqueMaterials, wall.activeMaterials, wall.opaqueMaterials.Length);
        }
    }

    void OnDestroy()
    {
        RestoreSurfacePreviews();
        RestorePreviewLights();
        foreach (Wall wall in walls)
        {
            if (wall.renderer != null)
            {
                wall.renderer.sharedMaterials = wall.originalMaterials;
                wall.renderer.shadowCastingMode = wall.originalShadowCastingMode;
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
