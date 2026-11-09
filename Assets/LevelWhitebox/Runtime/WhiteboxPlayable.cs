using UnityEngine;
using UnityEngine.SceneManagement;

namespace GhostParty.LevelWhitebox
{
    /// <summary>Shared ghost, floor links and the Level01 curtain prank.</summary>
    public sealed class WhiteboxPlayable : MonoBehaviour
    {
        const string FirstFloor = "Assets/LevelWhitebox/Scenes/Level01_FirstFloor.unity";
        const string SecondFloor = "Assets/LevelWhitebox/Scenes/Level02_SecondFloor.unity";
        const float InteractionRange = 1.35f;
        // Spawn offset from the Level01 curtain: in front of it, slightly to the right.
        // Horizontal distance ~2.06 m so the player starts with some breathing room.
        static readonly Vector3 CurtainSpawnOffset = new Vector3(1.9f, 0f, -0.8f);

        static bool arriveAtStair;
        [SerializeField] Transform player;
        [SerializeField] Transform stair;
        [SerializeField] Transform curtainSpot;
        [SerializeField] LevelCurtainEncounter curtainPrank;
        [SerializeField] Camera followCamera;
        [SerializeField] Transform floorBoundsSource;
        WhiteboxCamera cameraSettings;
        float floorLeftX;
        float floorRightX;
        bool secondFloor;

        // Both the prompt and E/A resolve this same target, including stair priority.
        public Transform CurrentInteractionTarget
        {
            get
            {
                if (!isActiveAndEnabled || player == null || GhostInput.Suspended ||
                    (curtainPrank != null && curtainPrank.IsActive)) return null;
                if (Near(stair)) return stair;
                if (!secondFloor && curtainPrank != null && curtainPrank.CanStart && Near(curtainSpot)) return curtainSpot;
                return null;
            }
        }

        public bool IsCurrentPossessionTarget(LevelCurtainEncounter encounter)
        {
            return curtainSpot != null && encounter != null && encounter == curtainPrank && CurrentInteractionTarget == curtainSpot;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RegisterSceneLoads()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.path != FirstFloor && scene.path != SecondFloor) return;
            if (FindObjectOfType<WhiteboxPlayable>() != null) return;
            new GameObject("WhiteboxPlayable_Runtime").AddComponent<WhiteboxPlayable>();
        }

        void Awake()
        {
            secondFloor = SceneManager.GetActiveScene().path == SecondFloor;
            var stairObject = GameObject.Find("stair");
            if (stair == null) stair = stairObject != null ? stairObject.transform : null;
            var curtainObject = !secondFloor ? GameObject.Find("Curtain") : null;
            if (curtainSpot == null) curtainSpot = curtainObject != null ? curtainObject.transform : null;
            if (curtainPrank == null) curtainPrank = curtainObject != null ? curtainObject.GetComponent<LevelCurtainEncounter>() : null;

            Vector3 spawn;
            if (!secondFloor && !arriveAtStair && curtainSpot != null)
                spawn = curtainSpot.position + CurtainSpawnOffset;
            else if (stair != null)
                spawn = stair.position + new Vector3(0f, 0f, 1.25f);
            else
                spawn = Vector3.zero;
            spawn.y = 0.38f;
            if (player == null) player = GhostPlayerAvatar.Create(spawn);
            if (player == null) { enabled = false; return; }
            // Only floor transitions deliberately relocate; ordinary Play respects the saved scene instance.
            if (arriveAtStair) GhostPlayerAvatar.Teleport(player, spawn);
            arriveAtStair = false;

            if (followCamera == null) followCamera = Camera.main;
            if (followCamera == null)
            {
                Debug.LogError("Whitebox floor has no MainCamera.");
                return;
            }
            cameraSettings = followCamera.GetComponent<WhiteboxCamera>();
            if (cameraSettings != null) cameraSettings.enabled = false;
            FindFloorHorizontalBounds();
            var rig = followCamera.GetComponent<PrankCameraRig>();
            if (rig == null) rig = followCamera.gameObject.AddComponent<PrankCameraRig>();
            rig.Initialize(player, cameraSettings, floorLeftX, floorRightX);
            // Separate whitebox wall pieces still fade piece by piece. The imported wall mesh uses a
            // hard opening centered on the ghost when it stands behind a foreground wall.
            var wallGroup = GameObject.Find("02_BlackPlanLines_Walls_2m_80mm");
            if (wallGroup != null)
                followCamera.gameObject.AddComponent<WhiteboxWallOcclusion>()
                    .Initialize(player, wallGroup.transform);
            else
                GhostWallCutout.EnsureForScene(gameObject.scene, player);
            Debug.Log("Whitebox playable ready on " + (secondFloor ? "Level02" : "Level01") +
                      ", spawned at " + player.position);
        }

