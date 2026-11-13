using UnityEngine;
using UnityEngine.UI;
using GhostParty.LevelWhitebox;

/// <summary>Presentation only: the floor's actual interaction resolver owns availability.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(LevelCurtainEncounter))]
[DefaultExecutionOrder(1000)]
public sealed class CurtainPossessionPrompt : MonoBehaviour
{
    [Header("Authored target and artwork")]
    [SerializeField] Transform promptAnchor;
    [SerializeField] Sprite keyboardSprite;
    [SerializeField] Sprite gamepadSprite;
    [Header("Layout at the existing 1920 x 1080 reference resolution")]
    [SerializeField] Vector2 displaySize = new Vector2(216f, 64f);
    [Tooltip("The PNG's left speech-bubble tip. Keep this at the scene anchor.")]
    [SerializeField] Vector2 tipPivot = new Vector2(0.019f, 0.5f);
    [SerializeField] Vector2 screenOffset;

    Canvas host;
    Camera view;
    Image bubble;
    GameObject chineseCaption;
    LevelCurtainEncounter encounter;
    WhiteboxPlayable floor;

    public Transform Anchor { get { return promptAnchor; } }
    public Image Bubble { get { return bubble; } }
    public bool IsVisible { get { return bubble != null && bubble.enabled && bubble.gameObject.activeInHierarchy; } }

    public void Bind(Canvas existingCanvas, Camera sceneCamera)
    {
        host = existingCanvas;
        view = sceneCamera;
        encounter = GetComponent<LevelCurtainEncounter>();
        floor = FindObjectOfType<WhiteboxPlayable>();
        if (host == null) return;
        if (bubble == null)
        {
            var go = new GameObject("PossessionPrompt", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(host.transform, false);
            bubble = go.GetComponent<Image>();
            bubble.raycastTarget = false;
            bubble.preserveAspect = true;
            bubble.color = Color.white;
            // Preserve the approved bubble outline and device badge. Only cover
            // the baked English word in Chinese mode, using live localized type.
            chineseCaption = new GameObject("ChineseCaption", typeof(RectTransform), typeof(Image));
            chineseCaption.transform.SetParent(bubble.transform, false);
            var captionRect = (RectTransform)chineseCaption.transform;
            captionRect.anchorMin = new Vector2(0.37f, 0.28f);
            captionRect.anchorMax = new Vector2(0.95f, 0.76f);
            captionRect.offsetMin = captionRect.offsetMax = Vector2.zero;
            var fill = chineseCaption.GetComponent<Image>();
            fill.color = Color.white; fill.raycastTarget = false;
            var word = new GameObject("Word", typeof(RectTransform), typeof(Text));
            word.transform.SetParent(chineseCaption.transform, false);
            var wordRect = (RectTransform)word.transform;
            wordRect.anchorMin = Vector2.zero; wordRect.anchorMax = Vector2.one;
            wordRect.offsetMin = wordRect.offsetMax = Vector2.zero;
            var text = word.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 28; text.fontStyle = FontStyle.Bold;
            text.resizeTextForBestFit = true; text.resizeTextMinSize = 22; text.resizeTextMaxSize = 28;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(0.08f, 0.09f, 0.1f);
            text.text = "附身"; text.raycastTarget = false;
        }
        Hide();
    }

    void LateUpdate()
    {
        if (bubble == null) return;
        bool eligible = isActiveAndEnabled && host != null && host.isActiveAndEnabled &&
            view != null && promptAnchor != null && promptAnchor.gameObject.activeInHierarchy &&
            floor != null && floor.IsCurrentPossessionTarget(encounter);
        if (!eligible) { Hide(); return; }
        Sprite sprite = GhostInput.UsingGamepad ? gamepadSprite : keyboardSprite;
        Vector3 point = view.WorldToScreenPoint(promptAnchor.position);
        if (sprite == null || point.z <= 0f || !view.pixelRect.Contains(point)) { Hide(); return; }
        var hostRect = (RectTransform)host.transform;
        Camera uiCamera = host.renderMode == RenderMode.ScreenSpaceOverlay ? null : host.worldCamera;
        Vector2 localPoint;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(hostRect, point, uiCamera, out localPoint))
        { Hide(); return; }
        var rect = bubble.rectTransform;
        rect.anchorMin = rect.anchorMax = hostRect.pivot;
        rect.pivot = tipPivot;
        rect.sizeDelta = displaySize;
        rect.anchoredPosition = localPoint + screenOffset;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
        bubble.sprite = sprite;
        bubble.enabled = true;
        chineseCaption.SetActive(!GhostLanguage.English);
    }

    public void Hide() { if (bubble != null) bubble.enabled = false; if (chineseCaption != null) chineseCaption.SetActive(false); }
    void OnDisable() { Hide(); }
    void OnDestroy() { if (bubble != null) Destroy(bubble.gameObject); }
    void OnValidate()
    {
        displaySize.x = Mathf.Max(1f, displaySize.x);
        displaySize.y = Mathf.Max(1f, displaySize.y);
        tipPivot.x = Mathf.Clamp01(tipPivot.x);
        tipPivot.y = Mathf.Clamp01(tipPivot.y);
    }
    void OnDrawGizmosSelected()
    {
        if (promptAnchor == null) return;
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(promptAnchor.position, 0.06f);
    }
}
