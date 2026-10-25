namespace Tilde.Core;

/// <summary>
/// Rutas donde Tilde guarda sus datos. Todo vive en la carpeta local del usuario
/// (%LOCALAPPDATA%\Tilde); nada se sincroniza ni se envía fuera del equipo.
/// </summary>
public static class AppPaths
{
    /// <summary>
    /// Carpeta raíz de datos. Se puede redirigir con la variable de entorno TILDE_DATA_DIR
    /// (útil para pruebas o para ejecutar una copia portátil).
    /// </summary>
    public static string DataDirectory { get; } =
        Environment.GetEnvironmentVariable("TILDE_DATA_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tilde");

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    /// <summary>Crea las carpetas de datos si aún no existen.</summary>
    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);
    }
}
