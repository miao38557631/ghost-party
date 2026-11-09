using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Runtime assertions for the floating ghost following the sunken first-floor geometry.
/// Programmatic Play check, deliberately separate from physical keyboard/mouse acceptance.</summary>
[InitializeOnLoad]
public static class GhostFloorHoverPlayChecks
{
    const string ActiveKey = "GhostFloorHover.Active";
    const string BatchKey = "GhostFloorHover.Batch";
    const string StartSceneKey = "GhostFloorHover.PlayStartScene";
    const string EditorSceneKey = "GhostFloorHover.EditorScene";
    const string ReportPath = "Logs/PlayerPrefab/floor-hover-checks.json";
    const string Scene = "Assets/LevelWhitebox/Scenes/Level01_FirstFloor.unity";
    const float FixedStep = 1f / 60f;

    [Serializable]
    public class Report
    {
        public string kind = "Programmatic floating-floor assertions on Level01; not a physical-input acceptance";
        public string scene;
        public bool complete;
        public int floorColliders;
        public float hoverHeight;
        public float probeFloorY;
        public int gridSamples;
        public float floorExtentX;
        public float floorExtentZ;
        public float flatSurfaceY;
        public float lowestSurfaceY;
        public float pitDepth;
        public List<float> descentSamples = new List<float>();
        public List<float> ascentSamples = new List<float>();
        public List<string> passed = new List<string>();
        public List<string> failed = new List<string>();
    }

    static Report report;
    static readonly List<string> failures = new List<string>();
    static GameObject root;
    static GhostPlayerMotor motor;
    static Camera view;
    static Vector3 originalPosition;
    static Vector3 flatStart, pitCentre;
    static float flatSurface, pitSurface;
    static float maxDescentStep, maxAscentStep;
    static int stage;
    static int waitFrames;
    static double deadline;
    static bool registered;
    static bool blockedByProp;

    static GhostFloorHoverPlayChecks()
    {
        EditorApplication.playModeStateChanged += OnPlayChanged;
    }

    [MenuItem("Ghost Party/Player/Run Floor Hover Play Checks")]
    public static void Run()
    {
        Start(false);
    }

    /// <summary>Batch entry: -batchmode -projectPath ... -executeMethod GhostFloorHoverPlayChecks.BatchRun</summary>
    public static void BatchRun()
    {
        Start(true);
    }

