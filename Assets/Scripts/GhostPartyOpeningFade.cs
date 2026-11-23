using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Keeps one full-screen veil intact while the opening gives way to the tutorial.</summary>
public sealed class GhostPartyOpeningFade : MonoBehaviour
{
    public const float CoverSeconds = .22f;
    const float RevealSeconds = .30f;
    static readonly Color Ink = new Color(.13f, .06f, .13f);

    static GhostPartyOpeningFade active;
    Image veil;

    public static bool IsActive => active != null;

    public float Opacity
    {
        get => veil.color.a;
        set => veil.color = new Color(Ink.r, Ink.g, Ink.b, value);
    }

    public static GhostPartyOpeningFade Create()
    {
        var root = new GameObject("Opening transition", typeof(RectTransform), typeof(Canvas),
            typeof(GraphicRaycaster), typeof(GhostPartyOpeningFade));
        return root.GetComponent<GhostPartyOpeningFade>();
    }

    void Awake()
    {
        active = this;
        DontDestroyOnLoad(gameObject);
        var canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;

        veil = new GameObject("Transition veil", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        var rect = veil.rectTransform;
        rect.SetParent(transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        Opacity = 0f;
    }

    void OnDestroy()
    {
        if (active == this) active = null;
    }

    public void RevealAfter(AsyncOperation sceneLoad)
    {
        StartCoroutine(Handoff(sceneLoad));
    }

    IEnumerator Handoff(AsyncOperation sceneLoad)
    {
        if (Opacity < 1f)
            yield return FadeTo(1f, CoverSeconds * (1f - Opacity));

        sceneLoad.allowSceneActivation = true;
        yield return sceneLoad;
        // Give the tutorial's camera and HUD their first frame under the veil.
        yield return null;

        yield return FadeTo(0f, RevealSeconds);
        Destroy(gameObject);
    }

    IEnumerator FadeTo(float end, float duration)
    {
        float start = Opacity;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            yield return null;
            elapsed += Time.unscaledDeltaTime;
            Opacity = Mathf.SmoothStep(start, end, elapsed / duration);
        }

        Opacity = end;
    }
}
