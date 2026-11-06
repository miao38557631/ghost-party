using System;
using UnityEngine;

/// <summary>Shared, persistent Inspector tuning for controller input and confirmed-hit feedback.</summary>
[CreateAssetMenu(menuName = "Ghost Party/Input Settings")]
public sealed class GhostInputSettings : ScriptableObject
{
    [Header("Left stick (raw values, radial dead zone)")]
    [Range(0.05f, 0.45f)] public float movementDeadzone = 0.24f;
    [Range(0.3f, 0.9f)] public float deviceSwitchThreshold = 0.4f;
    [Range(0.4f, 0.95f)] public float directionThreshold = 0.65f;
    [Range(0.1f, 0.4f)] public float directionReleaseThreshold = 0.28f;
    [Range(0f, 20f)] public float directionHysteresisDegrees = 8f;
    [Header("RT grip hysteresis")]
    [Range(0.1f, 1f)] public float triggerPressThreshold = 0.55f;
    [Range(0f, 0.9f)] public float triggerReleaseThreshold = 0.4f;
    [Serializable]
    public struct Pulse
    {
        [Range(0f, 1f)] public float lowFrequency;
        [Range(0f, 1f)] public float highFrequency;
        [Min(0f)] public float durationMilliseconds;
        public Pulse(float low, float high, float milliseconds)
        { lowFrequency = low; highFrequency = high; durationMilliseconds = milliseconds; }
    }
    [Header("Confirmed rhythm actions (motor values are peak strength)")]
    public Pulse windowTap = new Pulse(0.35f, 0.50f, 80f);
    public Pulse directionHit = new Pulse(0.45f, 0.60f, 100f);
    public Pulse curtainHit = new Pulse(0.80f, 0.95f, 280f);
    public Pulse holdHit = new Pulse(0.60f, 0.70f, 140f);
    public Pulse finalThrow = new Pulse(1.00f, 0.90f, 280f);
    [Tooltip("Equal-duration strength steps for impact hits. 0.6, 1, 0.8, 0.6, 0.4 corresponds to 3-5-4-3-2.")]
    [Range(0f, 1f)] public float[] impactPattern = { 0.6f, 1f, 0.8f, 0.6f, 0.4f };
    [Tooltip("Equal-duration strength steps for the final action, adjustable independently of normal impacts.")]
    [Range(0f, 1f)] public float[] finalThrowPattern = { 0.6f, 1f, 0.8f, 0.6f, 0.4f };
}
