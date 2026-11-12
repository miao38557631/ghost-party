using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Authoring helpers for per-section foreground wall cutaways.</summary>
public static class GhostWallCutoutTool
{
    [MenuItem("Ghost Party/Level/Attach Wall Cutout To Selection")]
    public static void AttachToSelection()
    {
        GameObject[] selection = Selection.gameObjects;
        if (selection.Length == 0)
        {
            Debug.LogWarning("Select the wall object(s) first, then run this again.");
            return;
        }
        for (int i = 0; i < selection.Length; i++)
        {
            GhostWallCutout cutout = selection[i].GetComponent<GhostWallCutout>();
            if (cutout == null) cutout = Undo.AddComponent<GhostWallCutout>(selection[i]);
            EditorSceneManager.MarkSceneDirty(selection[i].scene);
            Debug.Log("GHOST_WALL_CUTOUT: " + selection[i].name + Describe(selection[i]));
        }
    }

    [MenuItem("Ghost Party/Level/Report Wall Cutout")]
    public static void Report()
    {
        Scene scene = SceneManager.GetActiveScene();
        Transform wall = GhostWallCutout.ResolveWallRoot(scene);
        var text = new StringBuilder();
        if (wall == null)
        {
            text.AppendLine("No wall object resolved. Name the wall object with 墙/wall, or attach GhostWallCutout to it.");
        }
        else
        {
            text.AppendLine("WALL ROOT: " + wall.name + Describe(wall.gameObject));
            GhostWallCutout cutout = wall.GetComponent<GhostWallCutout>();
            text.AppendLine("  component: " + (cutout != null
                ? "present, remaining wall height " + cutout.RemainingHeight.ToString("0.00") + " world units"
                : "missing (Play adds it automatically)"));
            Renderer[] renderers = wall.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length && i < 12; i++)
            {
                Material material = renderers[i].sharedMaterial;
                text.AppendLine("  renderer " + renderers[i].name + " -> " +
                                (material != null ? material.shader.name : "no material"));
            }
        }
        Debug.Log("GHOST_WALL_CUTOUT_REPORT\n" + text);
    }

    static string Describe(GameObject target)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        int supported = 0;
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i].sharedMaterial != null && renderers[i].sharedMaterial.HasProperty("_GhostCutawayEnabled")) supported++;
        return "  renderers=" + renderers.Length + "  cutout-capable materials=" + supported +
               (renderers.Length > 0 && supported == 0 ? "  -> assign a material using GhostParty/DecorationTransparent" : "");
    }
}
