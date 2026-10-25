using Tilde.Core.Settings;

namespace Tilde.Core.Layout;

/// <summary>
/// Medidas base del notch en DIP (píxeles independientes de la escala, 1 DIP = 1 px al 100 %).
/// Todo se multiplica por el factor de tamaño elegido en Ajustes.
/// </summary>
public static class NotchMetrics
{
    public const float ExpandedWidth = 500f;
    public const float ExpandedHeight = 168f;
    public const float ExpandedRadius = 22f;

    public const float CollapsedHeight = 30f;
    public const float CollapsedMinWidth = 92f;
    public const float CollapsedPadding = 16f;

    public const float MinimalWidth = 128f;
    public const float MinimalHeight = 4f;

    /// <summary>
    /// Margen transparente alrededor del notch abierto para que el pequeño rebote de la
    /// animación de apertura no se recorte contra el borde de la ventana.
    /// </summary>
    public const float OvershootMargin = 10f;

    /// <summary>Factor de escala para cada tamaño. «Auto» usa 1,2 en monitores anchos.</summary>
    public static float SizeFactor(NotchSize size, float monitorWidthDip, float monitorHeightDip) => size switch
    {
        NotchSize.Small => 0.85f,
        NotchSize.Medium => 1f,
        NotchSize.Large => 1.2f,
        NotchSize.ExtraLarge => 1.4f,
        _ => IsWideMonitor(monitorWidthDip, monitorHeightDip) ? 1.2f : 1f,
    };

    /// <summary>
    /// Monitor «ancho»: al menos 2560 DIP de ancho (p. ej. 4K al 150 %) o ultrapanorámico (≥ 21:10).
    /// </summary>
    public static bool IsWideMonitor(float widthDip, float heightDip) =>
        widthDip >= 2560f || (heightDip > 0 && widthDip / heightDip >= 2.1f);
}
