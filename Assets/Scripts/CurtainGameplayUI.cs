using UnityEngine;
using UnityEngine.UI;

/// <summary>Gameplay moves anchors, leaving the authored Canvas transforms and graphic styles editable.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1000)]
public sealed class CurtainGameplayUI : MonoBehaviour
{
    // The gameplay passes a snapshot from its existing audio clock and input
    // judgement. This component never reads input or awards a successful hit.
    public enum CueKind { Tap, Direction, Hold, Automatic, Result }

    public struct PlayerCue
    {
        public SpineCurtainDragIK curtainVisual;
        public CueKind kind;
        public string instruction, timing, icon, nextIcon, nextLabel;
        public float beatsToTarget, tolerance, motionProgress;
        public bool held, armed, recovering, motionReady;
        public bool inputPressed, usingGamepad, english, hasNextDirection;
        public string exitHint;
        public Vector2 gestureOrigin, gestureTarget, nextGestureTarget;
    }

    [Header("Current action")]
    [SerializeField] Canvas currentCueCanvas;
    [SerializeField] Text currentCueLabel;
    [SerializeField] Transform currentCueAnchor;
    [SerializeField] Transform beatPulse;
    [Header("Next action")]
    [SerializeField] Canvas nextCueCanvas;
    [SerializeField] Text nextCueLabel;
    [SerializeField] Transform nextCueAnchor;
    [Header("Guest escape")]
    [SerializeField] Canvas escapeCanvas;
    [SerializeField] Image escapeFill;
    [SerializeField] Text escapeLabel;
    [SerializeField] Transform escapeAnchor;
    [Header("Follow offsets (world units)")]
    [Tooltip("World displacement from the curtain. Edit the child Canvas for local layout.")]
    [SerializeField] Vector3 curtainOffset = new Vector3(-0.92f, -0.18f, -0.32f);
    [SerializeField, Min(0f)] float closeupDownOffset = 0.22f;
    [Tooltip("Camera-relative displacement: right / up / forward.")]
    [SerializeField] Vector3 nextCueOffset = new Vector3(-0.62f, -0.55f, 0f);
    [Tooltip("Camera-relative displacement from the guest: right / up / forward.")]
    [SerializeField] Vector3 escapeOffset = new Vector3(0.45f, 1.3f, 0f);
    [Header("Beat animation")]
    [SerializeField, Range(0f, 1f)] float beatPopStrength = 0.55f;

    [Header("Designer artwork (transparent, Full Rect sprites)")]
    [SerializeField] Sprite ghostWhite, ghostBlack, arrowWhite, arrowBlack;
    [SerializeField] Sprite ringOuter, ringInner, mouseIdle, mousePressed;
    [SerializeField] Sprite tapChinese, holdChinese, tapEnglish, holdEnglish;
    [Header("Interaction layout (1920 x 1080 reference pixels)")]
    [Tooltip("Viewport offset from the curtain object, before keeping the whole gesture on screen.")]
    [SerializeField] Vector2 playerHudOffset = new Vector2(0f, 0f);
    [SerializeField, Range(0.5f, 1.5f)] float playerHudScale = 1f;
    [SerializeField] Font playerHudFont;
    [SerializeField, Min(80f)] float ghostWidth = 170f;
    [SerializeField, Min(60f)] float beatRingDiameter = 188f;
    [SerializeField, Min(40f)] float targetDiameter = 118f;
    [SerializeField, Min(100f)] float routeHalfSpan = 245f;
    [SerializeField, Range(0.1f, 0.9f)] float previewStartProgress = 0.5f;
    [SerializeField, Range(0f, 1f)] float completedArrowAlpha = 0.14f;
    [SerializeField, Range(0f, 25f)] float ghostLeanDegrees = 10f;
    [Header("Cloth-bound cue size")]
    [SerializeField, Range(0.5f, 1f), Tooltip("贴住实体窗帘时幽灵和箭头的尺寸，给相邻抓取点留出空隙。")]
    float clothGhostScale = 0.75f;
    [SerializeField, Range(0.5f, 1f), Tooltip("贴住实体窗帘时目标圈的尺寸；位置始终对应实际可达抓取点。")]
    float clothTargetScale = 0.90f;

