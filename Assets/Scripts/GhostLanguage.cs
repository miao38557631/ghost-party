using System;
using UnityEngine;
using Language = GhostPartyPrototype.Language;

/// <summary>One saved player language shared by every scene and UI.</summary>
public static class GhostLanguage
{
    public const string PreferenceKey = "GhostParty.Language";
    static bool loaded;
    static Language current;
    public static event Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSession() { loaded = false; Changed = null; }

    public static Language Current
    {
        get
        {
            if (!loaded)
            {
                int saved = PlayerPrefs.GetInt(PreferenceKey, -1);
                current = saved == 0 || saved == 1 ? (Language)saved : DefaultFor(Application.systemLanguage);
                loaded = true;
            }
            return current;
        }
    }
    public static bool English => Current == Language.English;
    public static string Text(string chinese, string english) => English ? english : chinese;

    static Language DefaultFor(SystemLanguage system)
    {
        return system == SystemLanguage.Chinese || system == SystemLanguage.ChineseSimplified || system == SystemLanguage.ChineseTraditional
            ? Language.Chinese : Language.English;
    }

    public static void Set(Language value)
    {
        if (value != Language.Chinese && value != Language.English) return;
        bool changed = Current != value;
        current = value;
        loaded = true;
        PlayerPrefs.SetInt(PreferenceKey, (int)value);
        PlayerPrefs.Save();
        if (changed) Changed?.Invoke();
    }
    public static void Toggle() => Set(English ? Language.Chinese : Language.English);
}
