using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using GhostParty.LevelWhitebox;

public static class GhostPlayerPrefabBuilder
{
    public const string PrefabPath = "Assets/Prefabs/Player/GhostPlayer.prefab";
    const string DefinitionPath = "Assets/Resources/Player/GhostPlayerDefinition.asset";
    const string ReportFolder = "Logs/PlayerPrefab";
    static readonly string[] Scenes = {
        "Assets/LevelWhitebox/Scenes/Level01_FirstFloor.unity",
        "Assets/LevelWhitebox/Scenes/Level02_SecondFloor.unity",
        "Assets/Scenes/SampleScene.unity"
    };
    [Serializable] public class SceneAudit
    {
        public string path;
        public int players, wallRenderers, solidWallColliders, repairedWalls;
        public string playerSource;
        public List<string> wallDetails = new List<string>();
    }
    [Serializable] public class BuildReport { public string prefab; public List<SceneAudit> scenes = new List<SceneAudit>(); }

    [MenuItem("Ghost Party/Player/Build Player Prefab and Bind Scenes")]
    public static void BuildAndBind()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Exit Play and wait for compilation before authoring the player.");
        Directory.CreateDirectory(ReportFolder);
        BuildPrefab();
        var originalActive = SceneManager.GetActiveScene();
        var report = new BuildReport { prefab = PrefabPath };
        try
        {
            foreach (string path in Scenes)
            {
                Scene scene = SceneManager.GetSceneByPath(path);
                bool opened = !scene.IsValid() || !scene.isLoaded;
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try
                {
                    // Back up the actual loaded scene, including any previously unsaved edits, before touching it.
                    string backupFolder = "Backups/PlayerPrefab/EditorSceneSnapshots/" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    Directory.CreateDirectory(backupFolder);
                    EditorSceneManager.SaveScene(scene, backupFolder + "/" + Path.GetFileName(path), true);
                    report.scenes.Add(BindScene(scene));
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save " + path);
                }
                finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
            }
        }
        finally { if (originalActive.IsValid() && originalActive.isLoaded) SceneManager.SetActiveScene(originalActive); }
        AssetDatabase.SaveAssets();
        File.WriteAllText(ReportFolder + "/build-report.json", JsonUtility.ToJson(report, true));
        Debug.Log("GHOST_PLAYER_PREFAB_BOUND: " + PrefabPath + "; three scenes saved, original scene edits preserved.");
    }

    static void BuildPrefab()
    {
        EnsureFolder("Assets", "Prefabs"); EnsureFolder("Assets/Prefabs", "Player");
        var authored = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (authored == null)
        {
            var visualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Player/GhostVisual.prefab");
            if (visualPrefab == null) throw new InvalidOperationException("Existing GhostVisual prefab is required.");
            var root = new GameObject("InvisibleGhost");
            try
            {
                root.transform.localScale = Vector3.one * 0.76f;
                var controller = root.AddComponent<CharacterController>();
                controller.height = 1f; controller.radius = 0.45f; controller.center = Vector3.zero;
                controller.skinWidth = 0.03f; controller.minMoveDistance = 0f;
                controller.stepOffset = 0f; controller.slopeLimit = 45f; controller.detectCollisions = true;
                root.AddComponent<GhostPlayerMotor>();
                root.AddComponent<GhostPlayerVisibility>();
                var glow = root.AddComponent<Light>();
                glow.type = LightType.Point; glow.color = new Color(0.2f, 0.9f, 1f); glow.range = 2.5f; glow.intensity = 0.8f;
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(visualPrefab);
                visual.name = "Visual"; visual.transform.SetParent(root.transform, false);
                authored = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        // Repeated builds never reset editable motor/collider/Visual settings or existing GUIDs.
        if (authored.GetComponent<GhostPlayerMotor>() == null || authored.GetComponent<CharacterController>() == null)
            throw new InvalidOperationException("The authored player must have GhostPlayerMotor + CharacterController.");
        if (authored.GetComponent<Rigidbody>() != null || authored.GetComponents<Collider>().Any(c => !(c is CharacterController)))
            throw new InvalidOperationException("Remove conflicting root rigidbody/colliders from the player prefab.");
        var definition = AssetDatabase.LoadAssetAtPath<GhostPlayerPrefabDefinition>(DefinitionPath);
        if (definition == null) { definition = ScriptableObject.CreateInstance<GhostPlayerPrefabDefinition>(); AssetDatabase.CreateAsset(definition, DefinitionPath); }
        definition.playerPrefab = authored; EditorUtility.SetDirty(definition);
    }

    static SceneAudit BindScene(Scene scene)
    {
        var transforms = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
        var players = transforms.Where(t => t.name == "InvisibleGhost").ToArray();
        if (players.Length > 1) throw new InvalidOperationException("Multiple existing players in " + scene.path + "; refusing destructive replacement.");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        GameObject player;
        if (players.Length == 0)
        {
            player = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene); player.name = "InvisibleGhost";
            var curtain = transforms.FirstOrDefault(t => t.name == "Curtain");
            var stair = transforms.FirstOrDefault(t => t.name == "stair");
            Vector3 position = scene.path == Scenes[0] && curtain != null ? curtain.position + new Vector3(1.9f, 0, -0.8f)
                : scene.path == Scenes[1] && stair != null ? stair.position + new Vector3(0, 0, 1.25f)
                : new Vector3(-4.6f, 0.38f, -1.5f);
            position.y = 0.38f; player.transform.position = position;
            PrefabUtility.RecordPrefabInstancePropertyModifications(player.transform);
        }
        else
        {
            player = players[0].gameObject;
            if (PrefabUtility.GetCorrespondingObjectFromSource(player) != prefab)
                throw new InvalidOperationException("Existing InvisibleGhost is not the unified prefab. Preserve and migrate it explicitly before rebuilding.");
        }
        var camera = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>(true)).FirstOrDefault(c => c.CompareTag("MainCamera"));
        if (scene.path == Scenes[0] || scene.path == Scenes[1])
        {
            var playable = transforms.Select(t => t.GetComponent<WhiteboxPlayable>()).FirstOrDefault(p => p != null);
            if (playable == null) { var go = new GameObject("WhiteboxPlayable"); SceneManager.MoveGameObjectToScene(go, scene); playable = go.AddComponent<WhiteboxPlayable>(); }
            SetReference(playable, "player", player.transform); SetReference(playable, "followCamera", camera);
            var stair = transforms.FirstOrDefault(t => t.name == "stair"); SetReference(playable, "stair", stair);
            var curtain = scene.path == Scenes[0] ? transforms.FirstOrDefault(t => t.name == "Curtain") : null;
            SetReference(playable, "curtainSpot", curtain);
            if (curtain != null)
            {
                var encounter = curtain.GetComponent<LevelCurtainEncounter>();
                if (encounter == null) encounter = curtain.gameObject.AddComponent<LevelCurtainEncounter>();
                var prank = curtain.GetComponent<GhostPartyPrototype>();
                if (prank == null) prank = curtain.gameObject.AddComponent<GhostPartyPrototype>();
                SetReference(prank, "ghost", player.transform); SetReference(playable, "curtainPrank", encounter);
            }
        }
        else
        {
            var prank = transforms.Select(t => t.GetComponent<GhostPartyPrototype>()).FirstOrDefault(p => p != null);
            if (prank == null) { var go = new GameObject("GhostPartyPrototype"); SceneManager.MoveGameObjectToScene(go, scene); prank = go.AddComponent<GhostPartyPrototype>(); }
            SetReference(prank, "ghost", player.transform);
        }
        var audit = new SceneAudit { path = scene.path, players = 1, playerSource = AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(player)) };
        var walls = transforms.FirstOrDefault(t => t.name == "02_BlackPlanLines_Walls_2m_80mm");
        if (walls != null)
        {
            foreach (var renderer in walls.GetComponentsInChildren<Renderer>(true))
            {
                audit.wallRenderers++;
                var colliders = renderer.GetComponents<Collider>();
                if (colliders.Length == 0)
                {
                    var mesh = renderer.GetComponent<MeshFilter>();
                    if (mesh == null || mesh.sharedMesh == null) throw new InvalidOperationException("Wall has no collision geometry: " + renderer.name);
                    var collider = renderer.gameObject.AddComponent<MeshCollider>(); collider.sharedMesh = mesh.sharedMesh;
                    colliders = new Collider[] { collider }; audit.repairedWalls++;
                }
                if (!colliders.Any(c => c.enabled && !c.isTrigger))
                { colliders[0].enabled = true; colliders[0].isTrigger = false; audit.repairedWalls++; }
                foreach (var collider in colliders.Where(c => c.enabled && !c.isTrigger))
                {
                    if (Physics.GetIgnoreLayerCollision(player.layer, collider.gameObject.layer)) throw new InvalidOperationException("Player/wall layer pair is disabled: " + collider.name);
                    audit.solidWallColliders++;
                    audit.wallDetails.Add(collider.name + " | " + collider.GetType().Name + " | layer " + collider.gameObject.layer
                        + " | bounds " + collider.bounds + " | trigger " + collider.isTrigger);
                }
            }
        }
        return audit;
    }

    static void SetReference(UnityEngine.Object owner, string name, UnityEngine.Object value)
    {
        var serialized = new SerializedObject(owner); var property = serialized.FindProperty(name);
        if (property == null) throw new InvalidOperationException("Missing serialized reference " + owner.name + "." + name);
        property.objectReferenceValue = value; serialized.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(owner);
    }
    static void EnsureFolder(string parent, string name) { if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name); }
}
