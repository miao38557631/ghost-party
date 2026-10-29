using UnityEngine;

namespace GhostParty.LevelWhitebox
{
    // Preview controls plus per-level gameplay camera tuning.
    [RequireComponent(typeof(Camera))]
    public sealed class WhiteboxCamera : MonoBehaviour
    {
        public float speed = 8f;
        [Header("Gameplay edge turn")]
        [Range(0f, 90f)] public float leftTurnDegrees = 30f;
        [Range(0f, 90f)] public float rightTurnDegrees = 30f;
        Vector3 overviewPosition;
        Quaternion overviewRotation;
        Camera view;
        void Awake()
        {
            view = GetComponent<Camera>();
            overviewPosition = transform.position;
            overviewRotation = transform.rotation;
        }
        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                view.orthographic = true;
                transform.SetPositionAndRotation(overviewPosition, overviewRotation);
            }
            if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                view.orthographic = true;
                transform.SetPositionAndRotation(new Vector3(0, 25, 0), Quaternion.Euler(90, 0, 0));
            }
            if (Input.GetMouseButton(1))
            {
                view.orthographic = false;
                transform.Rotate(Vector3.up, Input.GetAxis("Mouse X") * 2, Space.World);
                transform.Rotate(Vector3.right, -Input.GetAxis("Mouse Y") * 2, Space.Self);
                Vector3 move = transform.right * Input.GetAxisRaw("Horizontal") + transform.forward * Input.GetAxisRaw("Vertical");
                if (Input.GetKey(KeyCode.E)) move += Vector3.up;
                if (Input.GetKey(KeyCode.Q)) move -= Vector3.up;
                transform.position += Vector3.ClampMagnitude(move, 1) * speed * (Input.GetKey(KeyCode.LeftShift) ? 2 : 1) * Time.unscaledDeltaTime;
            }
            float scroll = Input.mouseScrollDelta.y;
            if (view.orthographic) view.orthographicSize = Mathf.Clamp(view.orthographicSize - scroll, 3, 30);
            else transform.position += transform.forward * scroll;
        }
        void OnGUI()
        {
            GUI.Box(new Rect(12, 12, 480, 30), "WHITEBOX ONLY | 1: overview  2: plan  RMB+WASD/QE: fly  Wheel: zoom");
        }
    }
}
