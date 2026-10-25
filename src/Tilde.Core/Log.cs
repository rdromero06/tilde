using System.Diagnostics;

namespace Tilde.Core;

/// <summary>
/// Registro mínimo en archivo (logs\tilde.log, rota al pasar de 512 KB).
/// Regla de privacidad: aquí nunca se escriben claves, texto tecleado ni contenido de usuario.
/// </summary>
public static class Log
{
    private const long MaxBytes = 512 * 1024;
    private static readonly Lock Gate = new();

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
        Debug.Write(line);
        try
        {
            lock (Gate)
            {
                var path = Path.Combine(AppPaths.LogDirectory, "tilde.log");
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxBytes)
                {
                    File.Move(path, Path.Combine(AppPaths.LogDirectory, "tilde.old.log"), overwrite: true);
                }
                File.AppendAllText(path, line);
            }
        }
        catch
        {
            // El log nunca debe tumbar la app.
        }
    }
}
