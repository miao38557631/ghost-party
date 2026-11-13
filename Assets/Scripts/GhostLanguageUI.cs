using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>A compact, persistent language control outside the diagnostic HUD.</summary>
[DefaultExecutionOrder(-900)]
public sealed class GhostLanguageUI : MonoBehaviour
{
    static GhostLanguageUI instance;
    Text label;
    string renderedLabel;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { instance = null; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        if (instance == null) instance = FindObjectOfType<GhostLanguageUI>();
        if (instance == null) new GameObject("LanguageUI").AddComponent<GhostLanguageUI>();
    }

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        var control = new GameObject("LanguageButton", typeof(RectTransform), typeof(Image), typeof(Button));
        control.transform.SetParent(transform, false);
        var rect = (RectTransform)control.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
        rect.anchoredPosition = new Vector2(-20f, -16f);
        rect.sizeDelta = new Vector2(208f, 42f);
        var background = control.GetComponent<Image>();
        background.color = new Color(0.08f, 0.07f, 0.1f, 0.65f);
        var button = control.GetComponent<Button>();
        button.targetGraphic = background;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(() =>
        {
            GhostLanguage.Toggle();
            // A later Space/A gameplay tap must not submit the old UI selection.
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        });
        var text = new GameObject("Label", typeof(RectTransform), typeof(Text));
        text.transform.SetParent(control.transform, false);
        var textRect = (RectTransform)text.transform;
        textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(6f, 0f); textRect.offsetMax = new Vector2(-6f, 0f);
        label = text.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 18;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.raycastTarget = false;
        RefreshLabel();
    }

    void OnEnable() { SceneManager.sceneLoaded += SceneLoaded; }
    void OnDisable() { SceneManager.sceneLoaded -= SceneLoaded; }
    void OnDestroy() { if (instance == this) instance = null; }
    void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Scene-owned input modules are reused; a floor with only IMGUI still
        // needs one to make the language button clickable.
        if (FindObjectOfType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
    }
    void Update()
    {
        if (GhostInput.LanguagePressed) GhostLanguage.Toggle();
        RefreshLabel();
    }
    void RefreshLabel()
    {
        string shortcut = GhostInput.UsingGamepad ? "View" : "L";
        string text = (GhostLanguage.English ? "中文 / <b>EN</b>" : "<b>中文</b> / EN") + "   [" + shortcut + "]";
        if (label != null && renderedLabel != text) { label.text = text; renderedLabel = text; }
    }
}
