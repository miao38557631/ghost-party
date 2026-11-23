using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>The launch menu's start button and its presentation.</summary>
[RequireComponent(typeof(Button), typeof(AudioSource))]
public sealed class GhostPartyMainMenu : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler
{
    public const string ScenePath = "Assets/Scenes/MainMenuScene.unity";

    static readonly Color HoverColor = new Color(1f, .74f, .17f, .9f);
    static readonly Color PressColor = new Color(.37f, .13f, .25f, .8f);

    Button startButton;
    AudioSource audioSource;
    AudioClip hoverSound, clickSound;
    Image accent;
    Texture2D accentTexture;
    Sprite accentSprite;
    bool hovered, pointerHeld, confirming;

    void Awake()
    {
        startButton = GetComponent<Button>();
        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        hoverSound = CreateTone("Menu hover", .085f, 570f, 920f);
        clickSound = CreateTone("Menu confirm", .155f, 545f, 285f);

        var overlay = new GameObject("Start Game Feedback", typeof(RectTransform), typeof(Image));
        overlay.transform.SetParent(transform, false);
        accent = overlay.GetComponent<Image>();
        accent.raycastTarget = false;
        accent.color = Color.clear;
        var rect = accent.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        accentTexture = CreateAccent();
        accentSprite = Sprite.Create(accentTexture, new Rect(0f, 0f, accentTexture.width, accentTexture.height),
            Vector2.one * .5f, 100f, 0, SpriteMeshType.FullRect);
        accent.sprite = accentSprite;
    }

    void OnEnable() { startButton.onClick.AddListener(StartGame); }
    void OnDisable() { startButton.onClick.RemoveListener(StartGame); }

    void OnDestroy()
    {
        Destroy(accentSprite);
        Destroy(accentTexture);
        Destroy(hoverSound);
        Destroy(clickSound);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (hovered || confirming || !startButton.IsInteractable()) return;
        hovered = true;
        audioSource.PlayOneShot(hoverSound, .38f);
    }

    public void OnPointerExit(PointerEventData eventData) { hovered = false; }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left && startButton.IsInteractable())
            pointerHeld = true;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left) pointerHeld = false;
    }

    void Update()
    {
        bool pressed = confirming || (pointerHeld && hovered);
        Color tint = pressed ? PressColor : hovered ? HoverColor : Color.clear;
        var rect = accent.rectTransform;
        float shift = (pressed ? -.018f : hovered ? .022f : 0f) * rect.rect.height;
        float blend = 1f - Mathf.Exp(-35f * Time.unscaledDeltaTime);
        accent.color = Color.Lerp(accent.color, tint, blend);
        rect.anchoredPosition = Vector2.Lerp(rect.anchoredPosition, new Vector2(0f, shift), blend);
    }

    void StartGame()
    {
        if (confirming || !startButton.IsInteractable()) return;
        confirming = true;
        startButton.interactable = false;
        audioSource.Stop();
        audioSource.PlayOneShot(clickSound, .65f);
        StartCoroutine(PlayOpening());
    }

    IEnumerator PlayOpening()
    {
        yield return new WaitForSecondsRealtime(clickSound.length + .035f);
        SceneManager.LoadSceneAsync(GhostPartyOpening.ScenePath);
    }

    static AudioClip CreateTone(string title, float seconds, float startHz, float endHz)
    {
        const int rate = 44100;
        var samples = new float[Mathf.RoundToInt(rate * seconds)];
        float phase = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float progress = (float)i / samples.Length;
            phase += 2f * Mathf.PI * Mathf.Lerp(startHz, endHz, progress) / rate;
            float envelope = Mathf.Min(i / (rate * .005f), 1f) * (1f - progress) * (1f - progress);
            samples[i] = (Mathf.Sin(phase) + .18f * Mathf.Sin(phase * 2f)) * envelope * .55f;
        }
        var clip = AudioClip.Create(title, samples.Length, 1, rate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    static Texture2D CreateAccent()
    {
        const int width = 192;
        const int height = 48;
        const float radius = 22f;
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float dx = Mathf.Max(0f, Mathf.Abs(x + .5f - width * .5f) - (width * .5f - radius - 1f));
                float dy = Mathf.Max(0f, Mathf.Abs(y + .5f - height * .5f) - (height * .5f - radius - 1f));
                float edge = Mathf.Sqrt(dx * dx + dy * dy) - radius;
                float coverage = Mathf.Clamp01(.5f - edge);
                float outline = Mathf.Clamp01(edge + 1.7f);
                byte alpha = (byte)Mathf.RoundToInt(255f * coverage * Mathf.Lerp(.22f, 1f, outline));
                pixels[y * width + x] = new Color32(255, 255, 255, alpha);
            }
        }
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "Start Game highlight",
            wrapMode = TextureWrapMode.Clamp
        };
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }
}
