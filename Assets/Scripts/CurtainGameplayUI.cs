using UnityEngine;
using UnityEngine.UI;

/// <summary>Gameplay moves anchors, leaving the authored Canvas transforms and graphic styles editable.</summary>
[DisallowMultipleComponent]
public sealed class CurtainGameplayUI : MonoBehaviour
{
    // The gameplay passes a snapshot from its existing audio clock and input
    // judgement. This component never reads input or awards a successful hit.
    public enum CueKind { Tap, Direction, Hold, Automatic, Result }

    public struct PlayerCue
    {
        public CueKind kind;
        public string instruction, timing, icon, nextIcon, nextLabel;
        public float beatsToTarget, tolerance, motionProgress;
        public bool held, armed, recovering, motionReady;
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

    [Header("Player HUD (screen space)")]
    [Tooltip("Offset from the curtain's viewport position. This is presentation only.")]
    [SerializeField] Vector2 playerHudOffset = new Vector2(-0.13f, -0.16f);
    [SerializeField, Range(0.65f, 1.35f)] float playerHudScale = 1f;
    [SerializeField] Font playerHudFont;
    [SerializeField] Color cuePaper = new Color(0.98f, 0.97f, 0.94f);
    [SerializeField] Color cueInk = new Color(0.12f, 0.13f, 0.17f);
    [SerializeField] Color timingColor = new Color(1f, 0.84f, 0.34f);

    RectTransform playerHud;
    RectTransform actionVisual;
    Image actionDisk, timingRing, motionTrack, motionFill;
    Text playerIcon, playerInstruction, playerTiming, playerFeedback;
    Image instructionBackplate;
    RectTransform nextVisual;
    Text playerNextIcon, playerNextLabel;
    float playerFeedbackUntil;
    Sprite runtimeDisk, runtimeRing, runtimeSolid;
    Texture2D runtimeDiskTexture, runtimeRingTexture;

    public void AttachPlayerHud(Canvas host)
    {
        if (playerHud != null || host == null) return;
        var root = new GameObject("CurtainPlayerHUD", typeof(RectTransform), typeof(CanvasGroup));
        root.transform.SetParent(host.transform, false);
        playerHud = root.GetComponent<RectTransform>();
        playerHud.sizeDelta = new Vector2(460f, 240f);
        var group = root.GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        runtimeDisk = MakeMarkerSprite(false, out runtimeDiskTexture);
        runtimeRing = MakeMarkerSprite(true, out runtimeRingTexture);
        runtimeSolid = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));

        instructionBackplate = PlayerImage("InstructionBackplate", playerHud, new Vector2(0f, -55f),
            new Vector2(450f, 86f), new Color(0.07f, 0.08f, 0.12f, 0.86f));
        actionVisual = PlayerRect("CurrentAction", playerHud, new Vector2(0f, 37f), new Vector2(80f, 80f));
        timingRing = PlayerImage("ApproachingBeat", actionVisual, Vector2.zero, new Vector2(94f, 94f), timingColor);
        timingRing.sprite = runtimeRing;
        actionDisk = PlayerImage("ActionDisk", actionVisual, Vector2.zero, new Vector2(80f, 80f), cuePaper);
        actionDisk.sprite = runtimeDisk;
        playerIcon = PlayerText("ActionIcon", actionVisual, Vector2.zero, new Vector2(72f, 64f), 36, cueInk);
        motionTrack = PlayerImage("GestureProgressTrack", playerHud, new Vector2(0f, -16f),
            new Vector2(136f, 5f), new Color(1f, 1f, 1f, 0.22f));
        motionFill = PlayerImage("GestureProgress", motionTrack.rectTransform, Vector2.zero,
            new Vector2(136f, 5f), timingColor);
        motionFill.sprite = runtimeSolid;
        motionFill.type = Image.Type.Filled;
        motionFill.fillMethod = Image.FillMethod.Horizontal;
        motionFill.fillOrigin = 0;

