using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CurtainClothIntegrationSetup
{
    [MenuItem("Ghost Party/Curtain/Bind White Curtain In Level01")]
    public static void Bind()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Exit Play Mode first.");
        if (SceneManager.GetActiveScene().path != "Assets/LevelWhitebox/Scenes/Level01_FirstFloor.unity")
            throw new System.InvalidOperationException("Open Level01_FirstFloor first.");
        var interaction = GameObject.Find("Curtain");
        var cloth = Object.FindObjectOfType<SpineCurtainDragIK>();
        if (interaction == null || cloth == null) throw new System.InvalidOperationException("Curtain and Curtain Drag IK Preview are required.");
        var prank = interaction.GetComponent<GhostPartyPrototype>();
        Undo.RecordObjects(new Object[] { cloth.transform, cloth, prank, interaction.GetComponent<MeshRenderer>() }, "Bind white curtain");
        // The imported mesh has local center (-.01, 2.13, 0), size (1.66, 4.18, 0).
        // Fit its height to the old placeholder without distorting the rig.
        float scale = interaction.transform.localScale.y / 4.18f;
        cloth.transform.localScale = Vector3.one * scale;
        cloth.transform.rotation = interaction.transform.rotation;
        cloth.transform.position = interaction.transform.position
            - cloth.transform.rotation * (new Vector3(-0.01f, 2.13f, 0f) * scale)
            + interaction.transform.forward * 0.19f;
        var settings = new SerializedObject(cloth);
        settings.FindProperty("gameplayControlled").boolValue = true;
        settings.ApplyModifiedProperties();
        var owner = new SerializedObject(prank);
        owner.FindProperty("curtainVisual").objectReferenceValue = cloth;
        owner.ApplyModifiedProperties();
        interaction.GetComponent<MeshRenderer>().enabled = false;
        PrefabUtility.RecordPrefabInstancePropertyModifications(cloth.transform);
        PrefabUtility.RecordPrefabInstancePropertyModifications(cloth);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        Selection.activeGameObject = cloth.gameObject;
        Debug.Log("White curtain bound: fixed interaction anchor, shared rhythm input, ghost follows solved hem.");
    }
}
