using Tilde.App.Notch;
using Tilde.Core;
using Tilde.Render;
using Vortice.Direct2D1;
using Vortice.DirectWrite;

namespace Tilde.App.Pages;

/// <summary>
/// Una página del notch (Música, Pomodoro…). Cada página dibuja su contenido en su propia
/// capa, y solo se redibuja cuando llama a <see cref="Invalidate"/>.
/// </summary>
internal abstract class Page
{
    public abstract string Title { get; }

    public abstract IconKind Icon { get; }

    /// <summary>Se lanza cuando el contenido ha cambiado y hay que redibujar.</summary>
    public event Action? Invalidated;

    /// <summary>Dibuja la página en un área de <paramref name="width"/>×<paramref name="height"/> DIP.</summary>
    public abstract void Draw(ID2D1DeviceContext dc, Theme theme, float width, float height);

    protected void Invalidate() => Invalidated?.Invoke();
}

/// <summary>Página reservada para una fase posterior: muestra su icono, nombre y fase.</summary>
internal sealed class PlaceholderPage(string title, IconKind icon, int phase) : Page
{
    public override string Title => title;

    public override IconKind Icon => icon;

    public override void Draw(ID2D1DeviceContext dc, Theme theme, float width, float height)
    {
        float f = theme.F;
        float iconSize = 26f * f;
        float titleHeight = 21f * f;
        float captionHeight = 17f * f;
        float gap = 8f * f;
        float y = (height - (iconSize + gap + titleHeight + captionHeight)) / 2;

        theme.Icons.Draw(dc, icon, (width - iconSize) / 2, y, iconSize, theme.Accent);
        y += iconSize + gap;
        Text.Draw(dc, theme.DWrite, title, theme.Title, new RectF(0, y, width, titleHeight), theme.Primary, TextAlignment.Center);
        y += titleHeight;
        Text.Draw(dc, theme.DWrite, Strings.ComingInPhase(phase), theme.Caption, new RectF(0, y, width, captionHeight),
            theme.Secondary, TextAlignment.Center);
    }
}

/// <summary>Las páginas del notch, en orden.</summary>
internal static class PageCatalog
{
    public static IReadOnlyList<Page> Create() =>
    [
        new PlaceholderPage(Strings.PageMusic, IconKind.Music, 2),
        new PlaceholderPage(Strings.PagePomodoro, IconKind.Timer, 3),
        new PlaceholderPage(Strings.PageWater, IconKind.Water, 3),
        new PlaceholderPage(Strings.PageTasks, IconKind.Tasks, 3),
        new PlaceholderPage(Strings.PageShelf, IconKind.Shelf, 4),
        new PlaceholderPage(Strings.PageClaude, IconKind.Spark, 6),
    ];
}
