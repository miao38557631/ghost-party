using UnityEngine;
using UnityEngine.UI;

/// <summary>Bottom dialogue plus small illustrated reactions in the tutorial.</summary>
internal sealed class TutorialHud
{
    readonly RectTransform root;
    readonly TutorialPaper paper;
    readonly Text caption, reaction;
    readonly RectTransform sleep;
    readonly Image fade;

    public TutorialHud(Transform owner)
    {
        var canvasObject = new GameObject("Tutorial HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root = (RectTransform)canvasObject.transform;
        root.SetParent(owner, false);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 25;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;

        paper = new GameObject("Tutorial cue", typeof(RectTransform), typeof(TutorialPaper)).GetComponent<TutorialPaper>();
        paper.rectTransform.SetParent(root, false);
        paper.rectTransform.anchorMin = paper.rectTransform.anchorMax = new Vector2(.5f, 0f);
        paper.rectTransform.anchoredPosition = new Vector2(0f, 86f);
        paper.rectTransform.sizeDelta = new Vector2(565f, 62f);
        paper.SetPaper(TutorialPaper.Cream);
        caption = CreateText(paper.rectTransform, "Caption", 27, TutorialPaper.Ink);
        caption.rectTransform.anchorMin = Vector2.zero;
        caption.rectTransform.anchorMax = Vector2.one;
        caption.rectTransform.offsetMin = new Vector2(25f, 8f);
        caption.rectTransform.offsetMax = new Vector2(-25f, -5f);
        caption.resizeTextForBestFit = true;
        caption.resizeTextMinSize = 20;
        caption.resizeTextMaxSize = 27;
        caption.horizontalOverflow = HorizontalWrapMode.Wrap;
        paper.gameObject.SetActive(false);

        sleep = new GameObject("Sleep marks", typeof(RectTransform)).GetComponent<RectTransform>();
        sleep.SetParent(root, false);
        sleep.sizeDelta = new Vector2(100f, 56f);
        for (int i = 0; i < 3; i++)
        {
            var mark = CreateText(sleep, "Z", 19 + i * 7, TutorialPaper.Yellow);
            mark.text = "Z";
            mark.rectTransform.sizeDelta = new Vector2(36f, 46f);
            mark.rectTransform.anchoredPosition = new Vector2(-28f + i * 28f, i * 9f);
            mark.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 13f - i * 11f);
            AddInkOutline(mark);
        }
        sleep.gameObject.SetActive(false);

        reaction = CreateText(root, "Reaction marks", 42, TutorialPaper.Yellow);
        reaction.rectTransform.sizeDelta = new Vector2(112f, 64f);
        reaction.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -9f);
        AddInkOutline(reaction);
        reaction.enabled = false;

        fade = new GameObject("Scene transition", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        fade.rectTransform.SetParent(root, false);
        fade.rectTransform.anchorMin = Vector2.zero;
        fade.rectTransform.anchorMax = Vector2.one;
        fade.rectTransform.offsetMin = fade.rectTransform.offsetMax = Vector2.zero;
        fade.raycastTarget = false;
        SetFade(0f);
    }

    public void ShowCaption(string line, Color32 tint)
    {
        paper.gameObject.SetActive(true);
        paper.color = tint;
        if (caption.text != line) caption.text = line;
        paper.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Min(565f, root.rect.width - 42f));
    }

    public void HideCaption() { paper.gameObject.SetActive(false); }

    public void ShowSleep(Camera camera, Vector3 anchor, float elapsed)
    {
        bool visible = Project(camera, anchor, out Vector2 point);
        sleep.gameObject.SetActive(visible);
        if (visible)
            sleep.anchoredPosition = KeepOnScreen(point + new Vector2(40f, 16f + Mathf.Sin(elapsed * 3f) * 5f), sleep.sizeDelta);
    }

    public void HideSleep() { sleep.gameObject.SetActive(false); }

    public void ShowReaction(Camera camera, Vector3 anchor, string marks, Color tint)
    {
        reaction.enabled = Project(camera, anchor, out Vector2 point);
        if (!reaction.enabled) return;
        if (reaction.text != marks) reaction.text = marks;
        reaction.color = tint;
        reaction.rectTransform.anchoredPosition = KeepOnScreen(point, reaction.rectTransform.sizeDelta);
    }

    public void HideReaction() { reaction.enabled = false; }

    public void SetFade(float opacity)
    {
        fade.enabled = opacity > 0f;
        fade.color = new Color(.13f, .06f, .13f, opacity);
    }

    bool Project(Camera camera, Vector3 anchor, out Vector2 point)
    {
        Vector3 screen = camera.WorldToScreenPoint(anchor);
        point = Vector2.zero;
        return screen.z > 0f && RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out point);
    }

    Vector2 KeepOnScreen(Vector2 point, Vector2 size)
    {
        Vector2 half = size * .5f;
        Rect bounds = root.rect;
        return new Vector2(
            Mathf.Clamp(point.x, bounds.xMin + half.x + 18f, bounds.xMax - half.x - 18f),
            Mathf.Clamp(point.y, bounds.yMin + half.y + 18f, bounds.yMax - half.y - 18f));
    }

    static void AddInkOutline(Text mark)
    {
        var outline = mark.gameObject.AddComponent<Outline>();
        outline.effectColor = TutorialPaper.Ink;
        outline.effectDistance = new Vector2(1.8f, -1.8f);
    }

    static Text CreateText(RectTransform parent, string name, int size, Color32 color)
    {
        var text = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        text.rectTransform.SetParent(parent, false);
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }
}
