using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GhostParty.LevelWhitebox
{
    /// <summary>Fade only whitebox wall pieces between the camera and ghost.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class WhiteboxWallOcclusion : MonoBehaviour
    {
        const float OccludedAlpha = 0.2f;
        const float SideRayOffset = 0.22f;

        readonly RaycastHit[] hits = new RaycastHit[64];
        readonly HashSet<Renderer> blockingWalls = new HashSet<Renderer>();
        readonly Dictionary<Renderer, Material> originalMaterials = new Dictionary<Renderer, Material>();
        readonly Dictionary<Material, Material> transparentMaterials = new Dictionary<Material, Material>();
        readonly List<Renderer> restoreQueue = new List<Renderer>();

        Transform player;
        Transform wallGroup;

        public void Initialize(Transform targetPlayer, Transform targetWallGroup)
        {
            player = targetPlayer;
            wallGroup = targetWallGroup;
        }

        void LateUpdate()
        {
            if (player == null || wallGroup == null) return;

            blockingWalls.Clear();
            ScanRay(0f);
            ScanRay(-SideRayOffset);
            ScanRay(SideRayOffset);

            restoreQueue.Clear();
            foreach (var pair in originalMaterials)
            {
                if (!blockingWalls.Contains(pair.Key)) restoreQueue.Add(pair.Key);
            }
            foreach (var wall in restoreQueue)
            {
                if (wall != null) wall.sharedMaterial = originalMaterials[wall];
                originalMaterials.Remove(wall);
            }

            foreach (var wall in blockingWalls)
            {
                if (wall == null || originalMaterials.ContainsKey(wall)) continue;
                var original = wall.sharedMaterial;
                if (original == null) continue;
                originalMaterials.Add(wall, original);
                wall.sharedMaterial = GetTransparentMaterial(original);
            }
        }

        void ScanRay(float sideOffset)
        {
            Vector3 side = transform.right * sideOffset;
            Vector3 origin = transform.position + side;
            Vector3 direction = player.position + side - origin;
            float distance = direction.magnitude;
            int count = Physics.RaycastNonAlloc(
                new Ray(origin, direction / distance), hits, distance,
                ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var collider = hits[i].collider;
                if (collider == null || !collider.transform.IsChildOf(wallGroup)) continue;
                var renderer = collider.GetComponent<Renderer>();
                if (renderer != null) blockingWalls.Add(renderer);
            }
        }

        Material GetTransparentMaterial(Material original)
        {
            if (transparentMaterials.TryGetValue(original, out var transparent)) return transparent;

            transparent = new Material(original) { name = original.name + " (camera fade)" };
            transparent.SetFloat("_Mode", 3f);
            transparent.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            transparent.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            transparent.SetInt("_ZWrite", 0);
            transparent.SetOverrideTag("RenderType", "Transparent");
            transparent.DisableKeyword("_ALPHATEST_ON");
            transparent.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            transparent.EnableKeyword("_ALPHABLEND_ON");
            transparent.renderQueue = (int)RenderQueue.Transparent;
            Color color = transparent.color;
            color.a = OccludedAlpha;
            transparent.color = color;

            transparentMaterials.Add(original, transparent);
            return transparent;
        }

        void OnDestroy()
        {
            foreach (var pair in originalMaterials)
                if (pair.Key != null) pair.Key.sharedMaterial = pair.Value;
            foreach (var material in transparentMaterials.Values)
                if (material != null) Destroy(material);
        }
    }
}
