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
    [Header("Confirmed hits only")]
    public Pulse windowTap = new Pulse(0.10f, 0.20f, 45f);
    public Pulse curtainHit = new Pulse(0.25f, 0.38f, 65f);
    public Pulse finalThrow = new Pulse(0.65f, 0.45f, 140f);
}
