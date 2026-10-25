using Microsoft.Win32;
using Tilde.Core;
using Tilde.Platform.Interop;
using static Tilde.Platform.Interop.Native;

namespace Tilde.Platform;

/// <summary>Información de monitores y escala (DPI).</summary>
public static class Monitors
{
    public static nint Primary() => MonitorFromPoint(default, MONITOR_DEFAULTTOPRIMARY);

    public static RECT Bounds(nint monitor)
    {
        var info = new MONITORINFO { cbSize = (uint)System.Runtime.CompilerServices.Unsafe.SizeOf<MONITORINFO>() };
        GetMonitorInfoW(monitor, ref info);
        return info.rcMonitor;
    }

    /// <summary>DPI efectivo del monitor (96 = 100 %, 144 = 150 %…).</summary>
    public static uint Dpi(nint monitor) => GetDpiForMonitor(monitor, 0, out uint dpi, out _) == 0 ? dpi : 96;
}

/// <summary>
/// Formatos de fecha y hora según la configuración regional de Windows (12/24 h, idioma,
/// orden día/mes). Usamos las APIs de Windows y no las de .NET porque la app se compila
/// sin ICU para ahorrar memoria, y porque así respetamos las personalizaciones del usuario.
/// </summary>
public static unsafe class RegionalFormat
{
    private static string? s_shortTime;
    private static string? s_monthDay;

    /// <summary>Olvida los formatos en caché (llamar con WM_SETTINGCHANGE "intl").</summary>
    public static void Invalidate()
    {
        s_shortTime = null;
        s_monthDay = null;
    }

    /// <summary>Hora corta, p. ej. «17:05» o «5:05 p. m.».</summary>
    public static string ShortTime(DateTime t)
    {
        s_shortTime ??= LocaleString(LOCALE_SSHORTTIME);
        var st = SYSTEMTIME.From(t);
        char* buffer = stackalloc char[80];
        int n = GetTimeFormatEx(null, 0, st, s_shortTime, buffer, 80);
        return n > 1 ? new string(buffer, 0, n - 1) : t.ToString("HH:mm");
    }

    /// <summary>Día de la semana y fecha sin año, p. ej. «domingo, 25 de octubre».</summary>
    public static string DayAndMonth(DateTime t)
    {
        s_monthDay ??= LocaleString(LOCALE_SMONTHDAY);
        var st = SYSTEMTIME.From(t);
        char* buffer = stackalloc char[128];
        int n = GetDateFormatEx(null, 0, st, "dddd", buffer, 128, null);
        var day = n > 1 ? new string(buffer, 0, n - 1) : string.Empty;
        n = GetDateFormatEx(null, 0, st, s_monthDay, buffer, 128, null);
        var date = n > 1 ? new string(buffer, 0, n - 1) : t.ToString("dd/MM");
        return day.Length > 0 ? $"{day}, {date}" : date;
    }

    private static string? LocaleString(uint type)
    {
        char* buffer = stackalloc char[80];
        int n = GetLocaleInfoEx(null, type, buffer, 80);
        return n > 1 ? new string(buffer, 0, n - 1) : null;
    }
}

/// <summary>Idioma de la interfaz de Windows.</summary>
public static class Language
{
    /// <summary>true si el idioma de visualización de Windows es español (cualquier variante).</summary>
    public static bool IsSpanishUi() => (GetUserDefaultUILanguage() & 0x3FF) == 0x0A;
}

/// <summary>
/// «Iniciar con Windows» mediante la clave Run del usuario (HKCU): no requiere permisos de
/// administrador y el desinstalador la borra.
/// </summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Tilde";

    private static string Command => $"\"{Environment.ProcessPath}\"";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value && value.Equals(Command, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
            {
                key.SetValue(ValueName, Command);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo cambiar «Iniciar con Windows»", ex);
        }
    }
}
