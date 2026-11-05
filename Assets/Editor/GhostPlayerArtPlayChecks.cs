using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Programmatic Play checks, deliberately separate from physical keyboard/mouse acceptance.</summary>
[InitializeOnLoad]
public static class GhostPlayerArtPlayChecks
{
    const string ActiveKey = "GhostPlayerArtChecks.Active";
    const string PhaseKey = "GhostPlayerArtChecks.Phase";
    const string ReportPath = "Logs/PlayerPrefab/play-checks.json";
    static readonly string[] Scenes = {
        "Assets/LevelWhitebox/Scenes/Level01_FirstFloor.unity", "Assets/Scenes/SampleScene.unity",
        "Assets/LevelWhitebox/Scenes/Level02_SecondFloor.unity", "Assets/LevelWhitebox/Scenes/Level01_FirstFloor.unity"
    };
    [Serializable] public class SceneResult { public string scene; public List<string> passed = new List<string>(); public List<string> failed = new List<string>(); }
    [Serializable] public class Report { public string kind = "Programmatic runtime assertions, not physical input acceptance"; public bool complete; public List<SceneResult> scenes = new List<SceneResult>(); }
    static int stage;
    static double deadline;
    static bool registered;
    static Report report;
    static SceneResult result;
    static bool savedFacing;
    static Vector3 savedReturnPosition;

    static GhostPlayerArtPlayChecks()
    {
        EditorApplication.playModeStateChanged += OnPlayChanged;
        if (SessionState.GetBool(ActiveKey, false) && EditorApplication.isPlaying) BeginScene();
    }

    [MenuItem("Ghost Party/Player/Run Ghost Art Play Checks")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetBool(ActiveKey, false))
        { Debug.LogWarning("Exit Play or wait for the current ghost checks."); return; }
        GhostPlayerArtBuilder.Validate();
        Directory.CreateDirectory("Logs/PlayerPrefab");
        var gameType = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
        var gameWindow = EditorWindow.GetWindow(gameType);
        SessionState.SetBool("GhostPlayerArtChecks.GameMaximized", gameWindow.maximized);
        gameWindow.maximized = true;
        gameWindow.Focus();
        SessionState.SetString("GhostPlayerArtChecks.StartScene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetString("GhostPlayerArtChecks.EditorScene", SceneManager.GetActiveScene().path);
        SessionState.SetBool("GhostPlayerArtChecks.Dirty", SceneManager.GetActiveScene().isDirty);
        SessionState.SetInt(PhaseKey, 0);
        SessionState.SetBool(ActiveKey, true);
        File.WriteAllText(ReportPath, JsonUtility.ToJson(new Report(), true));
        StartNextScene();
    }

    static void StartNextScene()
    {
        int phase = SessionState.GetInt(PhaseKey, 0);
        // Existing open scene is never opened/saved/reloaded in Edit mode.
        var target = AssetDatabase.LoadAssetAtPath<SceneAsset>(Scenes[phase]);
        EditorSceneManager.playModeStartScene = SceneManager.GetActiveScene().path == Scenes[phase] ? null : target;
        EditorApplication.isPlaying = true;
    }