    static void Start(bool batch)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetBool(ActiveKey, false))
        {
            Debug.LogWarning("Exit Play or wait for the current floor hover checks.");
            return;
        }
        Directory.CreateDirectory("Logs/PlayerPrefab");
        SessionState.SetBool(BatchKey, batch);
        SessionState.SetBool(ActiveKey, true);
        SessionState.SetString(StartSceneKey, AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetString(EditorSceneKey, SceneManager.GetActiveScene().path);
        File.WriteAllText(ReportPath, JsonUtility.ToJson(new Report { scene = Scene }, true));
        if (SceneManager.GetActiveScene().path == Scene) EditorSceneManager.playModeStartScene = null;
        else EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(Scene);
        EditorApplication.isPlaying = true;
    }

    static void OnPlayChanged(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(ActiveKey, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            if (registered) return;
            report = JsonUtility.FromJson<Report>(File.ReadAllText(ReportPath));
            stage = 0;
            waitFrames = 0;
            deadline = EditorApplication.timeSinceStartup + 0.5;
            EditorApplication.update += Tick;
            registered = true;
        }
        if (change != PlayModeStateChange.EnteredEditMode) return;
        if (registered) { EditorApplication.update -= Tick; registered = false; }
        FinishAfterPlay();
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isPaused) return;
        if (EditorApplication.timeSinceStartup < deadline) return;
        try
        {
            if (stage == 0) Begin();
            else if (stage == 1) SettleOnFlatFloor();
            else if (stage == 2) DescendIntoPit();
            else if (stage == 3) AscendBackOut();
        }
        catch (Exception error)
        {
            report.failed.Add(error.ToString());
            Save();
            EditorApplication.isPlaying = false;
        }
    }

    static void Check(bool value, string message)
    {
        (value ? report.passed : report.failed).Add(message);
        if (!value) failures.Add(message);
    }

    static void Save()
    {
        File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
    }

    static void Begin()
    {
        root = GameObject.Find("InvisibleGhost");
        if (root == null)
        {
            if (++waitFrames < 600) return;
            throw new InvalidOperationException("InvisibleGhost did not spawn in " + Scene);
        }
        motor = root.GetComponent<GhostPlayerMotor>();
        if (motor == null) throw new InvalidOperationException("InvisibleGhost has no GhostPlayerMotor.");
        view = Camera.main;
        originalPosition = root.transform.position;
        report.hoverHeight = motor.HoverHeight;
        report.floorColliders = motor.FloorColliderCount;
        Check(motor.HasFloor, "Floor root discovered and probe colliders available (count " + motor.FloorColliderCount + ")");
        if (!motor.HasFloor) throw new InvalidOperationException("No floor root with colliders was found; the ghost cannot follow the floor.");

        // Sample the floor on a grid to locate the flat level and the sunken area without hard-coding coordinates.
        Bounds bounds = FloorBounds();
        report.floorExtentX = bounds.size.x;
        report.floorExtentZ = bounds.size.z;
        float scanY = originalPosition.y;
        float coarseStep = Mathf.Max(0.6f, Mathf.Max(bounds.size.x, bounds.size.z) / 48f);
        var samples = new List<KeyValuePair<Vector3, float>>();
        for (float x = bounds.min.x + 0.3f; x <= bounds.max.x - 0.3f; x += coarseStep)
            for (float z = bounds.min.z + 0.3f; z <= bounds.max.z - 0.3f; z += coarseStep)
            {
                Vector3 at = new Vector3(x, scanY, z);
                float surface;
                if (!motor.TryGetSurfaceAt(at, out surface)) continue;
                samples.Add(new KeyValuePair<Vector3, float>(at, surface));
            }
        report.gridSamples = samples.Count;
        if (samples.Count < 8) throw new InvalidOperationException("Floor probe found only " + samples.Count + " samples.");
        float highest = float.NegativeInfinity, lowest = float.PositiveInfinity;
        foreach (var sample in samples)
        {
            highest = Mathf.Max(highest, sample.Value);
            lowest = Mathf.Min(lowest, sample.Value);
        }
        report.flatSurfaceY = highest;
        report.lowestSurfaceY = lowest;
        report.pitDepth = highest - lowest;
        Check(report.pitDepth > 0.15f, "Sunken floor detected under the level: " + report.pitDepth.ToString("0.00") + " m below the flat floor");

        // Sunken reference: the lowest sample with the widest clearance, so the probe sits well inside the pit.
        float lowestNeighbour = float.NegativeInfinity;
        foreach (var sample in samples)
        {
            if (sample.Value > lowest + 0.05f) continue;
            Vector2 centre = new Vector2(sample.Key.x, sample.Key.z);
            float neighbour = float.PositiveInfinity;
            foreach (var other in samples)
                if (other.Value > lowest + 0.05f)
                    neighbour = Mathf.Min(neighbour, Vector2.Distance(centre, new Vector2(other.Key.x, other.Key.z)));
            if (neighbour > lowestNeighbour) { lowestNeighbour = neighbour; pitCentre = sample.Key; pitSurface = sample.Value; }
        }
        // Flat reference: the flat sample closest to the sunken area (short walk, least chance of a blocking prop).
        float bestFlat = float.PositiveInfinity;
        Vector2 pitXZ = new Vector2(pitCentre.x, pitCentre.z);
        foreach (var sample in samples)
        {
            if (sample.Value < highest - 0.05f) continue;
            Vector2 point = new Vector2(sample.Key.x, sample.Key.z);
            float clearance = float.PositiveInfinity;
            foreach (var other in samples)
                if (other.Value < highest - 0.05f)
                    clearance = Mathf.Min(clearance, Vector2.Distance(point, new Vector2(other.Key.x, other.Key.z)));
            if (clearance < 1.2f) continue;
            float distance = Vector2.Distance(point, pitXZ);
            if (distance < bestFlat) { bestFlat = distance; flatStart = sample.Key; flatSurface = sample.Value; }
        }
        if (flatStart == Vector3.zero || pitCentre == Vector3.zero) throw new InvalidOperationException("Could not pick floor/pit reference points.");
        report.probeFloorY = flatSurface;
        Debug.Log("FLOOR_HOVER_MEASURED flat=" + flatSurface.ToString("0.000") + " lowest=" + lowest.ToString("0.000") +
                  " at " + pitCentre + " flatPoint=" + flatStart + " colliders=" + motor.FloorColliderCount);

        // Approach from the flat side so the descent happens through ordinary movement.
        motor.Teleport(new Vector3(flatStart.x, flatSurface + motor.HoverHeight + 1.2f, flatStart.z));
        deadline = EditorApplication.timeSinceStartup + 0.1;
        stage = 1;
    }

    static void SettleOnFlatFloor()
    {
        float expected = flatSurface + motor.HoverHeight;
        Check(Mathf.Abs(root.transform.position.y - expected) < 0.06f,
            "Teleport lands at hover height over the flat floor (" + root.transform.position.y.ToString("0.00") + " vs " + expected.ToString("0.00") + ")");
        // Spend a few frames walking on the flat floor, away from the sunken area: height must not drift.
        float before = root.transform.position.y;
        Vector3 away = root.transform.position - pitCentre;
        away.y = 0f;
        away = away.sqrMagnitude < 0.0001f ? Vector3.right : Vector3.Normalize(away);
        for (int i = 0; i < 12; i++) motor.Move(away, FixedStep);
        Check(Mathf.Abs(root.transform.position.y - before) < 0.03f, "Walking across the flat floor keeps a constant floating height");

        Vector3 toPit = pitCentre - root.transform.position; toPit.y = 0f;
        stepDirection = Vector3.Normalize(toPit);
        stage = 2;
    }

    static Vector3 stepDirection;

    static void DescendIntoPit()
    {
        float startY = root.transform.position.y;
        float target = pitSurface + motor.HoverHeight;
        report.descentSamples.Clear();
        report.descentSamples.Add(startY);
        maxDescentStep = 0f;
        blockedByProp = false;
        float last = startY;
        Vector3 lastPosition = root.transform.position;
        int stuck = 0;
        int iterations = 0;
        while (iterations++ < 900)
        {
            motor.Move(stepDirection, FixedStep);
            float now = root.transform.position.y;
            maxDescentStep = Mathf.Max(maxDescentStep, Mathf.Abs(now - last));
            last = now;
            Vector3 moved = root.transform.position - lastPosition;
            moved.y = 0f;
            stuck = moved.magnitude < 0.01f ? stuck + 1 : 0;
            lastPosition = root.transform.position;
            if (iterations % 4 == 0) report.descentSamples.Add(now);
            if (now <= target + 0.06f) break;
            if (stuck > 60) { blockedByProp = true; break; }
        }
        float finalY = root.transform.position.y;
        Check(finalY <= target + 0.08f,
            "Moving over the sunken floor lowers the ghost to the pit level (" + finalY.ToString("0.00") + " vs " + target.ToString("0.00") + ")");
        Check(maxDescentStep < 0.25f, "Descent is a glide, not a snap (largest frame step " + maxDescentStep.ToString("0.000") + " m)");
        Check(target < startY - 0.15f, "Pit level differs from the flat floor by " + (startY - target).ToString("0.00") + " m");
        Check(!blockedByProp, "The walk from the flat floor into the sunken area was not blocked by a prop collider");

        // Cross the pit to the far edge and keep going, then walk back out the way we came.
        stepDirection = -stepDirection;
        stage = 3;
    }

    static void AscendBackOut()
    {
        float target = flatSurface + motor.HoverHeight;
        report.ascentSamples.Clear();
        maxAscentStep = 0f;
        float last = root.transform.position.y;
        Vector3 lastPosition = root.transform.position;
        int stuck = 0;
        report.ascentSamples.Add(last);
        int iterations = 0;
        while (iterations++ < 900)
        {
            motor.Move(stepDirection, FixedStep);
            float now = root.transform.position.y;
            maxAscentStep = Mathf.Max(maxAscentStep, Mathf.Abs(now - last));
            last = now;
            Vector3 moved = root.transform.position - lastPosition;
            moved.y = 0f;
            stuck = moved.magnitude < 0.01f ? stuck + 1 : 0;
            lastPosition = root.transform.position;
            if (iterations % 4 == 0) report.ascentSamples.Add(now);
            if (now >= target - 0.06f) break;
            if (stuck > 60) { blockedByProp = true; break; }
        }
        Check(!blockedByProp, "The walk back out of the sunken area was not blocked by a prop collider");
        Check(root.transform.position.y >= target - 0.1f,
            "Leaving the sunken floor raises the ghost back to the flat floor height (" + root.transform.position.y.ToString("0.00") + ")");
        Check(maxAscentStep < 0.25f, "Ascent is a glide, not a snap (largest frame step " + maxAscentStep.ToString("0.000") + " m)");
        Check(Mathf.Abs(root.transform.position.y - originalPosition.y) < 1.2f, "Height stays in the authored band around the saved player position");

        motor.Teleport(originalPosition);
        Check(Mathf.Abs(root.transform.position.y - (flatSurface + motor.HoverHeight)) < 0.06f,
            "Restoring the saved player position re-seats the hover height");
        if (view != null) Check(view.orthographic, "Follow camera still drives an orthographic view while the ghost changes height");
        report.complete = true;
        Save();
        EditorApplication.isPlaying = false;
    }

    static Bounds FloorBounds()
    {
        // Same source as the motor, so a re-authored or split floor model needs no change here either.
        var sources = GhostFloorSurface.ResolveRoots(SceneManager.GetActiveScene(), null);
        var renderers = new List<Renderer>();
        for (int i = 0; i < sources.Count; i++)
            if (sources[i] != null) renderers.AddRange(sources[i].GetComponentsInChildren<Renderer>());
        if (renderers.Count == 0) throw new InvalidOperationException("No floor renderer found for bounds.");
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Count; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    static void FinishAfterPlay()
    {
        bool batch = SessionState.GetBool(BatchKey, false);
        SessionState.SetBool(ActiveKey, false);
        string previous = SessionState.GetString(StartSceneKey, "");
        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(previous) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(previous);
        if (File.Exists(ReportPath))
        {
            report = JsonUtility.FromJson<Report>(File.ReadAllText(ReportPath));
            if (report != null && !report.complete && report.failed.Count == 0)
            {
                report.failed.Add("Play session ended before the checks completed");
                Save();
            }
            if (report != null) Debug.Log("FLOOR_HOVER_CHECKS: " + report.passed.Count + " passed, " + report.failed.Count + " failed -> " + ReportPath);
        }
        if (batch)
        {
            int failed = report != null ? report.failed.Count : 1;
            EditorApplication.Exit(failed == 0 ? 0 : 1);
        }
    }
}
