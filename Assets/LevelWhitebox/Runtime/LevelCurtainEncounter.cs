using UnityEngine;

namespace GhostParty.LevelWhitebox
{
    /// <summary>Level01's editable curtain/NPC location and bridge to the rhythm prank.</summary>
    [DisallowMultipleComponent]
    public sealed class LevelCurtainEncounter : MonoBehaviour
    {
        GhostPartyPrototype prank;
        PrankCameraRig cameraRig;

        void Start()
        {
            prank = GetComponent<GhostPartyPrototype>();
            if (prank == null) prank = gameObject.AddComponent<GhostPartyPrototype>();
            cameraRig = Camera.main != null ? Camera.main.GetComponent<PrankCameraRig>() : null;
            prank.EmbeddedPrankFinished += OnPrankFinished;
        }

        public bool IsActive { get { return prank != null && prank.IsEmbeddedPrankActive; } }

        public void TryStart()
        {
            if (prank == null) return;
            prank.StartEmbeddedPrank();
            if (prank.IsEmbeddedPrankActive && cameraRig != null) cameraRig.EnterCloseup(transform);
        }

        void OnPrankFinished()
        {
            if (cameraRig != null) cameraRig.ExitCloseup();
        }

        void OnDestroy()
        {
            if (prank != null) prank.EmbeddedPrankFinished -= OnPrankFinished;
        }
    }
}