    RectTransform playerHud, actionVisual, ghostBody, targetVisual, nextVisual;
    Image ghostImage, actionLabel, mouseImage, timingRing, directionArrow;
    Image currentTargetRing, currentTargetCore, nextTargetRing, nextTargetCore;
    CanvasGroup nextGroup;
    Text playerInstruction, playerTiming, playerFeedback, deviceLabel;
    RectTransform instructionBackplate;
    float playerFeedbackUntil;
    PlayerCue lastPlayerCue;
    Camera lastPlayerView;
    Vector3 lastCurtainPosition;
    bool lastPlayerVisible;

    void LateUpdate()
    {
        // Run after camera composition and Spine pose evaluation. Camera shake
        // must not separate the icon from the solved cloth grab point.
        if (lastPlayerCue.curtainVisual != null)
            PresentPlayerCue(lastPlayerVisible, lastPlayerCue, lastPlayerView, lastCurtainPosition);
    }

    public bool HasDesignerArtwork { get { return ghostWhite && ghostBlack && arrowWhite && arrowBlack
        && ringOuter && ringInner && mouseIdle && mousePressed && tapChinese && holdChinese && tapEnglish && holdEnglish; } }

    public void AttachPlayerHud(Canvas host)
    {
        if (playerHud != null || host == null) return;
        if (!HasDesignerArtwork) { Debug.LogError("Curtain designer artwork is not bound on the UI prefab.", this); return; }
        playerHud = PlayerRect("CurtainPlayerHUD", host.transform, Vector2.zero, new Vector2(800f, 700f));
        var group = playerHud.gameObject.AddComponent<CanvasGroup>();
        group.interactable = group.blocksRaycasts = false;

        // Targets are below the moving ghost. They are guidance, never hit-test surfaces.
        nextVisual = PlayerRect("NextDragTarget", playerHud, Vector2.zero, Vector2.one * targetDiameter);
        nextGroup = nextVisual.gameObject.AddComponent<CanvasGroup>();
        nextTargetRing = Artwork("Ring", nextVisual, ringOuter, Vector2.zero, Vector2.one * targetDiameter);
        nextTargetCore = Artwork("Center", nextVisual, ringInner, Vector2.zero, Vector2.one * targetDiameter * 0.67f);
        targetVisual = PlayerRect("CurrentDragTarget", playerHud, Vector2.zero, Vector2.one * targetDiameter);
        currentTargetRing = Artwork("Ring", targetVisual, ringOuter, Vector2.zero, Vector2.one * targetDiameter);
        currentTargetCore = Artwork("Center", targetVisual, ringInner, Vector2.zero, Vector2.one * targetDiameter * 0.67f);
        directionArrow = Artwork("DirectionArrow", playerHud, arrowWhite, Vector2.zero, new Vector2(138f, 56f));
        actionVisual = PlayerRect("CurrentAction", playerHud, Vector2.zero, new Vector2(ghostWidth, ghostWidth));
        timingRing = Artwork("ApproachingBeat", actionVisual, ringOuter, Vector2.zero, Vector2.one * beatRingDiameter);
        ghostBody = PlayerRect("GhostBody", actionVisual, Vector2.zero, new Vector2(ghostWidth, ghostWidth));
        ghostImage = Artwork("Ghost", ghostBody, ghostBlack, Vector2.zero, new Vector2(ghostWidth, ghostWidth * 0.89f));
        actionLabel = Artwork("ActionWord", actionVisual, tapChinese, new Vector2(15f, -40f), new Vector2(90f, 42f));
        mouseImage = Artwork("MouseButton", actionVisual, mouseIdle, new Vector2(-47f, -40f), new Vector2(27f, 34f));
        deviceLabel = PlayerText("GamepadButton", actionVisual, new Vector2(-4f, -40f), new Vector2(95f, 42f), 29);
        instructionBackplate = PlayerRect("Caption", playerHud, Vector2.zero, new Vector2(410f, 64f));
        var back = instructionBackplate.gameObject.AddComponent<Image>();
        back.color = new Color(0.08f, 0.07f, 0.1f, 0.57f);
        back.raycastTarget = false;
        playerInstruction = PlayerText("Instruction", instructionBackplate, new Vector2(0f, 12f), new Vector2(394f, 28f), 20);
        playerTiming = PlayerText("Timing", instructionBackplate, new Vector2(0f, -14f), new Vector2(394f, 24f), 16);
        playerFeedback = PlayerText("JudgementFeedback", playerHud, Vector2.zero, new Vector2(310f, 36f), 24);
        playerHud.gameObject.SetActive(false);
    }

