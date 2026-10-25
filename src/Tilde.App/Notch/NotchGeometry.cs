using Tilde.Core.Layout;
using Tilde.Render;

namespace Tilde.App.Notch;

/// <summary>
/// Geometría del notch para una escala de pantalla y un tamaño concretos.
///
/// La ventana tiene siempre el tamaño del notch abierto (más un margen para el rebote) y
/// está pegada al borde superior del monitor. Las zonas transparentes no bloquean clics:
/// la «región» de la ventana se ajusta a la píldora cuando está cerrada.
///
/// Unidades: los campos *Dip están en DIP; las «zonas» (Header, Content, Bar) están en DIP
/// relativos a la esquina superior izquierda del notch abierto.
/// </summary>
internal sealed class NotchGeometry(float scale, float sizeFactor, bool minimal)
{
    /// <summary>Escala de pantalla (1 = 100 %).</summary>
    public float Scale { get; } = scale;

    /// <summary>Factor de tamaño del notch elegido en Ajustes.</summary>
    public float F { get; } = sizeFactor;

    public bool Minimal { get; } = minimal;

    public float MarginDip => NotchMetrics.OvershootMargin * F;

    public float ExpandedWidthDip => NotchMetrics.ExpandedWidth * F;

    public float ExpandedHeightDip => NotchMetrics.ExpandedHeight * F;

    public float ExpandedRadiusDip => NotchMetrics.ExpandedRadius * F;

    public float WindowWidthDip => ExpandedWidthDip + 2 * MarginDip;

    public float WindowHeightDip => ExpandedHeightDip + MarginDip;

    public int WindowPixelWidth => (int)MathF.Ceiling(WindowWidthDip * Scale);

    public int WindowPixelHeight => (int)MathF.Ceiling(WindowHeightDip * Scale);

    /// <summary>Centro horizontal de la ventana en DIP.</summary>
    public float CenterDip => WindowWidthDip / 2;

    /// <summary>Alto de la píldora con reloj (aunque el modo mínimo esté activo).</summary>
    public float ClockPillHeightDip => NotchMetrics.CollapsedHeight * F;

    public float CollapsedHeightDip => Minimal ? NotchMetrics.MinimalHeight * F : ClockPillHeightDip;

    public float CollapsedRadiusDip => CollapsedHeightDip / 2;

    /// <summary>Ancho de la píldora cerrada para un texto de reloj del ancho dado.</summary>
    public float CollapsedWidthDip(float clockTextWidth) => Minimal
        ? NotchMetrics.MinimalWidth * F
        : MathF.Max(NotchMetrics.CollapsedMinWidth * F, clockTextWidth + 2 * NotchMetrics.CollapsedPadding * F);

    public float Px(float dip) => MathF.Round(dip * Scale);

    // ── Zonas del notch abierto ──

    public float PadDip => 18f * F;

    public RectF Header => new(PadDip, 9f * F, ExpandedWidthDip - 2 * PadDip, 24f * F);

    public RectF Content => new(PadDip, 38f * F, ExpandedWidthDip - 2 * PadDip, ExpandedHeightDip - 38f * F - 36f * F);

    public RectF Bar => new(PadDip, ExpandedHeightDip - 33f * F, ExpandedWidthDip - 2 * PadDip, 28f * F);

    /// <summary>Icono de una página en la barra (relativo a <see cref="Bar"/>).</summary>
    public RectF BarIcon(int index, int count)
    {
        float size = 18f * F;
        float step = 34f * F;
        float x0 = (Bar.W - ((count - 1) * step + size)) / 2;
        return new RectF(x0 + index * step, (Bar.H - size) / 2, size, size);
    }

    /// <summary>Botón de Ajustes en la barra (relativo a <see cref="Bar"/>).</summary>
    public RectF BarSettings
    {
        get
        {
            float size = 16f * F;
            return new RectF(Bar.W - size, (Bar.H - size) / 2, size, size);
        }
    }
}
