using UnityEngine;

/// <summary>Shared ghost root, existing ground input, and a separately authored visual.</summary>
public static class GhostPlayerAvatar
{
    const string DefinitionResource = "Player/GhostPlayerDefinition";

    public static Transform Create(Vector3 position)
    {
        // Reuse authored players exactly as configured; never replace Visual or reset root settings.
        var existing = GameObject.Find("InvisibleGhost");
        if (existing != null) return existing.transform;
        var definition = Resources.Load<GhostPlayerPrefabDefinition>(DefinitionResource);
        if (definition == null || definition.playerPrefab == null)
        {
            Debug.LogError("Player prefab reference missing. Run Ghost Party > Player > Build Player Prefab and Bind Scenes.");
            return null;
        }
        var player = Object.Instantiate(definition.playerPrefab, position, definition.playerPrefab.transform.rotation);
        player.name = "InvisibleGhost";
        return player.transform;
    }

    public static void Move(Transform player, Vector3 input, float deltaTime)
    {
        if (player == null) return;
        var motor = player.GetComponent<GhostPlayerMotor>();
        if (motor == null) { Debug.LogError("InvisibleGhost must use the authored GhostPlayer prefab motor.", player); return; }
        motor.Move(input, deltaTime);
        SetMovement(player, input);
    }

    public static void Teleport(Transform player, Vector3 destination)
    {
        if (player == null) return;
        var motor = player.GetComponent<GhostPlayerMotor>();
        if (motor != null) motor.Teleport(destination);
        else Debug.LogError("Cannot relocate a player without GhostPlayerMotor.", player);
    }

    public static void SetMovement(Transform player, Vector3 existingMovement)
    {
        if (player == null) return;
        var visual = player.GetComponentInChildren<GhostPlayerVisual>(true);
        if (visual != null) visual.SetMovement(existingMovement);
    }

    /// <summary>All possession systems use this after success and when exploration resumes; never on raw E input.</summary>
    public static void SetVisible(Transform player, bool visible)
    {
        if (player == null) return;
        var visibility = player.GetComponent<GhostPlayerVisibility>();
        if (visibility == null) visibility = player.gameObject.AddComponent<GhostPlayerVisibility>();
        visibility.SetVisible(visible);
    }

    public static Vector3 ReadGroundInput()
    {
        return GhostInput.GroundMovement();
    }
}
