using Tilde.App.Music;
using Tilde.Core;
using Tilde.Core.Settings;
using Tilde.Render;

namespace Tilde.App.Pages;

/// <summary>Las páginas del notch, en orden.</summary>
internal static class PageCatalog
{
    public static IReadOnlyList<Page> Create(MusicController music, SettingsStore settings) =>
    [
        new MusicPage(music, settings),
        new PlaceholderPage(Strings.PagePomodoro, IconKind.Timer, 3),
        new PlaceholderPage(Strings.PageWater, IconKind.Water, 3),
        new PlaceholderPage(Strings.PageTasks, IconKind.Tasks, 3),
        new PlaceholderPage(Strings.PageShelf, IconKind.Shelf, 4),
        new PlaceholderPage(Strings.PageClaude, IconKind.Spark, 6),
    ];
}