    static void OnPlayChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("GhostPlayerArtChecks.PendingInput", false))
        {
            SessionState.SetBool("GhostPlayerArtChecks.PendingInput", false);
            ObserveExistingInput();
        }
        if (!SessionState.GetBool(ActiveKey, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode) BeginScene();
        if (change != PlayModeStateChange.EnteredEditMode) return;
        if (registered) { EditorApplication.update -= Tick; registered = false; }
        report = JsonUtility.FromJson<Report>(File.ReadAllText(ReportPath));
        int next = SessionState.GetInt(PhaseKey, 0) + 1;
        if (next < Scenes.Length)
        {
            SessionState.SetInt(PhaseKey, next);
            EditorApplication.delayCall += StartNextScene;
        }
        else
        {
            string old = SessionState.GetString("GhostPlayerArtChecks.StartScene", "");
            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(old) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(old);
            bool preserved = SceneManager.GetActiveScene().path == SessionState.GetString("GhostPlayerArtChecks.EditorScene", "")
                && SceneManager.GetActiveScene().isDirty == SessionState.GetBool("GhostPlayerArtChecks.Dirty", false);
            if (report.scenes.Count > 0)
            {
                var last = report.scenes[report.scenes.Count - 1];
                (preserved ? last.passed : last.failed).Add("Editor active scene and dirty state restored without saving");
            }
            var gameType = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
            EditorWindow.GetWindow(gameType).maximized = SessionState.GetBool("GhostPlayerArtChecks.GameMaximized", false);
            report.complete = true;
            File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
            SessionState.SetBool(ActiveKey, false);
            Debug.Log("GHOST_PLAYER_PLAY_CHECKS_FINISHED: " + ReportPath);
        }
    }

    static void BeginScene()
    {
        if (registered) return;
        report = JsonUtility.FromJson<Report>(File.ReadAllText(ReportPath));
        result = new SceneResult { scene = Scenes[SessionState.GetInt(PhaseKey, 0)] };
        report.scenes.Add(result);
        stage = 0; deadline = EditorApplication.timeSinceStartup + 0.8;
        EditorApplication.update += Tick; registered = true;
    }

    static void Check(bool value, string message) { (value ? result.passed : result.failed).Add(message); }
    static void Save() { File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true)); }
    static void Capture(string name) { ScreenCapture.CaptureScreenshot("Logs/PlayerPrefab/GhostPlayerArt-" + name + "-" + SessionState.GetInt(PhaseKey, 0) + ".png"); }

    static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isPaused || EditorApplication.timeSinceStartup < deadline) return;
        try
        {
            var root = GameObject.Find("InvisibleGhost");
            if (root == null) throw new InvalidOperationException("InvisibleGhost did not spawn.");
            var visual = root.transform.Find("Visual");
            if (visual == null) throw new InvalidOperationException("Visual child did not spawn.");
            var renderer = visual.GetComponent<SpriteRenderer>();
            var animator = visual.GetComponent<Animator>();
            if (stage == 0)
            {
                Check(UnityEngine.Object.FindObjectsOfType<Transform>().Count(t => t.name == "InvisibleGhost") == 1, "Exactly one InvisibleGhost");
                Check(root.GetComponent<CharacterController>() != null && root.GetComponents<Renderer>().All(r => !r.enabled), "One CharacterController provides 3D blocking; placeholder renderers absent or disabled");
                var pos = root.transform.position; var scale = root.transform.localScale; var rot = root.transform.rotation;
                Check(GhostPlayerAvatar.Create(pos + Vector3.one) == root.transform && root.transform.position == pos, "Shared factory is idempotent and does not move an existing root");
                Check(renderer != null && renderer.sprite != null && renderer.color == Color.white && !renderer.flipX, "Native sprite present, white tint, authored right-facing initial state");
                Check(renderer.sharedMaterial != null && renderer.sharedMaterial.shader.isSupported && renderer.sharedMaterial.shader.name == "Sprites/Default", "Supported transparent built-in sprite material");
                GhostPlayerAvatar.SetMovement(root.transform, Vector3.zero); animator.Update(0.12f);
                Check(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), "Zero existing movement selects Idle");
                var idleA = renderer.sprite; animator.Update(0.12f); Check(renderer.sprite != idleA, "Idle frames advance");
                GhostPlayerAvatar.SetMovement(root.transform, Vector3.right); animator.Update(0.12f);
                Check(animator.GetCurrentAnimatorStateInfo(0).IsName("Walk"), "Existing movement vector selects Walk");
                var walkA = renderer.sprite; animator.Update(0.12f); Check(renderer.sprite != walkA, "Walk frames advance");
                GhostPlayerAvatar.SetMovement(root.transform, Vector3.zero); animator.Update(0.12f);
                Check(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), "Stopping returns to Idle");
                Check(root.transform.position == pos && root.transform.localScale == scale && Quaternion.Angle(root.transform.rotation, rot) < 0.001f, "Animation switching does not move/scale/rotate the player root");
                Check(Camera.main != null && Quaternion.Angle(visual.rotation, Camera.main.transform.rotation) < 0.2f, "Only Visual faces exploration camera");
                Check(Mathf.Abs(visual.lossyScale.y * 1.97265625f - 1.35f) < 0.02f, "Stable calibrated artwork height of 1.35 world units");
                var driver = visual.GetComponent<GhostPlayerVisual>();
                GhostPlayerAvatar.SetMovement(root.transform, Vector3.left); animator.Update(0.12f);
                Check(renderer.flipX && !driver.FacingRight, "Left movement mirrors only the SpriteRenderer");
                GhostPlayerAvatar.SetMovement(root.transform, Vector3.zero); animator.Update(0.12f);
                Check(renderer.flipX && animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), "Stopping preserves left facing and selects Idle");
                GhostPlayerAvatar.SetMovement(root.transform, Vector3.forward); animator.Update(0.12f);
                Check(renderer.flipX && driver.IsMoving, "Pure forward movement preserves left facing");
                GhostPlayerAvatar.SetMovement(root.transform, Vector3.back); animator.Update(0.12f);
                Check(renderer.flipX, "Pure backward movement preserves left facing");
                GhostPlayerAvatar.SetMovement(root.transform, Vector3.right); animator.Update(0.12f);
                Check(!renderer.flipX && driver.FacingRight, "Right movement restores right facing");
                var cameraRotation = Camera.main.transform.rotation;
                Camera.main.transform.rotation = Quaternion.Euler(0, 180, 0);
                GhostPlayerAvatar.SetMovement(root.transform, Vector3.right);
                Check(renderer.flipX, "Facing uses screen-horizontal projection when camera is reversed");
                Camera.main.transform.rotation = cameraRotation;
                GhostPlayerAvatar.SetMovement(root.transform, Vector3.left);
                GhostPlayerAvatar.SetMovement(root.transform, Vector3.zero); animator.Update(0.12f);
                Check(root.transform.position == pos && root.transform.localScale == scale && root.transform.rotation == rot,
                    "Mirroring never changes control root position, scale or rotation");
                var glow = root.GetComponent<Light>();
                bool originalGlow = glow != null && glow.enabled;
                GhostPlayerAvatar.SetVisible(root.transform, false);
                Check(!renderer.enabled && (glow == null || !glow.enabled) && root.GetComponent<CharacterController>().enabled,
                    "Unified visibility hides owned art/effects immediately and preserves collider");
                GhostPlayerAvatar.SetVisible(root.transform, false);
                GhostPlayerAvatar.SetVisible(root.transform, true);
                Check(renderer.enabled && !root.GetComponents<Renderer>().Any(r => r.enabled) && (glow == null || glow.enabled == originalGlow),
                    "Repeated hide then show restores exact renderer/effect states; placeholder stays disabled");
                savedFacing = renderer.flipX;
                savedReturnPosition = root.transform.position;
                CheckCollisionAndPrefab(root);
                if (SceneManager.GetActiveScene().path.EndsWith("Level02_SecondFloor.unity", StringComparison.Ordinal))
                { Save(); EditorApplication.isPlaying = false; return; }
                Capture("explore"); stage = 1; deadline = EditorApplication.timeSinceStartup + 0.5; Save();
            }
            else if (stage == 1)
            {
                var encounter = UnityEngine.Object.FindObjectOfType<GhostParty.LevelWhitebox.LevelCurtainEncounter>();
                if (encounter != null) encounter.TryStart();
                else
                {
                    var prank = UnityEngine.Object.FindObjectOfType<GhostPartyPrototype>();
                    typeof(GhostPartyPrototype).GetMethod("BeginPrank", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(prank, null);
                }
                Check(!renderer.enabled && !root.GetComponent<GhostPlayerVisibility>().IsVisible,
                    "Successful possession hides art synchronously before returning from start entry");
                Check(root.activeInHierarchy && root.GetComponent<CharacterController>().enabled,
                    "Possession retains the active control root and collider");
                stage = 2; deadline = EditorApplication.timeSinceStartup + 0.8;
            }
            else if (stage == 2)
            {
                Check(!visual.GetComponent<GhostPlayerVisual>().IsMoving, "Existing closeup path stops locomotion animation");
                Check(Quaternion.Angle(visual.rotation, Camera.main.transform.rotation) < 0.2f, "Visual follows closeup camera without rotating root");
                Vector3 screenPoint = Camera.main.WorldToViewportPoint(renderer.bounds.center);
                Check(renderer.sprite != null && screenPoint.z > 0f && screenPoint.x > 0f && screenPoint.x < 1f
                    && screenPoint.y > 0f && screenPoint.y < 1f, "New artwork centre is inside the full closeup viewport");
                Check(GameObject.Find("CurtainPlayerHUD") != null, "Current curtain gameplay UI is visible");
                Check(!renderer.enabled && renderer.flipX == savedFacing, "Possession remains hidden and retains facing after Animator evaluation");
                var prank = UnityEngine.Object.FindObjectOfType<GhostPartyPrototype>();
                Call(prank, "FailHoldAndRestartWind");
                Check(!renderer.enabled, "Failed hold/internal retry does not reveal the ghost");
                Call(prank, "ResetPrank");
                Check(!renderer.enabled, "Full challenge retry remains hidden without changing restore state");
                Capture("closeup"); stage = 3; deadline = EditorApplication.timeSinceStartup + 0.5; Save();
            }
            else if (stage == 3)
            {
                var prank = UnityEngine.Object.FindObjectOfType<GhostPartyPrototype>();
                Check(!renderer.enabled, "Retry stays hidden across rendered frames");
                Call(prank, "CompletePrank");
                Check(!renderer.enabled, "Success presentation remains hidden until exploration resumes");
                stage = 4; deadline = EditorApplication.timeSinceStartup + 2.1;
            }
            else if (stage == 4)
            {
                CheckRestored(root, renderer, animator, "Completion");
                StartPossession();
                Call(UnityEngine.Object.FindObjectOfType<GhostPartyPrototype>(), "EscapeCurtain");
                Check(!renderer.enabled, "NPC escape presentation remains hidden until exploration resumes");
                stage = 5; deadline = EditorApplication.timeSinceStartup + 2.1; Save();
            }
            else if (stage == 5)
            {
                CheckRestored(root, renderer, animator, "NPC escape");
                StartPossession();
                Call(UnityEngine.Object.FindObjectOfType<GhostPartyPrototype>(), "ReturnToExplore");
                CheckRestored(root, renderer, animator, "Shared Esc return path");
                stage = 6; deadline = EditorApplication.timeSinceStartup + 0.4; Save();
            }
            else
            {
                Check(root.transform.Find("Visual") == visual && UnityEngine.Object.FindObjectsOfType<Transform>().Count(t => t.name == "InvisibleGhost") == 1, "Returning from curtain retains one player and one Visual");
                Check(GameObject.Find("CurtainPlayerHUD") == null || SceneManager.GetActiveScene().path == Scenes[1], "Embedded curtain UI hides on return to map");
                GhostPlayerAvatar.SetMovement(root.transform, Vector3.right); animator.Update(0.12f);
                Check(renderer.enabled && !renderer.flipX && animator.GetCurrentAnimatorStateInfo(0).IsName("Walk"),
                    "Movement, facing and animation work after repeated possession cycles");
                Save(); EditorApplication.isPlaying = false;
            }
        }
        catch (Exception error)
        {
            result.failed.Add(error.ToString()); Save(); EditorApplication.isPlaying = false;
        }
    }
    static void CheckCollisionAndPrefab(GameObject root)
    {
        var motor = root.GetComponent<GhostPlayerMotor>();
        var controller = root.GetComponent<CharacterController>();
        Check(motor != null && controller != null && controller.enabled && controller.detectCollisions
            && root.GetComponents<Collider>().Length == 1 && root.GetComponent<Rigidbody>() == null,
            "One CharacterController motor; no conflicting sphere collider or rigidbody");
        var source = PrefabUtility.GetCorrespondingObjectFromSource(root);
        Check(source != null && AssetDatabase.GetAssetPath(source) == GhostPlayerPrefabBuilder.PrefabPath,
            "Saved scene player is an instance of the one editable GhostPlayer prefab");
        var playable = UnityEngine.Object.FindObjectOfType<GhostParty.LevelWhitebox.WhiteboxPlayable>();
        var binding = playable != null ? new SerializedObject(playable).FindProperty("player")
            : new SerializedObject(UnityEngine.Object.FindObjectOfType<GhostPartyPrototype>()).FindProperty("ghost");
        Check(binding.objectReferenceValue == root.transform, "Gameplay uses the explicit saved player reference");
        var originalPosition = root.transform.position;
        var originalScale = root.transform.localScale;
        var originalRotation = root.transform.rotation;
        var wall = new GameObject("GhostCollisionRegression_TemporaryWall");
        try
        {
            var box = wall.AddComponent<BoxCollider>();
            wall.transform.position = new Vector3(1002f, 1f, 1000f);
            box.size = new Vector3(0.08f, 2f, 20f);
            Physics.SyncTransforms();
            Check(!Physics.GetIgnoreLayerCollision(root.layer, wall.layer), "Player/wall 3D layer collision pair is enabled");
            motor.Teleport(new Vector3(1000f, originalPosition.y, 1000f));
            CollisionFlags flags = CollisionFlags.None;
            for (int i = 0; i < 360; i++) flags |= motor.Move(Vector3.right, 1f / 60f);
            float radius = controller.radius * Mathf.Abs(root.transform.lossyScale.x);
            float stopLimit = box.bounds.min.x - radius;
            Check(root.transform.position.x <= stopLimit + 0.08f && root.transform.position.x >= stopLimit - 0.15f
                && (flags & CollisionFlags.Sides) != 0,
                "Six seconds of sustained pressure cannot pass an 8cm solid wall");
            motor.Teleport(new Vector3(1000f, originalPosition.y, 995f));
            for (int i = 0; i < 180; i++) motor.Move(new Vector3(1, 0, 1), 1f / 60f);
            Check(root.transform.position.x <= stopLimit + 0.08f && root.transform.position.z > 1000f,
                "Diagonal input slides along the wall without crossing it");
            motor.Teleport(new Vector3(1000f, originalPosition.y, 1000f));
            motor.Move(Vector3.right, 1f);
            Check(root.transform.position.x <= stopLimit + 0.08f, "Long frame movement still sweeps against thin walls");
            GhostPlayerAvatar.SetVisible(root.transform, false);
            motor.Move(Vector3.right, 0.5f);
            Check(controller.enabled && root.transform.position.x <= stopLimit + 0.08f,
                "Possession visibility never disables physical blocking");
            GhostPlayerAvatar.SetVisible(root.transform, true);
            Check(root.transform.localScale == originalScale && root.transform.rotation == originalRotation,
                "Collision/visibility operations leave the control root scale and orientation intact");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(wall);
            motor.Teleport(originalPosition);
            GhostPlayerAvatar.SetVisible(root.transform, true);
        }
        // Also exercise an actual authored wall; the isolated barrier above tests thin-wall/sliding mechanics.
        var group = GameObject.Find("02_BlackPlanLines_Walls_2m_80mm");
        var candidates = group != null ? group.GetComponentsInChildren<BoxCollider>()
            : UnityEngine.Object.FindObjectsOfType<BoxCollider>().Where(c => c.name == "BackWall").ToArray();
        bool tested = false;
        foreach (var box in candidates)
        {
            if (!box.enabled || box.isTrigger || box.size.x * Mathf.Abs(box.transform.lossyScale.x) < 3f) continue;
            Vector3 normal = box.transform.forward; normal.y = 0; normal.Normalize();
            float halfThickness = box.size.z * Mathf.Abs(box.transform.lossyScale.z) * 0.5f;
            float radius = controller.radius * Mathf.Abs(root.transform.lossyScale.x);
            Vector3 centre = box.transform.TransformPoint(box.center); centre.y = originalPosition.y;
            Vector3 start = centre + normal * (halfThickness + radius + 0.6f);
            Vector3 bottom = start + Vector3.up * (controller.height * 0.5f * root.transform.lossyScale.y - radius);
            Vector3 top = start - Vector3.up * (controller.height * 0.5f * root.transform.lossyScale.y - radius);
            if (Physics.OverlapCapsule(bottom, top, radius * 0.95f, ~0, QueryTriggerInteraction.Ignore)
                .Any(c => !c.transform.IsChildOf(root.transform))) continue;
            try
            {
                motor.Teleport(start);
                CollisionFlags flags = CollisionFlags.None;
                for (int i = 0; i < 120; i++) flags |= motor.Move(-normal, 1f / 60f);
                float separation = Vector3.Dot(root.transform.position - centre, normal);
                Check(separation >= halfThickness + radius - 0.08f && (flags & CollisionFlags.Sides) != 0,
                    "Authored wall blocks the real controller: " + box.name + " | " + box.bounds);
                tested = true;
            }
            finally { motor.Teleport(originalPosition); }
            break;
        }
        Check(tested, "A real scene wall collider was tested, not only a synthetic barrier");
        GhostPlayerAvatar.SetMovement(root.transform, Vector3.zero);
    }

    static void Call(GhostPartyPrototype prank, string method)
    {
        typeof(GhostPartyPrototype).GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(prank, null);
    }

    static void StartPossession()
    {
        var encounter = UnityEngine.Object.FindObjectOfType<GhostParty.LevelWhitebox.LevelCurtainEncounter>();
        if (encounter != null) encounter.TryStart();
        else Call(UnityEngine.Object.FindObjectOfType<GhostPartyPrototype>(), "BeginPrank");
    }

    static void CheckRestored(GameObject root, SpriteRenderer renderer, Animator animator, string path)
    {
        animator.Update(0.12f);
        Check(renderer.enabled && root.GetComponent<GhostPlayerVisibility>().IsVisible
            && !root.GetComponents<Renderer>().Any(r => r.enabled), path + ": art restored, placeholder stays off");
        Check(renderer.flipX == savedFacing && animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"),
            path + ": previous facing retained and Idle restored");
        Check(Vector3.Distance(root.transform.position, savedReturnPosition) < 0.01f,
            path + ": existing exploration return position retained");
        Check(root.activeInHierarchy && root.GetComponent<CharacterController>().enabled,
            path + ": root/collider remain operational");
    }

    [Serializable] public class InputObservation
    {
        public int frame, visualCount, screenWidth, screenHeight;
        public Vector3 position, rootScale;
        public float horizontal, vertical, cameraAngle;
        public bool moving, closeup, hudVisible, colliderEnabled, rootRendererEnabled, spriteVisible, flipX;
        public string animation, sprite;
    }
    static EditorApplication.CallbackFunction inputObserver;

    [MenuItem("Ghost Party/Player/Observe Existing Ghost Input")]
    public static void ObserveExistingInput()
    {
        if (!EditorApplication.isPlaying)
        {
            SessionState.SetBool("GhostPlayerArtChecks.PendingInput", true);
            EditorApplication.isPlaying = true;
            return;
        }
        if (inputObserver != null) EditorApplication.update -= inputObserver;
        var root = GameObject.Find("InvisibleGhost");
        if (root == null) return;
        var visual = root.transform.Find("Visual");
        var driver = visual.GetComponent<GhostPlayerVisual>();
        var animator = visual.GetComponent<Animator>();
        var renderer = visual.GetComponent<SpriteRenderer>();
        var gameType = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
        EditorWindow.GetWindow(gameType).Focus();
        double last = 0;
        inputObserver = delegate
        {
            if (!EditorApplication.isPlaying || root == null)
            { EditorApplication.update -= inputObserver; inputObserver = null; return; }
            if (EditorApplication.timeSinceStartup - last < 0.025) return;
            last = EditorApplication.timeSinceStartup;
            var prank = UnityEngine.Object.FindObjectOfType<GhostPartyPrototype>();
            var state = animator.GetCurrentAnimatorStateInfo(0);
            var observation = new InputObservation
            {
                frame = Time.frameCount, position = root.transform.position, rootScale = root.transform.localScale,
                visualCount = UnityEngine.Object.FindObjectsOfType<GhostPlayerVisual>().Length,
                horizontal = Input.GetAxisRaw("Horizontal"), vertical = Input.GetAxisRaw("Vertical"),
                moving = driver.IsMoving, closeup = prank != null && prank.IsEmbeddedPrankActive,
                hudVisible = GameObject.Find("CurtainPlayerHUD") != null,
                colliderEnabled = root.GetComponent<CharacterController>().enabled,
                rootRendererEnabled = root.GetComponents<Renderer>().Any(r => r.enabled),
                spriteVisible = renderer.enabled, flipX = renderer.flipX,
                cameraAngle = Camera.main == null ? 0 : Quaternion.Angle(visual.rotation, Camera.main.transform.rotation),
                animation = state.IsName("Walk") ? "Walk" : state.IsName("Idle") ? "Idle" : "Unknown",
                sprite = renderer.sprite == null ? "" : renderer.sprite.name,
                screenWidth = Screen.width, screenHeight = Screen.height
            };
            File.WriteAllText("Temp/GhostPlayerArt-input-observation.json", JsonUtility.ToJson(observation));
        };
        EditorApplication.update += inputObserver;
    }

    [Serializable] public class EditorObservation
    {
        public bool isPlaying, isPaused, isCompiling, sceneDirty;
        public string scene, startScene;
    }
    [MenuItem("Ghost Party/Player/Write Editor Check State")]
    public static void WriteEditorState()
    {
        var state = new EditorObservation
        {
            isPlaying = EditorApplication.isPlaying, isPaused = EditorApplication.isPaused,
            isCompiling = EditorApplication.isCompiling, sceneDirty = SceneManager.GetActiveScene().isDirty,
            scene = SceneManager.GetActiveScene().path,
            startScene = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene)
        };
        File.WriteAllText("Temp/GhostPlayerArt-editor-state.json", JsonUtility.ToJson(state, true));
    }
    [MenuItem("Ghost Party/Player/Finish Ghost Input Check")]
    public static void FinishInputCheck()
    {
        SessionState.SetBool("GhostPlayerArtChecks.PendingInput", false);
        if (inputObserver != null) { EditorApplication.update -= inputObserver; inputObserver = null; }
        EditorApplication.isPlaying = false;
    }

}
