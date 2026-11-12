using UnityEngine;

/// <summary>Preserves an orthographic sprite's screen height without leaning it through nearby walls.</summary>
public static class GroundPlaneBillboard
{
    public static void Align(Transform artwork, Camera view, float baseScaleY, float rollDegrees = 0f)
    {
        if (artwork == null || view == null) return;
        Vector3 forward = Vector3.ProjectOnPlane(view.transform.forward, Vector3.up);
        // Keep the full billboard for top-down/perspective views where the vertical projection collapses.
        if (!view.orthographic || forward.sqrMagnitude < 0.09f)
        {
            artwork.rotation = view.transform.rotation * Quaternion.Euler(0f, 0f, rollDegrees);
            Vector3 fallback = artwork.localScale;
            fallback.y = baseScaleY;
            artwork.localScale = fallback;
            return;
        }
        artwork.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up) *
            Quaternion.Euler(0f, 0f, rollDegrees);
        Vector3 scale = artwork.localScale;
        scale.y = baseScaleY / Mathf.Max(0.3f, forward.magnitude);
        artwork.localScale = scale;
    }
}