    public void PresentPlayerCue(bool visible, PlayerCue cue, Camera view, Vector3 curtainPosition)
    {
        lastPlayerCue = cue;
        lastPlayerView = view;
        lastCurtainPosition = curtainPosition;
        lastPlayerVisible = visible;
        if (playerHud == null) return;
        bool worldGrab = cue.curtainVisual != null && cue.curtainVisual.IsReady && view != null;
        if (worldGrab) curtainPosition = cue.curtainVisual.GrabWorldPosition;
        actionVisual.localScale = Vector3.one * (worldGrab ? clothGhostScale : 1f);
        directionArrow.rectTransform.localScale = actionVisual.localScale;
        targetVisual.localScale = nextVisual.localScale = Vector3.one * (worldGrab ? clothTargetScale : 1f);
        if (visible && view != null && view.WorldToViewportPoint(curtainPosition).z <= 0f) visible = false;
        playerHud.gameObject.SetActive(visible);
        if (!visible) { ClearPlayerFeedback(); return; }

        bool manual = cue.kind == CueKind.Tap || cue.kind == CueKind.Direction || cue.kind == CueKind.Hold;
        bool directional = cue.kind == CueKind.Direction;
        bool dragging = directional && cue.held && !cue.recovering;
        float progress = dragging ? Mathf.Clamp01(cue.motionProgress) : 0f;
        float handoff = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(previewStartProgress, 1f, progress));
        Vector2 origin = cue.gestureOrigin * routeHalfSpan;
        Vector2 destination = cue.gestureTarget * routeHalfSpan;
        Vector2 ghostPosition = Vector2.Lerp(origin, destination, progress);
        // Coordinates are derived from the actual plan on every frame: retries,
        // skipped frames and interrupted holds cannot advance the visual route.
        actionVisual.anchoredPosition = manual ? ghostPosition : Vector2.zero;
        targetVisual.anchoredPosition = destination;
        nextVisual.anchoredPosition = cue.nextGestureTarget * routeHalfSpan;
        targetVisual.gameObject.SetActive(dragging);
        bool preview = dragging && cue.hasNextDirection && handoff > 0f;
        nextVisual.gameObject.SetActive(preview);
        nextGroup.alpha = handoff;

        bool pressed = cue.inputPressed && !cue.recovering;
        ghostImage.sprite = pressed || !manual ? ghostWhite : ghostBlack;
        ghostImage.color = new Color(1f, 1f, 1f, cue.recovering ? 0.6f : 1f);
        bool showWord = manual && !dragging;
        actionLabel.gameObject.SetActive(showWord && !cue.usingGamepad);
        mouseImage.gameObject.SetActive(showWord && !cue.usingGamepad);
        deviceLabel.gameObject.SetActive(manual && cue.usingGamepad);
        actionLabel.sprite = cue.kind == CueKind.Tap ? (cue.english ? tapEnglish : tapChinese)
            : (cue.english ? holdEnglish : holdChinese);
        actionLabel.rectTransform.sizeDelta = new Vector2(cue.kind == CueKind.Tap && !cue.english ? 42f : 90f, 42f);
        mouseImage.sprite = pressed ? mousePressed : mouseIdle;
        PutText(deviceLabel, cue.kind == CueKind.Tap ? "A" : "RT");
        deviceLabel.rectTransform.anchoredPosition = new Vector2(-4f, dragging ? -87f : -40f);

