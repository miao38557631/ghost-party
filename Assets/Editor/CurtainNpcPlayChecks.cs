using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class CurtainNpcPlayChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Ghost Party/Level/Check Curtain NPC Attraction (Play)")]
    public static void Check()
    {
        if (!EditorApplication.isPlaying)
        {
            Debug.LogWarning("Enter Play mode before checking curtain NPC attraction.");
            return;
        }
        GhostPartyPrototype prank = UnityEngine.Object.FindObjectOfType<GhostPartyPrototype>();
        if (prank == null || !prank.CanStartEmbeddedPrank) throw new InvalidOperationException("Level01 curtain is not ready in Explore mode.");
        Transform curtain = prank.transform;
        PrankNpcActor expected = null;
        float radius = (float)typeof(GhostPartyPrototype).GetField("npcAttractionRadius", Private).GetValue(prank);
        float nearestDistance = radius * radius;
        foreach (PrankNpcActor actor in UnityEngine.Object.FindObjectsOfType<PrankNpcActor>())
        {
            Vector3 offset = actor.transform.position - curtain.position;
            float distance = offset.x * offset.x + offset.z * offset.z;
            if (distance > nearestDistance) continue;
            nearestDistance = distance;
            expected = actor;
        }
        if (expected == null) throw new InvalidOperationException("No active NPC is within the curtain attraction circle.");

        MethodInfo select = typeof(GhostPartyPrototype).GetMethod("SelectNearestGuest", Private);
        if (select == null || !(bool)select.Invoke(prank, null)) throw new InvalidOperationException("Curtain failed to select an NPC.");
        Transform selected = (Transform)typeof(GhostPartyPrototype).GetField("guest", Private).GetValue(prank);
        Vector3 spot = (Vector3)typeof(GhostPartyPrototype).GetField("curtainGuestSpot", Private).GetValue(prank);
        if (selected != expected.transform) throw new InvalidOperationException("Curtain did not choose the nearest NPC.");
        if (Mathf.Abs(spot.x - (curtain.position.x + 2f)) > 0.01f ||
            Mathf.Abs(spot.z - curtain.position.z) > 0.01f)
            throw new InvalidOperationException("Guest spot is not curtain world-X +2.");

        NpcNavMover mover = selected.GetComponent<NpcNavMover>();
        Vector3 original = selected.position;
        try
        {
            if (mover != null && mover.Agent.enabled) throw new InvalidOperationException("Selected NPC navigation was not suspended for the scripted approach.");
            selected.position += Vector3.right * 4.3f;
            typeof(GhostPartyPrototype).GetMethod("UpdateEmbeddedExplore", Private).Invoke(prank, null);
            if (Vector3.Distance(selected.position, original + Vector3.right * 4.3f) > 0.01f)
                throw new InvalidOperationException("Roaming NPC teleported back when crossing the old X limit.");
        }
        finally
        {
            selected.position = original;
            if (mover != null) mover.ResumeNavigation();
        }
        Debug.Log("CURTAIN_NPC_CHECKS: nearest within 8, world-X +2 target, and no wandering snap passed (" + selected.name + ").");
    }
}
