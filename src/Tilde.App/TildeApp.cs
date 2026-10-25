using System.Diagnostics;
using Tilde.App.Music;
using Tilde.App.Notch;
using Tilde.App.Pages;
using Tilde.Core;
using Tilde.Core.Settings;
using Tilde.Platform;
using Tilde.Render;
using static Tilde.Platform.Interop.Native;

namespace Tilde.App;

/// <summary>
/// Raíz de la aplicación: crea los servicios, la ventana del notch y el icono de la bandeja,
/// y ejecuta el bucle de mensajes de Windows en un único hilo de interfaz.
/// </summary>
internal sealed class TildeApp : IDisposable
{
    private const int CmdStartup = 1;
    private const int CmdMascot = 2;
    private const int CmdMinimal = 3;
    private const int CmdSettings = 4;
    private const int CmdAbout = 5;
    private const int CmdExit = 6;

    private readonly UiDispatcher _dispatcher;
    private readonly SettingsStore _settings;
    private readonly GraphicsDevice _graphics;
    private readonly MusicController _music;
    private readonly NotchWindow _notch;
    private readonly TrayIcon _tray;
    private readonly nint _icon;

    public TildeApp()
    {
        // Todo lo asíncrono (multimedia, red) vuelve al hilo de interfaz tras cada await.
        _dispatcher = new UiDispatcher();
        SynchronizationContext.SetSynchronizationContext(new UiSynchronizationContext(_dispatcher));

        _settings = new SettingsStore(AppPaths.SettingsFile);
        if (!File.Exists(AppPaths.SettingsFile))
        {
            _settings.Save(); // Crea settings.json con los valores por defecto.
        }

        _graphics = new GraphicsDevice();
        _music = new MusicController(_settings, _graphics, Version);
        _notch = new NotchWindow(_graphics, _settings, PageCatalog.Create(_music, _settings), _music);
        _notch.SettingsRequested += OpenSettings;

        // Cambios en settings.json hechos desde fuera: se aplican en el hilo de interfaz.
        _settings.FileChanged += () => PostMessageW(_notch.Handle, NotchWindow.WM_APP_SETTINGS, 0, 0);
        _settings.StartWatching();

        _icon = AppIcon.LoadSmall();
        _tray = new TrayIcon(_icon, "Tilde");
        _tray.Clicked += ShowTrayMenu;

        _ = _music.StartAsync();
    }

    public static string Version => typeof(TildeApp).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Bucle de mensajes: el hilo duerme dentro de GetMessage hasta que pasa algo.</summary>
    public int Run()
    {
        while (GetMessageW(out var msg, 0, 0, 0) > 0)
        {
            TranslateMessage(msg);
            DispatchMessageW(msg);
        }
        return 0;
    }

    public void Dispose()
    {
        _tray.Dispose(); // Importante: si no, queda un icono «fantasma» en la bandeja.
        _notch.Dispose();
        _music.Dispose();
        _graphics.Dispose();
        _settings.Dispose();
        _dispatcher.Dispose();
        if (_icon != 0)
        {
            DestroyIcon(_icon);
        }
    }

    private void ShowTrayMenu(int x, int y)
    {
        var s = _settings.Current;
        MenuItem[] items =
        [
            new(CmdStartup, Strings.TrayStartWithWindows, StartupManager.IsEnabled),
            new(CmdMascot, Strings.TrayMascot, s.MascotEnabled),
            new(CmdMinimal, Strings.TrayMinimal, s.MinimalNotch),
            MenuItem.Separator,
            new(CmdSettings, Strings.TraySettings),
            new(CmdAbout, Strings.TrayAbout),
            MenuItem.Separator,
            new(CmdExit, Strings.TrayExit),
        ];

        switch (_tray.ShowMenu(items, x, y))
        {
            case CmdStartup:
                StartupManager.SetEnabled(!StartupManager.IsEnabled);
                break;
            case CmdMascot:
                _settings.Update(st => st.MascotEnabled = !st.MascotEnabled); // Efecto visible en la fase 5.
                break;
            case CmdMinimal:
                _settings.Update(st => st.MinimalNotch = !st.MinimalNotch);
                _notch.ApplySettings();
                break;
            case CmdSettings:
                OpenSettings();
                break;
            case CmdAbout:
                MessageBoxW(0, Strings.AboutText(Version), Strings.AboutTitle, MB_ICONINFORMATION);
                break;
            case CmdExit:
                PostQuitMessage(0);
                break;
        }
    }

    /// <summary>
    /// Provisional hasta la fase 7 (ventana de Ajustes): abre settings.json en el Bloc de notas.
    /// Al guardar, Tilde detecta el cambio y lo aplica al instante.
    /// </summary>
    private void OpenSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{AppPaths.SettingsFile}\"") { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo abrir el archivo de ajustes", ex);
        }
    }
}

/// <summary>Carga el icono de la app para la bandeja al tamaño correcto para la escala actual.</summary>
internal static class AppIcon
{
    /// <summary>Id del icono que el compilador incrusta a partir de &lt;ApplicationIcon&gt;.</summary>
    private const nint ApplicationIconId = 32512;

    public static nint LoadSmall()
    {
        uint dpi = Monitors.Dpi(Monitors.Primary());
        int size = GetSystemMetricsForDpi(SM_CXSMICON, dpi);
        nint icon = LoadImageW(GetModuleHandleW(0), ApplicationIconId, IMAGE_ICON, size, size, 0);
        if (icon == 0)
        {
            icon = LoadImageFromFile(0, Path.Combine(AppContext.BaseDirectory, "Tilde.ico"), IMAGE_ICON, size, size, LR_LOADFROMFILE);
        }
        return icon;
    }
}