        Vector2 direction = (destination - origin).normalized;
        directionArrow.gameObject.SetActive(dragging);
        if (dragging)
        {
            // Rotate the supplied left-pointing arrow, not the text or whole ghost.
            directionArrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 180f);
            // Keep the arrow outside the ghost silhouette while fading it.
            // Compressing it into the remaining distance hides it behind the body.
            directionArrow.rectTransform.anchoredPosition = ghostPosition + direction * (ghostWidth * 0.65f);
            directionArrow.sprite = cue.armed ? arrowBlack : arrowWhite;
            directionArrow.color = new Color(1f, 1f, 1f, Mathf.Lerp(1f, completedArrowAlpha, handoff));
        }
        // Preserve the artist's upright body, with a small directional lean only.
        ghostBody.localRotation = Quaternion.Euler(0f, 0f, dragging ? -direction.x * ghostLeanDegrees * Mathf.Sin(progress * Mathf.PI) : 0f);
        ghostBody.localScale = new Vector3(directional && direction.x > 0f ? -1f : 1f, 1f, 1f);
        float phase = 1f - Mathf.Clamp01(cue.beatsToTarget);
        bool inWindow = Mathf.Abs(cue.beatsToTarget) <= cue.tolerance;
        timingRing.gameObject.SetActive(manual && !dragging);
        timingRing.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.42f, 1f, phase);
        timingRing.color = new Color(1f, 1f, 1f, cue.recovering ? 0.3f : inWindow ? 1f : 0.78f);
        currentTargetRing.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.35f, 1f, phase);
        currentTargetRing.color = new Color(1f, 1f, 1f, inWindow ? 1f : 0.8f);

        // Keep one compact caption below the gesture, outside its target circles.
        float lowestPoint = Mathf.Min(origin.y, destination.y);
        if (cue.hasNextDirection) lowestPoint = Mathf.Min(lowestPoint, cue.nextGestureTarget.y * routeHalfSpan);
        instructionBackplate.anchoredPosition = new Vector2(-100f, manual ? lowestPoint - 135f : -120f);
        PutText(playerInstruction, manual ? cue.timing : cue.instruction);
        PutText(playerTiming, manual ? cue.exitHint : cue.timing + "  ·  " + cue.exitHint);
        playerFeedback.rectTransform.anchoredPosition = actionVisual.anchoredPosition + new Vector2(0f, ghostWidth * 0.65f);
        playerFeedback.gameObject.SetActive(Time.unscaledTime < playerFeedbackUntil);

        var parent = playerHud.parent as RectTransform;
        float width = parent == null ? 1920f : Mathf.Max(1f, parent.rect.width);
        float height = parent == null ? 1080f : Mathf.Max(1f, parent.rect.height);
        float halfWidth = Mathf.Max(routeHalfSpan + ghostWidth, 320f);
        float halfHeight = routeHalfSpan + 140f;
        float scale = Mathf.Min(playerHudScale, Mathf.Min(width / (2f * halfWidth + 24f), height / (2f * halfHeight + 24f)));
        playerHud.localScale = Vector3.one * scale;
        Vector3 projected = view == null ? new Vector3(0.4f, 0.5f, 1f) : view.WorldToViewportPoint(curtainPosition);
        Vector2 anchor = new Vector2(projected.x, projected.y) + playerHudOffset;
        anchor.x = Mathf.Clamp(anchor.x, halfWidth * scale / width, 1f - halfWidth * scale / width);
        anchor.y = Mathf.Clamp(anchor.y, halfHeight * scale / height, 1f - halfHeight * scale / height);
        playerHud.anchorMin = playerHud.anchorMax = anchor;
        playerHud.anchoredPosition = Vector2.zero;
        if (worldGrab) FollowSolvedGrab(cue, view, scale, dragging);
    }

    void FollowSolvedGrab(PlayerCue cue, Camera view, float scale, bool dragging)
    {
        // Never screen-clamp this anchor: that would detach the icon from cloth.
        Vector3 grab = view.WorldToViewportPoint(cue.curtainVisual.GrabWorldPosition);
        playerHud.anchorMin = playerHud.anchorMax = new Vector2(grab.x, grab.y);
        actionVisual.anchoredPosition = Vector2.zero;
        Vector2 origin = WorldOffset(cue.curtainVisual.GestureOriginWorld, view, grab, scale);
        Vector2 destination = WorldOffset(cue.curtainVisual.GestureTargetWorld, view, grab, scale);
        Vector2 next = WorldOffset(cue.curtainVisual.RouteWorldPosition(cue.nextGestureTarget, view), view, grab, scale);
        targetVisual.anchoredPosition = destination;
        nextVisual.anchoredPosition = next;
        Vector2 direction = (destination - origin).normalized;
        if (dragging)
        {
            directionArrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 180f);
            directionArrow.rectTransform.anchoredPosition = direction * ghostWidth * clothGhostScale * 0.65f;
        }
        playerFeedback.rectTransform.anchoredPosition = new Vector2(0f, ghostWidth * clothGhostScale * 0.65f);
        var parent = playerHud.parent as RectTransform;
        float height = parent == null ? 1080f : parent.rect.height;
        float lowest = dragging ? Mathf.Min(0f, destination.y) : 0f;
        float highest = dragging ? Mathf.Max(0f, destination.y) : 0f;
        if (nextVisual.gameObject.activeSelf)
        {
            lowest = Mathf.Min(lowest, next.y);
            highest = Mathf.Max(highest, next.y);
        }
        // Keep the caption clear of the downward target, not just the ghost.
        float captionY = lowest - 125f;
        if (grab.y * height + captionY * scale < 42f) captionY = highest + 125f;
        instructionBackplate.anchoredPosition = new Vector2(-100f, captionY);
    }

    Vector2 WorldOffset(Vector3 world, Camera view, Vector3 grab, float scale)
    {
        var parent = playerHud.parent as RectTransform;
        Vector2 size = parent == null ? new Vector2(1920f, 1080f) : parent.rect.size;
        Vector3 p = view.WorldToViewportPoint(world);
        return new Vector2((p.x - grab.x) * size.x, (p.y - grab.y) * size.y) / Mathf.Max(0.01f, scale);
    }

    public void ShowPlayerFeedback(string message, Color color, float duration)
    {
        if (playerFeedback == null) return;
        PutText(playerFeedback, message);
        playerFeedback.color = color;
        playerFeedbackUntil = Time.unscaledTime + Mathf.Max(0.05f, duration);
    }

    public void ClearPlayerFeedback()
    {
        playerFeedbackUntil = 0f;
        if (playerFeedback != null) playerFeedback.gameObject.SetActive(false);
    }

    static void PutText(Text text, string value)
    {
        value = value ?? "";
        if (text != null && text.text != value) text.text = value;
    }

    static RectTransform PlayerRect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    static Image Artwork(string name, Transform parent, Sprite sprite, Vector2 position, Vector2 size)
    {
        var image = PlayerRect(name, parent, position, size).gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        return image;
    }

    Text PlayerText(string name, Transform parent, Vector2 position, Vector2 size, int fontSize)
    {
        var text = PlayerRect(name, parent, position, size).gameObject.AddComponent<Text>();
        text.font = playerHudFont != null ? playerHudFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = Mathf.Max(13, fontSize - 3);
        text.resizeTextMaxSize = fontSize;
        text.raycastTarget = false;
        var outline = text.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.1f, 0.07f, 0.1f, 0.95f);
        outline.effectDistance = new Vector2(1f, -1f);
        return text;
    }

    public Canvas CurrentCueCanvas { get { return currentCueCanvas; } }
    public Text CurrentCueLabel { get { return currentCueLabel; } }
    public Canvas NextCueCanvas { get { return nextCueCanvas; } }
    public Text NextCueLabel { get { return nextCueLabel; } }
    public Canvas EscapeCanvas { get { return escapeCanvas; } }
    public Image EscapeFill { get { return escapeFill; } }
    public Text EscapeLabel { get { return escapeLabel; } }
    public bool IsConfigured
    {
        get
        {
            return currentCueCanvas != null && currentCueLabel != null && currentCueAnchor != null
                && beatPulse != null && nextCueCanvas != null && nextCueLabel != null
                && nextCueAnchor != null && escapeCanvas != null && escapeFill != null
                && escapeLabel != null && escapeAnchor != null;
        }
    }

    public void BindCamera(Camera view)
    {
        if (currentCueCanvas != null) currentCueCanvas.worldCamera = view;
        if (nextCueCanvas != null) nextCueCanvas.worldCamera = view;
        if (escapeCanvas != null) escapeCanvas.worldCamera = view;
    }

    public void FollowCurtain(Vector3 curtainPosition, Camera view, float closeupBlend, float pulse)
    {
        if (view == null || currentCueAnchor == null || beatPulse == null || nextCueAnchor == null) return;
        currentCueAnchor.position = curtainPosition + curtainOffset
            - view.transform.up * (closeupDownOffset * closeupBlend);
        currentCueAnchor.rotation = Quaternion.LookRotation(currentCueAnchor.position - view.transform.position, Vector3.up);
        // A dedicated parent animates the beat without overwriting the designer's Canvas scale.
        beatPulse.localScale = Vector3.one * (1f + beatPopStrength * pulse);
        nextCueAnchor.position = currentCueAnchor.position + CameraOffset(view, nextCueOffset);
        nextCueAnchor.rotation = currentCueAnchor.rotation;
    }

    public void FollowGuest(Vector3 guestPosition, Camera view)
    {
        if (view == null || escapeAnchor == null) return;
        escapeAnchor.position = guestPosition + CameraOffset(view, escapeOffset);
        escapeAnchor.rotation = Quaternion.LookRotation(escapeAnchor.position - view.transform.position, Vector3.up);
    }

    static Vector3 CameraOffset(Camera view, Vector3 offset)
    {
        return view.transform.right * offset.x + view.transform.up * offset.y + view.transform.forward * offset.z;
    }

    void OnDestroy()
    {
        if (playerHud != null) Destroy(playerHud.gameObject);

    }
}
