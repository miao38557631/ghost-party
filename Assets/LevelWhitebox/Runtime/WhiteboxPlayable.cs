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
        WhiteboxCamera cameraSettings;
        float floorLeftX;
        float floorRightX;
        bool secondFloor;

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
            var wallGroup = GameObject.Find("02_BlackPlanLines_Walls_2m_80mm");
            if (wallGroup != null)
                followCamera.gameObject.AddComponent<WhiteboxWallOcclusion>()
                    .Initialize(player, wallGroup.transform);
            Debug.Log("Whitebox playable ready on " + (secondFloor ? "Level02" : "Level01") +
                      ", spawned at " + player.position);
        }

        void Update()
        {
            if (player == null) return;
            if (curtainPrank != null && curtainPrank.IsActive)
            {
                GhostPlayerAvatar.SetMovement(player, Vector3.zero);
                return;
            }
            Vector3 movement = GhostPlayerAvatar.ReadGroundInput();
            GhostPlayerAvatar.Move(player, movement, Time.deltaTime);

            if (!Input.GetKeyDown(KeyCode.E)) return;
            if (Near(stair))
            {
                arriveAtStair = true;
                SceneManager.LoadScene(secondFloor ? FirstFloor : SecondFloor);
            }
            else if (!secondFloor && Near(curtainSpot) && curtainPrank != null)
                curtainPrank.TryStart();
        }

        void FindFloorHorizontalBounds()
        {
            var slabs = GameObject.Find("01_Slabs");
            var renderers = slabs != null ? slabs.GetComponentsInChildren<Renderer>() : null;
            if (renderers == null || renderers.Length == 0)
            {
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
            if (marker == null || player == null) return false;
            Vector2 a = new Vector2(player.position.x, player.position.z);
            Vector2 b = new Vector2(marker.position.x, marker.position.z);
            return Vector2.Distance(a, b) <= InteractionRange;
        }

        void OnGUI()
        {
            if (player == null) return;
            if (curtainPrank != null && curtainPrank.IsActive) return;
            string prompt = secondFloor ? "2F  |  WASD: move" : "1F  |  WASD: move";
            if (Near(stair)) prompt += secondFloor ? "  |  E: go downstairs" : "  |  E: go upstairs";
            else if (Near(curtainSpot)) prompt += "  |  E: curtain prank";
            var style = new GUIStyle(GUI.skin.box)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter
            };
            GUI.Box(new Rect(12, 12, 460, 38), prompt, style);
        }

    }
}
