namespace Tilde.Core;

/// <summary>
/// Textos de la interfaz en español e inglés. El idioma se elige al arrancar según el
/// idioma de visualización de Windows (ver Tilde.Platform.Language).
/// </summary>
public static class Strings
{
    public static bool Spanish { get; set; } = true;

    private static string T(string es, string en) => Spanish ? es : en;

    // Páginas
    public static string PageMusic => T("Música", "Music");
    public static string PagePomodoro => "Pomodoro";
    public static string PageWater => T("Agua", "Water");
    public static string PageTasks => T("Tareas", "Tasks");
    public static string PageShelf => T("Estante", "Shelf");
    public static string PageClaude => "Claude";

    public static string ComingInPhase(int phase) => T($"Llega en la fase {phase}", $"Coming in phase {phase}");

    // Música
    public static string NothingPlaying => T("Nada sonando", "Nothing playing");
    public static string NothingPlayingHint => T("Reproduce algo en cualquier app", "Play something in any app");
    public static string UnknownArtist => T("Artista desconocido", "Unknown artist");
    public static string LyricsLoading => T("Buscando la letra…", "Looking for lyrics…");
    public static string LyricsNotFound => T("No hay letra para esta canción", "No lyrics for this song");
    public static string LyricsPlainOnly => T("Solo hay letra sin sincronizar", "Only unsynced lyrics available");
    public static string LyricsInstrumental => T("♪  Instrumental", "♪  Instrumental");
    public static string LyricsUnavailable => T("Sin conexión con LRCLIB", "Can't reach LRCLIB");
    public static string LyricsDisabled => T("Búsqueda de letras desactivada", "Lyrics lookup is off");

    // Pomodoro
    public static string PomodoroFocus => T("Foco", "Focus");
    public static string PomodoroShortBreak => T("Descanso corto", "Short break");
    public static string PomodoroLongBreak => T("Descanso largo", "Long break");
    public static string PomodoroStart => T("Empezar", "Start");
    public static string PomodoroPause => T("Pausar", "Pause");
    public static string PomodoroResume => T("Reanudar", "Resume");
    public static string PomodoroSession(int n, int total) => T($"Sesión {n} de {total}", $"Session {n} of {total}");
    public static string PomodoroToday(int n) => T(n == 1 ? "1 hoy" : $"{n} hoy", $"{n} today");
    public static string PomodoroFocusDone => T("¡Sesión completada!", "Session complete!");
    public static string PomodoroBreakRunning(int min) => T($"Descanso de {min} min en marcha", $"{min}-minute break started");
    public static string PomodoroBreakReady(int min) => T($"Toca un descanso de {min} min", $"Time for a {min}-minute break");
    public static string PomodoroBreakOver => T("Fin del descanso", "Break's over");
    public static string PomodoroAnother(int min) => T($"¿Otra sesión de {min} min?", $"Another {min}-minute session?");
    public static string PomodoroFocusRunning(int min) => T($"Sesión de {min} min en marcha", $"{min}-minute session started");
    public static string PomodoroSkipBreak => T("Saltar descanso", "Skip break");
    public static string PomodoroStartBreak => T("Empezar descanso", "Start break");
    public static string Minutes(int n) => $"{n} min";

    // Agua
    public static string WaterOf(string goal, int percent) => T($"de {goal} · {percent} %", $"of {goal} · {percent}%");
    public static string WaterDrink(string amount) => T($"Beber {amount}", $"Drink {amount}");
    public static string WaterGlass(string amount) => T($"Vaso {amount}", $"Glass {amount}");
    public static string WaterGoal(string amount) => T($"Meta {amount}", $"Goal {amount}");
    public static string WaterToday => T("Hoy", "Today");
    public static string WaterNext(string time) => T($"Aviso {time}", $"Reminder {time}");
    public static string WaterGoalReached => T("¡Meta cumplida!", "Goal reached!");
    public static string WaterReminderTitle => T("Hora de beber agua", "Time to drink water");
    public static string WaterReminderMessage(string drunk, string goal) => T($"Llevas {drunk} de {goal} hoy", $"{drunk} of {goal} so far today");
    public static string Later => T("Luego", "Later");
    public static string WaterResetConfirm => T("¿Poner a cero?", "Reset today?");
    public static string DecimalSeparator => T(",", ".");

    // Tareas
    public static string TasksNew => T("Nueva tarea…", "New task…");
    public static string TasksHint => T("Ej.: Llamar a Ana 18:30 · Pizza en 20 min", "e.g. Call Ana 6:30pm · Pizza in 20 min");
    public static string TasksEmpty => T("Sin tareas pendientes", "Nothing to do");
    public static string TasksTimeUp => T("¡Se acabó el tiempo!", "Time's up!");
    public static string TasksSnooze(int min) => T($"Posponer {min} min", $"Snooze {min} min");
    public static string TasksDone => T("Hecho", "Done");
    public static string Tomorrow => T("mañana", "tomorrow");

    // Estante
    public static string ShelfDropHint => T("Arrastra aquí archivos, carpetas o texto", "Drop files, folders or text here");
    public static string ShelfDropDetail => T("Se guardan como enlaces, no como copias", "Kept as links, never copied");
    public static string ShelfDropNow => T("Suelta para añadir", "Drop to add");
    public static string ShelfCount(int n) => T(n == 1 ? "1 elemento" : $"{n} elementos", n == 1 ? "1 item" : $"{n} items");
    public static string ShelfSelected(int selected, int total) => T($"{selected} de {total} seleccionados", $"{selected} of {total} selected");
    public static string ShelfClear => T("Vaciar todo", "Clear all");
    public static string ShelfCopied => T("Copiado", "Copied");
    public static string ShelfMissing => T("No encontrado", "Not found");

    // Bandeja del sistema
    public static string TrayStartWithWindows => T("Iniciar con Windows", "Start with Windows");
    public static string TrayMascot => T("Mascota", "Mascot");
    public static string TrayMinimal => T("Notch mínimo", "Minimal notch");
    public static string TraySettings => T("Ajustes…", "Settings…");
    public static string TrayAbout => T("Acerca de Tilde", "About Tilde");
    public static string TrayExit => T("Salir", "Exit");

    public static string AboutTitle => T("Acerca de Tilde", "About Tilde");

    public static string AboutText(string version) => T(
        $"Tilde {version}\n\nConvierte el borde superior de la pantalla en un notch vivo.\n\nTus datos se guardan solo en este equipo.",
        $"Tilde {version}\n\nTurns the top edge of your screen into a living notch.\n\nYour data stays on this computer.");
}
