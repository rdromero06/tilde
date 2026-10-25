using System.Numerics;
using Vortice.Direct2D1;
using Vortice.DirectWrite;

namespace Tilde.Render;

/// <summary>Rectángulo en DIP (x, y, ancho, alto).</summary>
public readonly record struct RectF(float X, float Y, float W, float H)
{
    public float Right => X + W;

    public float Bottom => Y + H;

    public bool Contains(float px, float py) => px >= X && px < Right && py >= Y && py < Bottom;

    public RectF Inflate(float dx, float dy) => new(X - dx, Y - dy, W + 2 * dx, H + 2 * dy);
}

/// <summary>Ayudas para elegir fuentes y dibujar o medir texto con DirectWrite.</summary>
public static class Text
{
    /// <summary>Devuelve la primera familia instalada de la lista (Segoe UI Variable solo existe en Windows 11).</summary>
    public static string ResolveFamily(IDWriteFactory dw, params string[] candidates)
    {
        using var fonts = dw.GetSystemFontCollection(false);
        foreach (var name in candidates)
        {
            if (fonts.FindFamilyName(name, out _))
            {
                return name;
            }
        }
        return "Segoe UI";
    }

    public static IDWriteTextFormat Format(IDWriteFactory dw, string family, float size, FontWeight weight = FontWeight.Normal)
    {
        var format = dw.CreateTextFormat(family, weight, FontStyle.Normal, FontStretch.Normal, size);
        format.WordWrapping = WordWrapping.NoWrap;
        return format;
    }

    /// <summary>Ancho del texto en DIP.</summary>
    public static float Measure(IDWriteFactory dw, string text, IDWriteTextFormat format)
    {
        using var layout = dw.CreateTextLayout(text, format, 10_000f, 1_000f);
        return layout.Metrics.WidthIncludingTrailingWhitespace;
    }

    /// <summary>Dibuja texto en una línea dentro de un rectángulo, con «…» si no cabe.</summary>
    public static void Draw(ID2D1DeviceContext dc, IDWriteFactory dw, string text, IDWriteTextFormat format, RectF rect,
        ID2D1Brush brush, TextAlignment align = TextAlignment.Leading, ParagraphAlignment vertical = ParagraphAlignment.Center)
    {
        using var layout = dw.CreateTextLayout(text, format, Math.Max(1, rect.W), Math.Max(1, rect.H));
        layout.TextAlignment = align;
        layout.ParagraphAlignment = vertical;
        using var ellipsis = dw.CreateEllipsisTrimmingSign(layout);
        layout.SetTrimming(new Trimming { Granularity = TrimmingGranularity.Character }, ellipsis);
        dc.DrawTextLayout(new Vector2(rect.X, rect.Y), layout, brush, DrawTextOptions.None);
    }
}
