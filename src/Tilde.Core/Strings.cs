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
