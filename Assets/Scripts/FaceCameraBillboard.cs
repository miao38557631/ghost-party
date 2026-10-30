using UnityEngine;

/// <summary>Keeps a world-space picture and its collider parallel to the game camera.</summary>
[DisallowMultipleComponent]
public sealed class FaceCameraBillboard : MonoBehaviour
{
    [Tooltip("Turn off for pictures placed flat against a wall.")]
    public bool faceCamera = true;

    void OnEnable() { Camera.onPreCull += AlignBeforeRender; }
    void OnDisable() { Camera.onPreCull -= AlignBeforeRender; }

    void LateUpdate()
    {
        if (!faceCamera) return;
        var camera = Camera.main;
        if (camera != null) transform.rotation = camera.transform.rotation;
    }

    void AlignBeforeRender(Camera renderingCamera)
    {
        if (!faceCamera || renderingCamera != Camera.main) return;
        transform.rotation = renderingCamera.transform.rotation;
    }
}
