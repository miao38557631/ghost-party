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
    }

    public void Hide() { if (bubble != null) bubble.enabled = false; }
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
