using System.Runtime.InteropServices;
using Tilde.Platform.Interop;
using static Tilde.Platform.Interop.Native;

namespace Tilde.Platform;

/// <summary>
/// Detecta si hay una app a pantalla completa en el monitor del notch, sin sondeos:
/// <list type="bullet">
/// <item>Registramos la ventana como «AppBar» (sin reservar espacio) para recibir
/// ABN_FULLSCREENAPP, que el shell envía al entrar o salir de pantalla completa.</item>
/// <item>Un gancho de eventos de primer plano (SetWinEventHook) nos avisa al cambiar de app.</item>
/// </list>
/// Ambos son solo «disparadores»: la decisión final se toma mirando la geometría de la
/// ventana en primer plano, lo que evita falsos positivos conocidos (p. ej. al hacer clic en
/// el escritorio, el shell a veces informa de «pantalla completa»).
/// </summary>
public sealed unsafe class FullscreenWatcher : IDisposable
{
    // Ventanas del propio shell que cubren el monitor pero no son «apps a pantalla completa».
    private static readonly HashSet<string> ShellClasses =
    [
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
        "XamlExplorerHostIslandWindow", "MultitaskingViewFrame", "ForegroundStaging",
        "Windows.UI.Core.CoreWindow", "LockScreenControllerProxyWindow",
    ];

    private static FullscreenWatcher? s_instance;
    private readonly nint _hwnd;
    private readonly uint _callbackMessage;
    private readonly Func<nint> _monitor;
    private nint _hook;

    /// <param name="hwnd">Ventana que recibe las notificaciones del AppBar.</param>
    /// <param name="callbackMessage">Mensaje que el shell enviará a esa ventana.</param>
    /// <param name="monitor">Devuelve el monitor en el que vive el notch.</param>
    public FullscreenWatcher(nint hwnd, uint callbackMessage, Func<nint> monitor)
    {
        _hwnd = hwnd;
        _callbackMessage = callbackMessage;
        _monitor = monitor;
        s_instance = this;
        RegisterAppBar();
        _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, 0, &OnWinEvent, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        IsFullscreen = IsForegroundFullscreen();
    }

    public bool IsFullscreen { get; private set; }

    /// <summary>Cambia el estado de pantalla completa.</summary>
    public event Action<bool>? Changed;

    /// <summary>Ha cambiado la ventana en primer plano (útil para reafirmar «siempre encima»).</summary>
    public event Action? ForegroundChanged;

    /// <summary>Llamar desde el WndProc cuando llegue <c>callbackMessage</c>.</summary>
    public void HandleAppBarMessage(nint wParam)
    {
        if ((uint)wParam == ABN_FULLSCREENAPP)
        {
            Evaluate();
        }
    }

    /// <summary>Vuelve a registrar el AppBar (p. ej. tras reiniciarse el Explorador).</summary>
    public void RegisterAppBar()
    {
        var data = new APPBARDATA { cbSize = (uint)sizeof(APPBARDATA), hWnd = _hwnd, uCallbackMessage = _callbackMessage };
        SHAppBarMessage(ABM_NEW, ref data);
    }

    public void Evaluate()
    {
        bool fullscreen = IsForegroundFullscreen();
        if (fullscreen != IsFullscreen)
        {
            IsFullscreen = fullscreen;
            Changed?.Invoke(fullscreen);
        }
    }

    public void Dispose()
    {
        if (_hook != 0)
        {
            UnhookWinEvent(_hook);
            _hook = 0;
        }
        var data = new APPBARDATA { cbSize = (uint)sizeof(APPBARDATA), hWnd = _hwnd };
        SHAppBarMessage(ABM_REMOVE, ref data);
        if (s_instance == this)
        {
            s_instance = null;
        }
    }

    private bool IsForegroundFullscreen()
    {
        nint fg = GetForegroundWindow();
        if (fg == 0 || fg == _hwnd || !IsWindowVisible(fg) || IsIconic(fg) || IsZoomed(fg))
        {
            return false; // Una ventana maximizada no es «pantalla completa».
        }
        GetWindowThreadProcessId(fg, out uint pid);
        if (pid == (uint)Environment.ProcessId || ShellClasses.Contains(GetClassName(fg)))
        {
            return false;
        }

        nint monitor = _monitor();
        if (MonitorFromWindow(fg, MONITOR_DEFAULTTOPRIMARY) != monitor)
        {
            return false; // Está en otro monitor: no nos estorba.
        }

        // Límites visibles reales (sin los bordes invisibles de redimensionado).
        RECT r;
        if (DwmGetWindowAttribute(fg, DWMWA_EXTENDED_FRAME_BOUNDS, &r, (uint)sizeof(RECT)) != 0)
        {
            GetWindowRect(fg, out r);
        }
        var m = Monitors.Bounds(monitor);
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
    }

    [UnmanagedCallersOnly]
    private static void OnWinEvent(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try
        {
            var self = s_instance;
            if (self is null) return;
            self.ForegroundChanged?.Invoke();
            self.Evaluate();
        }
        catch (Exception ex)
        {
            Tilde.Core.Log.Error("Error en el gancho de primer plano", ex);
        }
    }
}
