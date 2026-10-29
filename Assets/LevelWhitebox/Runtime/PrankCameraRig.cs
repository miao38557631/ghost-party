using UnityEngine;

namespace GhostParty.LevelWhitebox
{
    /// <summary>Reusable map follow, edge turn and prank close-up camera.</summary>
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public sealed class PrankCameraRig : MonoBehaviour
    {
        Camera view;
        Transform player;
        Transform closeupAnchor;
        WhiteboxCamera settings;
        Quaternion mapRotation;
        Vector3 velocity;
        float leftX;
        float rightX;
        float impactStartedAt = -1f;
        float impactStrength;

        public void Initialize(Transform followedPlayer, WhiteboxCamera cameraSettings, float floorLeft, float floorRight)
        {
            view = GetComponent<Camera>();
            player = followedPlayer;
            settings = cameraSettings;
            leftX = floorLeft;
            rightX = floorRight;
            mapRotation = transform.rotation;
            view.orthographic = true;
            view.orthographicSize = 3.8f;
            transform.position = player.position - transform.forward * 19.5f;
        }

        public void EnterCloseup(Transform anchor)
        {
            closeupAnchor = anchor;
            velocity = Vector3.zero;
        }

        public void ExitCloseup()
        {
            closeupAnchor = null;
            velocity = Vector3.zero;
        }

        public void PulseImpact(float strength)
        {
            impactStartedAt = Time.time;
            impactStrength = strength;
        }

        void LateUpdate()
        {
            if (player == null || view == null) return;
            Vector3 targetPosition;
            Quaternion targetRotation;
            float targetSize;
            float speed;
            if (closeupAnchor != null)
            {
                Vector3 focus = closeupAnchor.position + new Vector3(0.85f, -0.75f, 0f);
                targetPosition = focus + new Vector3(5f, 4.2f, -5.2f);
                targetRotation = Quaternion.LookRotation(focus - targetPosition, Vector3.up);
                targetSize = 2.9f;
                speed = 8f;
            }
            else
            {
                float halfWidth = Mathf.Max(0.01f, (rightX - leftX) * 0.5f);
                float middle = (leftX + rightX) * 0.5f;
                float horizontal = Mathf.Clamp((player.position.x - middle) / halfWidth, -1f, 1f);
                float edge = Mathf.InverseLerp(0.4f, 0.85f, Mathf.Abs(horizontal));
                edge = edge * edge * (3f - 2f * edge);
                float degrees = settings == null ? 30f : horizontal < 0f ? settings.leftTurnDegrees : settings.rightTurnDegrees;
                targetRotation = Quaternion.Euler(0f, Mathf.Sign(horizontal) * degrees * edge, 0f) * mapRotation;
                targetPosition = player.position - targetRotation * Vector3.forward * 19.5f;
                targetSize = 3.8f;
                speed = 3.5f;
            }
            float blend = 1f - Mathf.Exp(-speed * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, blend);
            Vector3 position = Vector3.SmoothDamp(transform.position, targetPosition, ref velocity,
                closeupAnchor != null ? 0.12f : 0.18f);
            float impact = impactStartedAt < 0f ? 0f : Mathf.Clamp01(1f - (Time.time - impactStartedAt) / 0.28f);
            position += transform.right * (Mathf.Sin(impact * 12f) * 0.09f * impactStrength * impact);
            transform.position = position;
            view.orthographicSize = Mathf.Lerp(view.orthographicSize, targetSize, blend)
                - impact * 0.18f * impactStrength;
        }
    }
}
