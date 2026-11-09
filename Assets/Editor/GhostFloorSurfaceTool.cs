using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Authoring helpers for the floating ghost's floor source. Floor is declared, not guessed:
/// only marked objects (or the legacy generated/imported roots) feed the height probe and camera bounds.</summary>
public static class GhostFloorSurfaceTool
{
    [MenuItem("Ghost Party/Level/Mark Selection As Floor Surface")]
    public static void MarkSelection()
    {
        GameObject[] selection = Selection.gameObjects;
        if (selection.Length == 0)
        {
            Debug.LogWarning("Select the floor object(s) first, then run this again.");
            return;
        }
        int added = 0;
        for (int i = 0; i < selection.Length; i++)
        {
            if (selection[i].GetComponent<GhostFloorSurface>() != null) continue;
            Undo.AddComponent<GhostFloorSurface>(selection[i]);
            EditorSceneManager.MarkSceneDirty(selection[i].scene);
            added++;
        }
        Debug.Log("GHOST_FLOOR_MARKED: " + added + " added, " + (selection.Length - added) + " already marked. Save the scene to keep it.");
    }

    [MenuItem("Ghost Party/Level/Report Floor And Wall Sources")]
    public static void Report()
    {
        Scene scene = SceneManager.GetActiveScene();
        var floorSources = GhostFloorSurface.ResolveRoots(scene, null);
        var text = new StringBuilder();
        text.AppendLine("FLOOR SOURCES (" + floorSources.Count + ") - these drive the ghost height probe and camera turn range");
        for (int i = 0; i < floorSources.Count; i++)
            AppendSource(text, floorSources[i]);
        if (floorSources.Count == 0)
            text.AppendLine("  <none> - mark the floor object with GhostFloorSurface before entering Play");

        text.AppendLine();
        text.AppendLine("NOT FLOOR (ignored by the height probe; walls may stay collider-free)");
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            if (IsIn(roots[i].transform, floorSources)) continue;
            if (roots[i].name == "InvisibleGhost") continue;
            if (roots[i].GetComponentsInChildren<Collider>(true).Length == 0 &&
                roots[i].GetComponentsInChildren<MeshFilter>(true).Length == 0) continue;
            AppendSource(text, roots[i].transform, true);
        }

        Debug.Log("GHOST_FLOOR_SOURCES\n" + text);
    }

    static void AppendSource(StringBuilder text, Transform root, bool outsideFloor = false)
    {
        if (root == null) return;
        int meshes = root.GetComponentsInChildren<MeshFilter>(true).Length;
        int colliders = root.GetComponentsInChildren<Collider>(true).Length;
        int triggers = 0;
        Collider[] all = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < all.Length; i++) if (all[i].isTrigger) triggers++;
        string flags = "";
        if (!root.gameObject.activeInHierarchy) flags += " [inactive]";
        if (root.GetComponent<GhostFloorSurface>() != null) flags += " [marked]";
        if (GhostFloorSurface.LooksLikeFloorName(root.name)) flags += " [floor name]";
        if (outsideFloor && !GhostFloorSurface.LooksLikeFloorName(root.name) && root.GetComponent<GhostFloorSurface>() == null)
            flags += " [not in floor source]";
        text.AppendLine("  " + root.name + flags + "  meshes=" + meshes + " colliders=" + colliders + " triggers=" + triggers +
                        (colliders == 0 && meshes > 0 ? "  -> runtime adds query-only MeshColliders" : ""));
    }

    static bool IsIn(Transform candidate, System.Collections.Generic.List<Transform> roots)
    {
        for (int i = 0; i < roots.Count; i++)
            if (roots[i] != null && (candidate == roots[i] || candidate.IsChildOf(roots[i]))) return true;
        return false;
    }
}