        playerInstruction = PlayerText("Instruction", playerHud, new Vector2(0f, -39f), new Vector2(426f, 40f), 26, Color.white);
        playerTiming = PlayerText("Timing", playerHud, new Vector2(0f, -74f), new Vector2(426f, 32f), 19,
            new Color(0.90f, 0.92f, 0.97f));
        playerFeedback = PlayerText("Feedback", playerHud, new Vector2(0f, 133f), new Vector2(430f, 38f), 24, Color.white);
        nextVisual = PlayerRect("NextAction", playerHud, new Vector2(-91f, 15f), new Vector2(62f, 82f));
        var nextGroup = nextVisual.gameObject.AddComponent<CanvasGroup>();
        nextGroup.alpha = 0.7f;
        var nextDisk = PlayerImage("Disk", nextVisual, Vector2.zero, new Vector2(42f, 42f), cuePaper);
        nextDisk.sprite = runtimeDisk;
        playerNextIcon = PlayerText("Icon", nextVisual, Vector2.zero, new Vector2(42f, 42f), 23, cueInk);
        playerNextLabel = PlayerText("Label", nextVisual, new Vector2(0f, -35f), new Vector2(86f, 24f), 15, Color.white);
        playerHud.gameObject.SetActive(false);
    }

    public void PresentPlayerCue(bool visible, PlayerCue cue, Camera view, Vector3 curtainPosition)
    {
        if (playerHud == null) return;
        if (playerHud.gameObject.activeSelf != visible) playerHud.gameObject.SetActive(visible);
        if (!visible) return;

        var anchor = new Vector2(0.28f, 0.43f);
        if (view != null)
        {
            Vector3 projected = view.WorldToViewportPoint(curtainPosition);
            if (projected.z > 0f) anchor = new Vector2(projected.x, projected.y) + playerHudOffset;
        }
        // Clamp using the actual scaled Canvas rectangle, including small windows.
        var parent = playerHud.parent as RectTransform;
        float width = parent == null ? 1920f : Mathf.Max(1f, parent.rect.width);
        float height = parent == null ? 1080f : Mathf.Max(1f, parent.rect.height);
        float scale = Mathf.Min(playerHudScale, Mathf.Min(width / 500f, height / 300f));
        playerHud.localScale = Vector3.one * scale;
        float marginX = Mathf.Min(0.49f, 242f * scale / width);
        float marginY = Mathf.Min(0.49f, 162f * scale / height);
        anchor.x = Mathf.Clamp(anchor.x, marginX, 1f - marginX);
        anchor.y = Mathf.Clamp(anchor.y, marginY, 1f - marginY);
        playerHud.anchorMin = playerHud.anchorMax = anchor;
        playerHud.anchoredPosition = Vector2.zero;

        bool manual = cue.kind == CueKind.Tap || cue.kind == CueKind.Direction || cue.kind == CueKind.Hold;
        actionVisual.gameObject.SetActive(manual);
        bool hasNext = manual && !string.IsNullOrEmpty(cue.nextIcon);
        nextVisual.gameObject.SetActive(hasNext);
        if (hasNext)
        {
            PutText(playerNextIcon, cue.nextIcon);
            PutText(playerNextLabel, cue.nextLabel);
        }
        PutText(playerInstruction, cue.instruction);
        PutText(playerTiming, cue.timing);
        PutText(playerIcon, cue.icon);
        playerInstruction.rectTransform.anchoredPosition = new Vector2(0f, manual ? -39f : 14f);
        playerTiming.rectTransform.anchoredPosition = new Vector2(0f, manual ? -74f : -21f);
        instructionBackplate.rectTransform.anchoredPosition = new Vector2(0f, manual ? -55f : -3f);
        playerFeedback.gameObject.SetActive(Time.unscaledTime < playerFeedbackUntil);

        if (!manual)
        {
            motionTrack.gameObject.SetActive(false);
            return;
        }
        float phase = 1f - Mathf.Clamp01(cue.beatsToTarget);
        bool inWindow = Mathf.Abs(cue.beatsToTarget) <= cue.tolerance;
        bool directionReady = cue.kind != CueKind.Direction || cue.armed || cue.motionReady;
        bool lit = inWindow && directionReady && !cue.recovering;
        actionDisk.color = cue.recovering ? new Color(0.70f, 0.72f, 0.77f)
            : cue.armed ? new Color(0.77f, 0.94f, 0.81f)
            : lit ? timingColor : cue.held ? new Color(0.86f, 0.89f, 0.94f) : cuePaper;
        playerIcon.color = cueInk;
        timingRing.color = cue.recovering ? new Color(1f, 1f, 1f, 0.25f)
            : (lit ? timingColor : new Color(1f, 1f, 1f, 0.72f));
        timingRing.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.65f, 1f, phase);
        bool showMotion = cue.kind == CueKind.Direction && cue.held && !cue.recovering;
        motionTrack.gameObject.SetActive(showMotion);
        if (showMotion) motionFill.fillAmount = Mathf.Clamp01(cue.motionProgress);
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

    RectTransform PlayerRect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    Image PlayerImage(string name, Transform parent, Vector2 position, Vector2 size, Color color)
    {
        var rect = PlayerRect(name, parent, position, size);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    Text PlayerText(string name, Transform parent, Vector2 position, Vector2 size, int fontSize, Color color)
    {
        var rect = PlayerRect(name, parent, position, size);
        var text = rect.gameObject.AddComponent<Text>();
        text.font = playerHudFont != null ? playerHudFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = Mathf.Max(13, fontSize - 5);
        text.resizeTextMaxSize = fontSize;
        text.raycastTarget = false;
        return text;
    }

    static Sprite MakeMarkerSprite(bool ring, out Texture2D texture)
    {
        const int size = 128;
        texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.name = ring ? "CurtainHUD_Ring" : "CurtainHUD_Disk";
        var pixels = new Color[size * size];
        float center = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float dx = x - center, dy = y - center;
            float distance = Mathf.Sqrt(dx * dx + dy * dy);
            float edge = ring ? 3f - Mathf.Abs(distance - (center - 4f)) : center - 1f - distance;
            pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(edge / 1.5f));
        }
        texture.SetPixels(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
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
        if (runtimeDisk != null) Destroy(runtimeDisk);
        if (runtimeRing != null) Destroy(runtimeRing);
        if (runtimeSolid != null) Destroy(runtimeSolid);
        if (runtimeDiskTexture != null) Destroy(runtimeDiskTexture);
        if (runtimeRingTexture != null) Destroy(runtimeRingTexture);
    }
}