        void Update()
        {
            if (player == null || GhostInput.Suspended) return;
            if (curtainPrank != null && curtainPrank.IsActive)
            {
                GhostPlayerAvatar.SetMovement(player, Vector3.zero);
                return;
            }
            Vector3 input = GhostPlayerAvatar.ReadGroundInput();
            Vector3 movement = CameraRelativeGroundMovement(input);
            GhostPlayerAvatar.Move(player, movement, Time.deltaTime);

            if (!GhostInput.InteractPressed) return;
            Transform target = CurrentInteractionTarget;
            if (target == null) return;
            if (target == stair)
            {
                arriveAtStair = true;
                SceneManager.LoadScene(secondFloor ? FirstFloor : SecondFloor);
            }
            else if (target == curtainSpot)
                curtainPrank.TryStart();
        }

        Vector3 CameraRelativeGroundMovement(Vector3 input)
        {
            if (followCamera == null || input.sqrMagnitude == 0f) return input;
            Transform view = followCamera.transform;
            Vector3 right = Vector3.ProjectOnPlane(view.right, Vector3.up).normalized;
            Vector3 forward = Vector3.ProjectOnPlane(view.forward, Vector3.up).normalized;
            return Vector3.ClampMagnitude(right * input.x + forward * input.z, 1f);
        }

        void FindFloorHorizontalBounds()
        {
            // The floor source comes from GhostFloorSurface (inspector references, then markers, then the
            // legacy generated/imported names), so splitting the model into floor and wall objects needs no
            // code change here. Walls are not part of that source and never widen the camera turn range.
            var floorSources = GhostFloorSurface.ResolveRoots(SceneManager.GetActiveScene(), null);
            GameObject source = floorBoundsSource != null && floorBoundsSource.gameObject.activeInHierarchy
                ? floorBoundsSource.gameObject
                : floorSources.Count > 0 ? floorSources[0].gameObject : null;
            var renderers = source != null ? source.GetComponentsInChildren<Renderer>() : null;
            if (renderers == null || renderers.Length == 0)
            {
                Debug.LogWarning("Camera turn bounds have no active floor renderer; using a temporary range around the player.");
                floorLeftX = player.position.x - 1f;
                floorRightX = player.position.x + 1f;
                return;
            }
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            floorLeftX = bounds.min.x;
            floorRightX = bounds.max.x;
        }

        bool Near(Transform marker)
        {
            if (marker == null || !marker.gameObject.activeInHierarchy || player == null) return false;
            Vector2 a = new Vector2(player.position.x, player.position.z);
            Vector2 b = new Vector2(marker.position.x, marker.position.z);
            return Vector2.Distance(a, b) <= InteractionRange;
        }

        void OnGUI()
        {
            if (player == null) return;
            if (curtainPrank != null && curtainPrank.IsActive) return;
            bool gamepad = GhostInput.UsingGamepad;
            string action = gamepad ? "A" : "E";
            string prompt = (secondFloor ? "2F" : "1F") + (gamepad ? "  |  Left stick: move" : "  |  WASD: move");
            if (CurrentInteractionTarget == stair && stair != null)
                prompt += "  |  " + action + (secondFloor ? ": go downstairs" : ": go upstairs");
            var style = new GUIStyle(GUI.skin.box)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter
            };
            GUI.Box(new Rect(12, 12, 460, 38), prompt, style);
        }

    }
}
