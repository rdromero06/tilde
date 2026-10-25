using System.Numerics;
using Tilde.App.Notch;
using Tilde.Render;
using Vortice.Direct2D1;
using Vortice.DirectWrite;

namespace Tilde.App.Pages;

/// <summary>Piezas de interfaz reutilizables (botones, iconos clicables, chips…).</summary>
internal static class Ui
{
    public static RoundedRectangle Rounded(RectF r, float radius) =>
        new(new Vortice.RawRectF(r.X, r.Y, r.Right, r.Bottom), radius, radius);

    /// <summary>Ancho que necesita un botón de texto (con o sin icono).</summary>
    public static float ButtonWidth(Theme theme, string text, bool icon)
    {
        float f = theme.F;
        return Text.Measure(theme.DWrite, text, theme.Button) + 24f * f + (icon ? 18f * f : 0);
    }

    /// <summary>
    /// Botón «píldora» con texto. El principal va en ámbar con texto negro; el secundario en gris
    /// translúcido. Al pasar el ratón se aclara.
    /// </summary>
    public static void Button(ID2D1DeviceContext dc, Theme theme, RectF r, string text, bool primary, bool hover,
        IconKind? icon = null, bool enabled = true)
    {
        float f = theme.F;
        var background = primary ? theme.Accent : hover ? theme.Raised : theme.Subtle;
        var foreground = primary ? (ID2D1Brush)theme.Fill : enabled ? theme.Primary : theme.Tertiary;
        dc.FillRoundedRectangle(Rounded(r, r.H / 2), background);
        if (primary && hover)
        {
            dc.FillRoundedRectangle(Rounded(r, r.H / 2), theme.Subtle);
        }
        float textWidth = Text.Measure(theme.DWrite, text, theme.Button);
        float iconSize = 14f * f;
        float content = textWidth + (icon is null ? 0 : iconSize + 4f * f);
        float x = r.X + (r.W - content) / 2;
        if (icon is { } kind)
        {
            theme.Icons.Draw(dc, kind, x, r.Y + (r.H - iconSize) / 2, iconSize, foreground);
            x += iconSize + 4f * f;
        }
        Text.Draw(dc, theme.DWrite, text, theme.Button, new RectF(x, r.Y, textWidth + 2, r.H), foreground);
    }

    /// <summary>Botón redondo con solo un icono.</summary>
    public static void IconButton(ID2D1DeviceContext dc, Theme theme, RectF r, IconKind icon, bool hover,
        float iconScale = 0.6f, ID2D1Brush? brush = null)
    {
        float d = MathF.Min(r.W, r.H);
        if (hover)
        {
            dc.FillEllipse(new Ellipse(new Vector2(r.X + r.W / 2, r.Y + r.H / 2), d / 2, d / 2), theme.Subtle);
        }
        float size = d * iconScale;
        theme.Icons.Draw(dc, icon, r.X + (r.W - size) / 2, r.Y + (r.H - size) / 2, size, brush ?? theme.Primary);
    }

    /// <summary>Texto de una línea alineado a la izquierda (atajo).</summary>
    public static void Label(ID2D1DeviceContext dc, Theme theme, string text, IDWriteTextFormat format, RectF r, ID2D1Brush brush,
        TextAlignment align = TextAlignment.Leading) => Text.Draw(dc, theme.DWrite, text, format, r, brush, align);
}

/// <summary>
/// Pequeño gestor de «zonas clicables» de una página: cada dibujo registra sus botones y el ratón
/// se resuelve contra lo último que se dibujó, así dibujo y clics nunca se desincronizan.
/// </summary>
internal sealed class HitZones<T> where T : notnull
{
    private readonly List<(RectF Rect, T Id)> _zones = [];

    public void Clear() => _zones.Clear();

    public RectF Add(RectF rect, T id)
    {
        _zones.Add((rect, id));
        return rect;
    }

    public T? Find(float x, float y)
    {
        for (int i = _zones.Count - 1; i >= 0; i--)
        {
            if (_zones[i].Rect.Contains(x, y))
            {
                return _zones[i].Id;
            }
        }
        return default;
    }

    public bool Any(float x, float y) => _zones.Exists(z => z.Rect.Contains(x, y));

    public RectF? RectOf(T id)
    {
        foreach (var (rect, zoneId) in _zones)
        {
            if (EqualityComparer<T>.Default.Equals(zoneId, id))
            {
                return rect;
            }
        }
        return null;
    }
}
