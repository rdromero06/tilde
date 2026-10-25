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
