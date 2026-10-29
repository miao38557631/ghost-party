using System;
using UnityEngine;

/// <summary>The generated BGM's clap/snare accents: beat indices 1, 3, 5...</summary>
public static class RhythmAccent
{
    public const int BeatsPerCycle = 2;
    public const int FirstAccentBeat = 1;
    static bool clockConfigured;
    static double clockStartDsp;
    static double clockSecondsPerBeat;
    static double heardOutputCompensation;

    public static void ConfigureClock(double startDsp, double secondsPerBeat, double outputCompensation)
    {
        clockStartDsp = startDsp;
        clockSecondsPerBeat = Math.Max(0.0001, secondsPerBeat);
        heardOutputCompensation = Math.Max(0.0, outputCompensation);
        clockConfigured = true;
    }

    public static void ClearClock() { clockConfigured = false; }

    public static bool TryGetBeat(out double beatIndex, out double secondsPerBeat)
    {
        secondsPerBeat = clockSecondsPerBeat;
        beatIndex = clockConfigured
            ? Math.Max(0.0, (AudioSettings.dspTime - clockStartDsp - heardOutputCompensation) / clockSecondsPerBeat)
            : 0.0;
        return clockConfigured;
    }

    public static bool IsAccentBeat(long wholeBeat)
    {
        return wholeBeat >= FirstAccentBeat &&
            (wholeBeat - FirstAccentBeat) % BeatsPerCycle == 0;
    }

    public static long CycleAtBeat(double beatIndex)
    {
        return (long)Math.Floor((beatIndex - FirstAccentBeat) / BeatsPerCycle);
    }

    public static double PhaseInCycle(double beatIndex)
    {
        return (beatIndex - FirstAccentBeat) / BeatsPerCycle - CycleAtBeat(beatIndex);
    }
}
