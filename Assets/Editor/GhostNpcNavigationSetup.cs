using Unity.AI.Navigation;
using Unity.AI.Navigation.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>Sets up floor navigation and roaming for the five first-floor guests.</summary>
public static class GhostNpcNavigationSetup
{
    [MenuItem("Ghost Party/Level/Check NPC Navigation")]
    public static void Check()
    {
        if (!EditorApplication.isPlaying)
        {
            Debug.Log("NPC_NAV_CHECK: run this check in Play mode, when the baked NavMesh is active.");
            return;
        }
        GameObject[] guests = new GameObject[5];
        guests[0] = GameObject.Find("CurtainGuest");
        for (int i = 1; i < guests.Length; i++) guests[i] = GameObject.Find("PartyGuest_" + i.ToString("00"));
        int sampled = 0;
        int complete = 0;
        int pairs = 0;
        for (int i = 0; i < guests.Length; i++)
        {
            if (guests[i] == null) continue;
            NavMeshAgent guestAgent = guests[i].GetComponent<NavMeshAgent>();
            NavMeshHit from;
            bool onMesh = NavMesh.SamplePosition(guests[i].transform.position, out from, 1.5f, NavMesh.AllAreas);
            Debug.Log("NPC_NAV_GUEST: " + guests[i].name + " at " + guests[i].transform.position +
                      ", nearest=" + (onMesh ? from.position.ToString() : "none") +
                      ", agentEnabled=" + (guestAgent != null && guestAgent.enabled) +
                      ", agentOnMesh=" + (guestAgent != null && guestAgent.enabled && guestAgent.isOnNavMesh) +
                      ", velocity=" + (guestAgent != null && guestAgent.enabled && guestAgent.isOnNavMesh ? guestAgent.velocity.ToString() : "none"));
            if (!onMesh || guestAgent == null || !guestAgent.enabled || !guestAgent.isOnNavMesh) continue;
            sampled++;
            for (int j = i + 1; j < guests.Length; j++)
            {
                if (guests[j] == null) continue;
                NavMeshHit to;
                NavMeshAgent otherAgent = guests[j].GetComponent<NavMeshAgent>();
                if (otherAgent == null || !otherAgent.enabled || !otherAgent.isOnNavMesh ||
                    !NavMesh.SamplePosition(guests[j].transform.position, out to, 1.5f, NavMesh.AllAreas)) continue;
                var path = new NavMeshPath();
                bool found = NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, path);
                pairs++;
                if (found && path.status == NavMeshPathStatus.PathComplete) complete++;
                Debug.Log("NPC_NAV_PATH: " + guests[i].name + " -> " + guests[j].name + " = " + path.status);
            }
        }
        Debug.Log("NPC_NAV_CHECK: " + sampled + "/5 guests on NavMesh, " + complete + "/" + pairs + " complete routes.");
    }

    [MenuItem("Ghost Party/Level/Setup NPC Navigation")]
    public static void Setup()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("Stop Play mode before setting up NPC navigation.");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        GameObject floor = GameObject.Find("1_Floor");
        if (floor == null || floor.scene != scene || floor.GetComponent<Collider>() == null)
        {
            Debug.LogError("NPC navigation needs the authored MeshCollider on 1_Floor in the active scene.");
            return;
        }

        GameObject navigationRoot = GameObject.Find("NPC_NavigationRoot");
        if (navigationRoot == null || navigationRoot.scene != scene)
        {
            navigationRoot = new GameObject("NPC_NavigationRoot");
            Undo.RegisterCreatedObjectUndo(navigationRoot, "Create NPC navigation root");
        }
        if (floor.transform.parent != navigationRoot.transform)
            Undo.SetTransformParent(floor.transform, navigationRoot.transform, "Include the floor in NPC navigation");
        // Children collection excludes the object holding the surface. The floor must be its child.
        NavMeshSurface oldSurface = floor.GetComponent<NavMeshSurface>();
        if (oldSurface != null) Undo.DestroyObjectImmediate(oldSurface);
        NavMeshSurface surface = navigationRoot.GetComponent<NavMeshSurface>();
        if (surface == null) surface = Undo.AddComponent<NavMeshSurface>(navigationRoot);
        Undo.RecordObject(surface, "Configure NPC NavMesh surface");
        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.layerMask = ~0;
        surface.agentTypeID = 0;
        surface.minRegionArea = 0f;
        EditorUtility.SetDirty(surface);

        int configured = 0;
        for (int i = 0; i <= 4; i++)
        {
            GameObject guest = GameObject.Find(i == 0 ? "CurtainGuest" : "PartyGuest_" + i.ToString("00"));
            if (guest == null || guest.scene != scene) continue;
            NavMeshAgent agent = guest.GetComponent<NavMeshAgent>();
            if (agent == null) agent = Undo.AddComponent<NavMeshAgent>(guest);
            Undo.RecordObject(agent, "Configure NPC NavMesh agent");
            agent.agentTypeID = 0;
            agent.radius = 0.35f;
            agent.height = 1.6f;
            agent.baseOffset = i == 0 ? 0.74f : 0.53f;
            agent.speed = 2.2f;
            agent.acceleration = 8f;
            agent.angularSpeed = 360f;
            agent.stoppingDistance = 0.15f;
            agent.autoBraking = true;
            agent.updateRotation = false;
            agent.enabled = i != 0; // CurtainGuest snaps onto the baked NavMesh in NpcNavMover.Start.
            EditorUtility.SetDirty(agent);
            if (guest.GetComponent<PrankNpcActor>() == null) Undo.AddComponent<PrankNpcActor>(guest);
            NpcNavMover mover = guest.GetComponent<NpcNavMover>();
            if (mover == null) mover = Undo.AddComponent<NpcNavMover>(guest);
            Undo.RecordObject(mover, "Enable NPC random roaming");
            mover.SetRandomRoam(true);
            EditorUtility.SetDirty(mover);
            configured++;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        NavMeshAssetManager.instance.StartBakingSurfaces(new Object[] { surface });
        Debug.Log("NPC_NAV_SETUP: baking 1_Floor MeshCollider for " + configured +
                  " roaming guests. CurtainGuest navigation pauses while the curtain encounter controls it.");
    }
}
