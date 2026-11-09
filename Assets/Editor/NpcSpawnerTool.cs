using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Authoring helper: creates the empty NPC_Spawner object the designer tunes in the inspector.</summary>
public static class NpcSpawnerTool
{
    [MenuItem("Ghost Party/Level/Create NPC Spawner")]
    public static void CreateSpawner()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("Stop Play mode before creating the spawner.");
            return;
        }
        var existing = Object.FindObjectOfType<NpcSpawner>();
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            Debug.Log("NPC_SPAWNER: already in the scene, selected it.");
            return;
        }

        var spawner = new GameObject("NPC_Spawner");
        Undo.RegisterCreatedObjectUndo(spawner, "Create NPC spawner");
        Undo.AddComponent<NpcSpawner>(spawner);
        EditorSceneManager.MarkSceneDirty(spawner.scene);
        Selection.activeGameObject = spawner;
        Debug.Log("NPC_SPAWNER: created. Tune count / spacing / ground offset, then Play.");
    }

    [MenuItem("Ghost Party/Level/Report NPC Ground Contact")]
    public static void Report()
    {
        NpcGroundContact[] contacts = Object.FindObjectsOfType<NpcGroundContact>(true);
        PrankNpcActor[] actors = Object.FindObjectsOfType<PrankNpcActor>(true);
        var text = new System.Text.StringBuilder();
        text.AppendLine("NPC ground contact: " + contacts.Length + " contacts / " + actors.Length + " actors");
        for (int i = 0; i < actors.Length; i++)
        {
            if (actors[i] == null) continue;
            NpcGroundContact contact = actors[i].GetComponent<NpcGroundContact>();
            text.AppendLine("  " + actors[i].name + "  contact=" + (contact != null ? "yes, offset " + contact.GroundOffset : "MISSING") +
                            "  position=" + actors[i].transform.position);
        }
        Debug.Log("NPC_GROUND_REPORT\n" + text);
    }
}
