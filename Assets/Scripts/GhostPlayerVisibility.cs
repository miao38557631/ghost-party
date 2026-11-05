using System.Collections.Generic;
using UnityEngine;

/// <summary>One presentation-only visibility entry for any possession target. Keeps the player root alive.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(200)]
public sealed class GhostPlayerVisibility : MonoBehaviour
{
    readonly Dictionary<Renderer, bool> renderers = new Dictionary<Renderer, bool>();
    readonly Dictionary<Light, bool> lights = new Dictionary<Light, bool>();
    readonly Dictionary<TrailRenderer, bool> trails = new Dictionary<TrailRenderer, bool>();
    public bool IsVisible { get; private set; } = true;

    public void SetVisible(bool visible)
    {
        if (visible == IsVisible) return; // Internal retries must not overwrite the original enabled states.
        if (!visible)
        {
            renderers.Clear(); lights.Clear(); trails.Clear();
            foreach (var renderer in GetComponentsInChildren<Renderer>(true)) renderers[renderer] = renderer.enabled;
            foreach (var light in GetComponentsInChildren<Light>(true)) lights[light] = light.enabled;
            foreach (var trail in GetComponentsInChildren<TrailRenderer>(true))
            { trails[trail] = trail.emitting; trail.emitting = false; trail.Clear(); }
            IsVisible = false;
            EnforceHidden(); // Synchronous: hide on the same frame possession is committed.
        }
        else
        {
            foreach (var item in renderers) if (item.Key != null) item.Key.enabled = item.Value;
            foreach (var item in lights) if (item.Key != null) item.Key.enabled = item.Value;
            foreach (var item in trails)
                if (item.Key != null) { item.Key.Clear(); item.Key.emitting = item.Value; }
            IsVisible = true;
            renderers.Clear(); lights.Clear(); trails.Clear();
        }
        // Animator, colliders, scripts, root position/scale and facing state are deliberately untouched.
    }

    void LateUpdate() { if (!IsVisible) EnforceHidden(); }

    void EnforceHidden()
    {
        foreach (var item in renderers) if (item.Key != null) item.Key.enabled = false;
        foreach (var item in lights) if (item.Key != null) item.Key.enabled = false;
        foreach (var item in trails) if (item.Key != null) item.Key.emitting = false;
    }
}
