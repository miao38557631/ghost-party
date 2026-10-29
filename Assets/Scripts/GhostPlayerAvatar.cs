using UnityEngine;

/// <summary>Shared prototype ghost appearance and ground-plane movement.</summary>
public static class GhostPlayerAvatar
{
    public static Transform Create(Vector3 position)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "InvisibleGhost";
        go.transform.position = position;
        go.transform.localScale = Vector3.one * 0.76f;

        var renderer = go.GetComponent<Renderer>();
        renderer.material = new Material(Shader.Find("Unlit/Color"));
        renderer.material.color = new Color(0.48f, 0.93f, 0.95f, 0.45f);

        var glow = go.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = new Color(0.2f, 0.9f, 1f);
        glow.range = 2.5f;
        glow.intensity = 0.8f;
        return go.transform;
    }

    public static Vector3 ReadGroundInput()
    {
        var input = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
        return Vector3.ClampMagnitude(input, 1f);
    }
}
