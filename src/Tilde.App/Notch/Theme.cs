using Tilde.Render;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Tilde.App.Notch;

/// <summary>
/// Estética «negro mate minimal»: fondo negro puro que se funde con el marco de la pantalla,
/// texto blanco con tres niveles de intensidad y un único acento cálido (ámbar).
/// Pinceles y fuentes se crean una vez por tamaño de notch.
/// </summary>
internal sealed class Theme : IDisposable
{
    public Theme(GraphicsDevice device, float sizeFactor)
    {
        F = sizeFactor;
        DWrite = device.DWrite;
        var dc = device.ResourceContext;
        Fill = dc.CreateSolidColorBrush(new Color4(0f, 0f, 0f, 1f), null);
        Primary = dc.CreateSolidColorBrush(new Color4(1f, 1f, 1f, 0.95f), null);
        Secondary = dc.CreateSolidColorBrush(new Color4(1f, 1f, 1f, 0.58f), null);
        Tertiary = dc.CreateSolidColorBrush(new Color4(1f, 1f, 1f, 0.26f), null);
        Subtle = dc.CreateSolidColorBrush(new Color4(1f, 1f, 1f, 0.10f), null);
        Accent = dc.CreateSolidColorBrush(new Color4(0.96f, 0.73f, 0.44f, 1f), null);
        Water = dc.CreateSolidColorBrush(new Color4(0.42f, 0.72f, 0.96f, 1f), null);
        Mint = dc.CreateSolidColorBrush(new Color4(0.44f, 0.83f, 0.66f, 1f), null);
        Raised = dc.CreateSolidColorBrush(new Color4(1f, 1f, 1f, 0.16f), null);
        // Colores de las tareas: ámbar, coral, menta, cielo y lila.
        TaskColors =
        [
            Accent,
            dc.CreateSolidColorBrush(new Color4(0.95f, 0.52f, 0.42f, 1f), null),
            Mint,
            Water,
            dc.CreateSolidColorBrush(new Color4(0.72f, 0.61f, 0.95f, 1f), null),
        ];

        // Segoe UI Variable (Windows 11) o Segoe UI (Windows 10).
        var text = Text.ResolveFamily(DWrite, "Segoe UI Variable Text", "Segoe UI");
        var display = Text.ResolveFamily(DWrite, "Segoe UI Variable Display", "Segoe UI");
        Clock = Text.Format(DWrite, display, 13.5f * F, FontWeight.SemiBold);
        HeaderDate = Text.Format(DWrite, text, 12.5f * F);
        HeaderTime = Text.Format(DWrite, display, 12.5f * F, FontWeight.SemiBold);
        Title = Text.Format(DWrite, display, 15f * F, FontWeight.SemiBold);
        Caption = Text.Format(DWrite, text, 12f * F);
        Small = Text.Format(DWrite, text, 11f * F);
        TrackTitle = Text.Format(DWrite, display, 14.5f * F, FontWeight.SemiBold);
        TrackArtist = Text.Format(DWrite, text, 12.5f * F);
        Lyric = Text.Format(DWrite, display, 14f * F, FontWeight.SemiBold);
        Lyric.WordWrapping = WordWrapping.Wrap;
        Countdown = Text.Format(DWrite, display, 19f * F, FontWeight.SemiBold);
        Button = Text.Format(DWrite, text, 12f * F, FontWeight.SemiBold);
        Icons = new IconSet(device.D2DFactory);
    }

    public float F { get; }

    public IDWriteFactory DWrite { get; }

    public ID2D1SolidColorBrush Fill { get; }

    public ID2D1SolidColorBrush Primary { get; }

    public ID2D1SolidColorBrush Secondary { get; }

    public ID2D1SolidColorBrush Tertiary { get; }

    public ID2D1SolidColorBrush Subtle { get; }

    public ID2D1SolidColorBrush Accent { get; }

    public ID2D1SolidColorBrush Water { get; }

    public ID2D1SolidColorBrush Mint { get; }

    /// <summary>Fondo de botones al pasar el ratón (algo más claro que <see cref="Subtle"/>).</summary>
    public ID2D1SolidColorBrush Raised { get; }

    public ID2D1SolidColorBrush[] TaskColors { get; }

    public IDWriteTextFormat Clock { get; }

    public IDWriteTextFormat HeaderDate { get; }

    public IDWriteTextFormat HeaderTime { get; }

    public IDWriteTextFormat Title { get; }

    public IDWriteTextFormat Caption { get; }

    public IDWriteTextFormat Small { get; }

    public IDWriteTextFormat TrackTitle { get; }

    public IDWriteTextFormat TrackArtist { get; }

    /// <summary>Líneas de letra (con ajuste de línea).</summary>
    public IDWriteTextFormat Lyric { get; }

    /// <summary>Cuenta atrás grande del Pomodoro.</summary>
    public IDWriteTextFormat Countdown { get; }

    /// <summary>Texto de los botones.</summary>
    public IDWriteTextFormat Button { get; }

    public IconSet Icons { get; }

    public void Dispose()
    {
        Fill.Dispose();
        Primary.Dispose();
        Secondary.Dispose();
        Tertiary.Dispose();
        Subtle.Dispose();
        Accent.Dispose();
        Water.Dispose();
        Mint.Dispose();
        Raised.Dispose();
        TaskColors[1].Dispose();
        TaskColors[4].Dispose();
        Clock.Dispose();
        HeaderDate.Dispose();
        HeaderTime.Dispose();
        Title.Dispose();
        Caption.Dispose();
        Small.Dispose();
        TrackTitle.Dispose();
        TrackArtist.Dispose();
        Lyric.Dispose();
        Countdown.Dispose();
        Button.Dispose();
        Icons.Dispose();
    }
}
