using Tilde.App.Music;
using Tilde.App.Productivity;
using Tilde.Core;
using Tilde.Core.Settings;
using Tilde.Render;

namespace Tilde.App.Pages;

/// <summary>Las páginas del notch, en orden.</summary>
internal static class PageCatalog
{
    public static IReadOnlyList<Page> Create(MusicController music, SettingsStore settings,
        PomodoroService pomodoro, WaterService water, TasksService tasks) =>
    [
        new MusicPage(music, settings),
        new PomodoroPage(pomodoro),
        new WaterPage(water),
        new TasksPage(tasks),
        new PlaceholderPage(Strings.PageShelf, IconKind.Shelf, 4),
        new PlaceholderPage(Strings.PageClaude, IconKind.Spark, 6),
    ];
}
