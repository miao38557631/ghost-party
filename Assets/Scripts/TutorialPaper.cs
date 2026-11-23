using UnityEngine;
using UnityEngine.UI;

/// <summary>Inked paper backplate shared by the opening's prompts.</summary>
public sealed class TutorialPaper : Image
{
    public static readonly Color32 Ink = new Color32(36, 27, 34, 255);
    public static readonly Color32 Cream = new Color32(255, 248, 226, 255);
    public static readonly Color32 Yellow = new Color32(255, 223, 124, 255);
    public static readonly Color32 Pink = new Color32(255, 184, 203, 255);

    static readonly Vector2[] contour =
    {
        new Vector2(.00f, .29f), new Vector2(.018f, .12f), new Vector2(.055f, .035f),
        new Vector2(.20f, .015f), new Vector2(.44f, .02f), new Vector2(.72f, .00f),
        new Vector2(.91f, .034f), new Vector2(.972f, .10f), new Vector2(1f, .25f),
        new Vector2(.995f, .72f), new Vector2(.98f, .87f), new Vector2(.93f, .96f),
        new Vector2(.74f, .974f), new Vector2(.51f, .99f), new Vector2(.25f, 1f),
        new Vector2(.078f, .968f), new Vector2(.028f, .89f), new Vector2(.006f, .73f)
    };
    static Sprite artwork;

    public void SetPaper(Color fill)
    {
        if (artwork == null) artwork = DrawPaper();
        sprite = artwork;
        type = Type.Sliced;
        color = fill;
        raycastTarget = false;
    }

    static Sprite DrawPaper()
    {
        const int scale = 2;
        const int width = 240;
        const int height = 60;
        var shadow = new Rect(5f, 2f, 233f, 54f);
        var outline = new Rect(2f, 6f, 233f, 52f);
        var face = new Rect(5f, 9f, 227f, 46f);

        var pixels = new Color32[width * height * scale * scale];
        Color32 shadowInk = new Color32(101, 56, 79, 110);
        Color32 white = new Color32(255, 255, 255, 255);
        Color32 transparent = new Color32(0, 0, 0, 0);
        int stride = width * scale;
        for (int y = 0; y < height * scale; y++)
        {
            for (int x = 0; x < stride; x++)
            {
                var point = new Vector2((x + .5f) / scale, (y + .5f) / scale);
                pixels[y * stride + x] = Inside(point, face) ? white
                    : Inside(point, outline) ? Ink
                    : Inside(point, shadow) ? shadowInk : transparent;
            }
        }

        var texture = new Texture2D(width * scale, height * scale, TextureFormat.RGBA32, false)
        {
            name = "Tutorial Paper",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        var result = Sprite.Create(texture, new Rect(0f, 0f, stride, height * scale),
            new Vector2(.5f, .5f), 100f * scale, 0, SpriteMeshType.FullRect, new Vector4(28f, 17f, 28f, 17f) * scale);
        result.hideFlags = HideFlags.HideAndDontSave;
        return result;
    }

    static bool Inside(Vector2 point, Rect bounds)
    {
        if (!bounds.Contains(point)) return false;
        Vector2 position = (point - bounds.min) / bounds.size;
        bool inside = false;
        for (int i = 0, previous = contour.Length - 1; i < contour.Length; previous = i++)
        {
            Vector2 a = contour[i], b = contour[previous];
            if ((a.y > position.y) != (b.y > position.y) &&
                position.x < (b.x - a.x) * (position.y - a.y) / (b.y - a.y) + a.x)
                inside = !inside;
        }
        return inside;
    }
}
