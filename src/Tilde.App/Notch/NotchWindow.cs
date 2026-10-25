using Tilde.App.Alerts;
using Tilde.App.Music;
using Tilde.App.Productivity;
using Tilde.App.Pages;
using Tilde.Core;
using Tilde.Core.Layout;
using Tilde.Core.Settings;
using Tilde.Platform;
using Tilde.Platform.Interop;
using Tilde.Render;
using Vortice.DirectComposition;
using static Tilde.Platform.Interop.Native;

namespace Tilde.App.Notch;

/// <summary>
/// Ventana del notch: sin bordes, transparente (DirectComposition, sin «redirection bitmap»),
/// siempre encima, centrada arriba, fuera de la barra de tareas y de Alt+Tab, y que no roba el
/// foco al pasar el ratón.
///
/// Todo funciona por eventos: mensajes de ratón, un temporizador alineado al minuto para el
/// reloj, notificaciones de pantalla completa y de cambios de ajustes. Nada se sondea.
/// </summary>
internal sealed unsafe partial class NotchWindow : Win32Window
{
    public const uint WM_APP_SETTINGS = WM_APP + 11;
    private const uint WM_APP_APPBAR = WM_APP + 10;

    private const nuint TimerClock = 1;
    private const nuint TimerCollapse = 2;
    private const nuint TimerRegion = 3;
    private const nuint TimerDemo = 99;

    /// <summary>Espera antes de cerrar al salir el ratón (evita parpadeos si sales un instante).</summary>
    private const uint CollapseDelayMs = 260;

    /// <summary>La región se encoge cuando ha terminado la animación de cierre.</summary>
    private const uint RegionDelayMs = 340;

    private readonly GraphicsDevice _g;
    private readonly SettingsStore _settings;
    private readonly IReadOnlyList<Page> _pages;
    private readonly MusicController _music;
    private readonly AlertCenter _alerts;
    private readonly PomodoroService _pomodoro;
    private readonly ShelfService _shelf;
    private readonly FullscreenWatcher _fullscreen;
    private IDCompositionTarget _target;
    private NotchView? _view;
    private Theme? _theme;
    private NotchGeometry _geo = null!;
    private nint _monitor;
    private bool _expanded;
    private bool _trackingMouse;
    private bool _hidden = true;
    private int _page;
    private int _wheelAccumulator;
    private long _wheelBlockedUntil;
    private nint _previousForeground;
    private bool _capturing;
    private bool _pointerInContent;

    public NotchWindow(GraphicsDevice graphics, SettingsStore settings, IReadOnlyList<Page> pages, MusicController music,
        AlertCenter alerts, PomodoroService pomodoro, ShelfService shelf)
    {
        _shelf = shelf;
        _g = graphics;
        _settings = settings;
        _pages = pages;
        _music = music;
        _alerts = alerts;
        _pomodoro = pomodoro;
        _page = Math.Clamp(settings.Current.LastPage, 0, pages.Count - 1);

        CreateWindow("TildeNotch",
            WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_NOREDIRECTIONBITMAP,
            WS_POPUP, 0, 0, 1, 1);
        ConfigureDwm();

        _monitor = Monitors.Primary();
        _target = _g.CreateTarget(Handle);
        Rebuild();

        // La píldora muestra la línea de la letra mientras suena música.
        music.LineChanged += UpdatePill;
        music.PlaybackChanged += UpdatePill;
        music.TrackChanged += UpdatePill;
        music.ArtChanged += UpdatePill;
        pomodoro.MinuteChanged += UpdatePill;
        alerts.Changed += UpdateAlert;
        foreach (var page in pages)
        {
            if (page is CanvasPage canvas)
            {
                canvas.Host = this;
            }
        }

        _fullscreen = new FullscreenWatcher(Handle, WM_APP_APPBAR, () => _monitor);
        _fullscreen.Changed += _ => ApplyVisibility();
        _fullscreen.ForegroundChanged += ReassertTopmost;
        InitDragDrop();
        ApplyVisibility();
        UpdateClock();
#if DEBUG
        if (Environment.GetEnvironmentVariable("TILDE_DEBUG_DEMO") == "hold")
        {
            _demoStep = 100; // Solo abrir el notch y dejarlo abierto.
            Expand();
        }
        else if (Environment.GetEnvironmentVariable("TILDE_DEBUG_DEMO") == "input")
        {
            // Abre la página de tareas y pulsa «Nueva tarea» (prueba del campo de texto).
            _demoStep = 100;
            Expand();
            int tasks = _pages.ToList().FindIndex(p => p is TasksPage);
            GoToPage(tasks);
            ((CanvasPage)_pages[tasks]).DebugActivate("add");
            _demoStep = 200;
            SetTimer(Handle, TimerDemo, 3000, 0);
        }
        else if (Environment.GetEnvironmentVariable("TILDE_DEBUG_DEMO") == "reset")
        {
            // Abre Agua y pulsa «poner a cero» (el segundo clic llega a los 3 s).
            Expand();
            int water = _pages.ToList().FindIndex(p => p is WaterPage);
            GoToPage(water);
            ((CanvasPage)_pages[water]).DebugActivate("reset");
            _demoStep = 300;
            SetTimer(Handle, TimerDemo, 3000, 0);
        }
        else if (Environment.GetEnvironmentVariable("TILDE_DEBUG_DEMO") == "1")
        {
            SetTimer(Handle, TimerDemo, 1000, 0);
        }
#endif
    }

    /// <summary>El usuario ha pulsado el botón de Ajustes de la barra.</summary>
    public event Action? SettingsRequested;

    /// <summary>Se han recargado los ajustes desde el archivo.</summary>
    public event Action? SettingsReloaded;

    /// <summary>Vuelve a leer los ajustes y reconstruye lo que haga falta.</summary>
    public void ApplySettings()
    {
        _music.ApplySettings();
        SettingsReloaded?.Invoke();
        Render(Rebuild);
        ApplyVisibility();
    }

    public override void Dispose()
    {
        KillTimer(Handle, TimerClock);
        KillTimer(Handle, TimerCollapse);
        KillTimer(Handle, TimerRegion);
        KillTimer(Handle, TimerAlert);
        _textInput?.Dispose();
        _drop?.Dispose();
        _fullscreen.Dispose();
        _view?.Dispose();
        _theme?.Dispose();
        _target.Dispose();
        base.Dispose();
    }

    protected override nint WndProc(uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_MOUSEMOVE:
                OnMouseMove(LoWord(lParam), HiWord(lParam));
                return 0;

            case WM_MOUSELEAVE:
                _trackingMouse = false;
                if (_shownAlert is not null)
                {
                    AlertMouseLeave();
                }
                else if (!_capturing && !InputOpen)
                {
                    LeaveContent();
                    SetTimer(Handle, TimerCollapse, CollapseDelayMs, 0);
                }
                return 0;

            case WM_LBUTTONDOWN when _shownAlert is null:
                OnPointerDown(LoWord(lParam), HiWord(lParam));
                return 0;

            case WM_LBUTTONUP when _shownAlert is not null:
                AlertClick(LoWord(lParam), HiWord(lParam));
                return 0;

            case WM_LBUTTONUP:
                OnClick(LoWord(lParam), HiWord(lParam));
                return 0;

            case WM_CAPTURECHANGED:
                _capturing = false;
                return 0;

            case WM_SETCURSOR:
                if (LoWord(lParam) == 1 && IsOverClickable()) // HTCLIENT
                {
                    SetCursor(LoadCursorW(0, IDC_HAND));
                    return 1;
                }
                break;

            case WM_MOUSEWHEEL:
            case WM_MOUSEHWHEEL:
                OnWheel(HiWord(wParam), msg == WM_MOUSEHWHEEL);
                return 0;

            case WM_KEYDOWN:
                OnKey((int)wParam);
                return 0;

            case WM_MOUSEACTIVATE:
                return MA_NOACTIVATE; // Hacer clic no roba el foco a la app en la que escribes.

            case WM_TIMER:
                OnTimer((nuint)wParam);
                return 0;

            case WM_DPICHANGED:
            case WM_DISPLAYCHANGE:
                // Cambió la escala o la resolución: recolocamos y redibujamos a la nueva escala.
                Log.Info(msg == WM_DPICHANGED ? "Cambio de escala de pantalla" : "Cambio de pantalla o resolución");
                _monitor = Monitors.Primary();
                Render(Rebuild);
                return 0;

            case WM_SETTINGCHANGE:
                if (lParam != 0 && new string((char*)lParam) == "intl")
                {
                    RegionalFormat.Invalidate(); // Cambió el formato regional (12/24 h, idioma…).
                    UpdateClock();
                }
                return 0;

            case WM_TIMECHANGE:
                UpdateClock();
                return 0;

            case WM_POWERBROADCAST:
                if ((uint)wParam == PBT_APMRESUMEAUTOMATIC)
                {
                    UpdateClock(); // Al volver de suspensión el reloj debe ponerse al día.
                }
                return 1;

            case WM_APP_APPBAR:
                _fullscreen.HandleAppBarMessage(wParam);
                return 0;

            case WM_APP_SETTINGS:
                if (_settings.TryReload())
                {
                    ApplySettings();
                }
                return 0;
        }
        return base.WndProc(msg, wParam, lParam);
    }

    // ── Ratón y teclado ──

    private void OnMouseMove(int x, int y)
    {
        if (_shownAlert is not null)
        {
            AlertMouseMove(x, y);
            return;
        }
        if (!_trackingMouse)
        {
            var tme = new TRACKMOUSEEVENT
            {
                cbSize = (uint)sizeof(TRACKMOUSEEVENT),
                dwFlags = TME_LEAVE,
                hwndTrack = Handle,
            };
            TrackMouseEvent(ref tme);
            _trackingMouse = true;
        }
        KillTimer(Handle, TimerCollapse);

        if (!_expanded)
        {
            Expand();
            return;
        }
        var (lx, ly) = ToLocal(x, y);
        if (_capturing)
        {
            Render(() => _view?.ActivePage.OnPointerMove(lx - _geo.Content.X, ly - _geo.Content.Y));
            return;
        }
        Render(() => _view?.SetBarHover(_view.IsOverBar(lx, ly)));
        if (_geo.Content.Contains(lx, ly))
        {
            _pointerInContent = true;
            Render(() => _view?.ActivePage.OnPointerMove(lx - _geo.Content.X, ly - _geo.Content.Y));
        }
        else
        {
            LeaveContent();
        }
    }

    private void LeaveContent()
    {
        if (_pointerInContent)
        {
            _pointerInContent = false;
            Render(() => _view?.ActivePage.OnPointerLeave());
        }
    }

    private void OnPointerDown(int x, int y)
    {
        if (!_expanded || _view is null)
        {
            return;
        }
        ActivateForKeyboard();
        var (lx, ly) = ToLocal(x, y);
        if (_geo.Content.Contains(lx, ly) && _view.ActivePage.OnPointerDown(lx - _geo.Content.X, ly - _geo.Content.Y))
        {
            // Arrastre (barra de progreso, volumen…): seguimos recibiendo el ratón aunque salga.
            SetCapture(Handle);
            _capturing = true;
        }
    }

    /// <summary>¿Hay algo clicable bajo el cursor? (para mostrar la mano).</summary>
    private bool IsOverClickable()
    {
        if (_view is null || !GetCursorPos(out var point))
        {
            return false;
        }
        ScreenToClient(Handle, ref point);
        if (_shownAlert is not null)
        {
            var (wx, wy) = ToWindowDip(point.X, point.Y);
            return _view.HitTestAlert(wx, wy) != NotchView.AlertHitNone;
        }
        if (!_expanded)
        {
            return false;
        }
        var (lx, ly) = ToLocal(point.X, point.Y);
        if (_view.HitTestBar(lx, ly) != NotchView.HitNone)
        {
            return true;
        }
        return _geo.Content.Contains(lx, ly) && _view.ActivePage.IsClickable(lx - _geo.Content.X, ly - _geo.Content.Y);
    }

    private void OnClick(int x, int y)
    {
        if (!_expanded || _view is null)
        {
            return;
        }
        var (lx, ly) = ToLocal(x, y);
        if (_capturing)
        {
            ReleaseCapture();
            Render(() => _view?.ActivePage.OnPointerUp(lx - _geo.Content.X, ly - _geo.Content.Y));
            // Si el arrastre terminó fuera del notch, volvemos a vigilar la salida del ratón.
            TrackMouseLeave();
            return;
        }
        if (_geo.Content.Contains(lx, ly))
        {
            Render(() => _view?.ActivePage.OnPointerUp(lx - _geo.Content.X, ly - _geo.Content.Y));
            return;
        }
        int hit = _view.HitTestBar(lx, ly);
        if (hit >= 0)
        {
            GoToPage(hit);
        }
        else if (hit == NotchView.HitSettings)
        {
            SettingsRequested?.Invoke();
        }
    }

    private void OnWheel(int delta, bool horizontal)
    {
        if (!_expanded)
        {
            return;
        }
        long now = Environment.TickCount64;
        if (now < _wheelBlockedUntil)
        {
            return;
        }
        // Rueda hacia abajo o hacia la derecha = página siguiente. Los touchpads envían pasos
        // pequeños: acumulamos hasta una «muesca» (120) y bloqueamos un instante tras cambiar.
        _wheelAccumulator += horizontal ? delta : -delta;
        if (Math.Abs(_wheelAccumulator) >= 120)
        {
            GoToPage(_page + Math.Sign(_wheelAccumulator));
            _wheelAccumulator = 0;
            _wheelBlockedUntil = now + 220;
        }
    }

    private void TrackMouseLeave()
    {
        var tme = new TRACKMOUSEEVENT { cbSize = (uint)sizeof(TRACKMOUSEEVENT), dwFlags = TME_LEAVE, hwndTrack = Handle };
        TrackMouseEvent(ref tme);
        _trackingMouse = true;
    }

    private void OnKey(int key)
    {
        if (_expanded && _view is not null && _view.ActivePage.OnKey(key))
        {
            return;
        }
        switch (key)
        {
            case VK_LEFT: GoToPage(_page - 1); break;
            case VK_RIGHT: GoToPage(_page + 1); break;
            case VK_ESCAPE: Collapse(); break;
        }
    }

    private void OnTimer(nuint id)
    {
        switch (id)
        {
            case TimerClock:
                UpdateClock();
                break;
            case TimerCollapse:
                KillTimer(Handle, TimerCollapse);
                if (!InputOpen && !_capturing && !_dragOver)
                {
                    Collapse();
                }
                break;
            case TimerAlert:
                OnAlertTimer();
                break;
#if DEBUG
            case TimerDemo:
                RunDemoStep();
                break;
#endif
            case TimerRegion:
                KillTimer(Handle, TimerRegion);
                if (!_expanded)
                {
                    UpdateRegion();
                }
                break;
        }
    }

    /// <summary>
    /// Las flechas necesitan el foco del teclado. Solo lo tomamos cuando el usuario hace clic en
    /// el notch, y lo devolvemos a la ventana anterior al cerrarse.
    /// </summary>
    private void ActivateForKeyboard()
    {
        nint foreground = GetForegroundWindow();
        if (foreground != Handle)
        {
            _previousForeground = foreground;
            SetForegroundWindow(Handle);
        }
    }

    // ── Estados ──

    private void Expand()
    {
        if (_shownAlert is not null)
        {
            return; // Con un aviso en pantalla, el notch no se abre hasta cerrarlo.
        }
        _expanded = true;
        _wheelAccumulator = 0;
        KillTimer(Handle, TimerRegion);
        UpdateRegion(); // Primero ampliamos la zona clicable y luego anima la forma.
        Render(() => _view?.SetExpanded(true));
    }

    private void Collapse(bool animate = true)
    {
        if (!_expanded)
        {
            return;
        }
        _expanded = false;
        Render(() => _view?.SetExpanded(false, animate));
        if (animate)
        {
            SetTimer(Handle, TimerRegion, RegionDelayMs, 0);
        }
        else
        {
            UpdateRegion();
        }

        if (GetForegroundWindow() == Handle && _previousForeground != 0 && IsWindow(_previousForeground))
        {
            SetForegroundWindow(_previousForeground);
        }
        _previousForeground = 0;
    }

    private void GoToPage(int index)
    {
        index = Math.Clamp(index, 0, _pages.Count - 1);
        if (index == _page)
        {
            return;
        }
        int direction = Math.Sign(index - _page);
        _page = index;
        Render(() => _view?.SetActivePage(index, direction));
        _settings.Update(s => s.LastPage = index);
    }

    private void ApplyVisibility()
    {
        bool hide = _settings.Current.HideInFullscreen && _fullscreen.IsFullscreen;
        if (hide == _hidden)
        {
            return;
        }
        _hidden = hide;
        Log.Info(hide ? "Pantalla completa detectada: notch oculto" : "Notch visible");
        if (hide)
        {
            Collapse(animate: false);
            ShowWindow(Handle, SW_HIDE);
        }
        else
        {
            ShowWindow(Handle, SW_SHOWNOACTIVATE);
            ReassertTopmost();
        }
        UpdateAlert();
    }

    /// <summary>Otras ventanas «siempre encima» pueden taparnos: al cambiar de app volvemos arriba.</summary>
    private void ReassertTopmost()
    {
        if (!_hidden)
        {
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }

    // ── Reloj ──

    private void UpdateClock()
    {
        var now = DateTime.Now;
        Render(() => _view?.SetClock(RegionalFormat.ShortTime(now), RegionalFormat.DayAndMonth(now)));
        UpdatePill();
        // Próximo aviso justo después del cambio de minuto: un único despertar por minuto.
        uint ms = (uint)(60_000 - (now.Second * 1000 + now.Millisecond)) + 25;
        SetTimer(Handle, TimerClock, ms, 0);
    }

#if DEBUG
    private int _demoStep;

    /// <summary>
    /// Solo depuración (TILDE_DEBUG_DEMO=1): recorre los estados del notch sin ratón, un paso
    /// por segundo, para poder revisar animaciones y capturas de forma automática.
    /// </summary>
    private void RunDemoStep()
    {
        switch (_demoStep++)
        {
            case 0: Expand(); break;
            case 1: Render(() => _view?.SetBarHover(true)); break;
            case 2: GoToPage(_page + 1); break;
            case 3: Render(() => _view?.SetBarHover(false)); break;
            case 4: GoToPage(_page - 1); break;
            case 5: Collapse(); break;
            case 300:
                ((CanvasPage)_pages[_page]).DebugActivate("reset-confirm");
                KillTimer(Handle, TimerDemo);
                break;
            case 200:
                _textInput?.DebugSubmit("Comprar pan en 30 min");
                KillTimer(Handle, TimerDemo);
                break;
            default: KillTimer(Handle, TimerDemo); break;
        }
    }
#endif

    /// <summary>
    /// Decide qué muestra la píldora: la línea actual de la letra (con la carátula) si suena
    /// música con letra sincronizada, o la hora en caso contrario.
    /// </summary>
    private void UpdatePill()
    {
        PillContent pill;
        if (_settings.Current.LyricsInPill && !_settings.Current.MinimalNotch && _music.CurrentLineText is { } line)
        {
            pill = new PillContent(line.Length > 0 ? line : "♪", _music.GetArt(), IsLyric: true);
        }
        else if (!_settings.Current.MinimalNotch && _pomodoro.PillText is { } remaining)
        {
            // Pomodoro en curso: icono de cronómetro y minutos que quedan (cambia una vez por minuto).
            pill = new PillContent(remaining, null, IsLyric: false, Icon: IconKind.Timer);
        }
        else
        {
            pill = PillContent.Clock(RegionalFormat.ShortTime(DateTime.Now));
        }
        Render(() => _view?.SetPill(pill));
        if (!_expanded)
        {
            UpdateRegion(); // La píldora puede haber cambiado de ancho (p. ej. 9:59 → 10:00).
        }
    }

    // ── Geometría y composición ──

    /// <summary>Recalcula tamaños para el monitor actual, recoloca la ventana y recrea las capas.</summary>
    private void Rebuild()
    {
        var bounds = Monitors.Bounds(_monitor);
        float scale = Monitors.Dpi(_monitor) / 96f;
        float factor = NotchMetrics.SizeFactor(_settings.Current.NotchSize, bounds.Width / scale, bounds.Height / scale);
        _geo = new NotchGeometry(scale, factor, _settings.Current.MinimalNotch);

        int w = _geo.WindowPixelWidth;
        int h = _geo.WindowPixelHeight;
        int x = bounds.Left + (bounds.Width - w) / 2;
#if DEBUG
        // Solo en depuración: TILDE_DEBUG_OFFSET_X desplaza el notch para probarlo junto a otra
        // app de notch que ocupe el centro (p. ej. Nusa).
        if (int.TryParse(Environment.GetEnvironmentVariable("TILDE_DEBUG_OFFSET_X"), out int offset))
        {
            x += offset;
        }
#endif
        SetWindowPos(Handle, HWND_TOPMOST, x, bounds.Top, w, h, SWP_NOACTIVATE);
        Log.Info($"Notch: escala {scale:0.##}, tamaño ×{factor:0.##}, ventana {w}×{h} px, mínimo={_geo.Minimal}");

        _view?.Dispose();
        _theme?.Dispose();
        _theme = new Theme(_g, factor);
        _view = new NotchView(_g, _target, _geo, _theme, _pages, _page, _expanded);
        var now = DateTime.Now;
        _view.SetClock(RegionalFormat.ShortTime(now), RegionalFormat.DayAndMonth(now));
        UpdatePill();
        if (_shownAlert is { } alert)
        {
            _view.ShowAlert(alert);
        }
        UpdateRegion();
    }

    /// <summary>
    /// La región define qué parte de la ventana recibe el ratón. Cerrado: solo la píldora, así
    /// el resto (transparente) deja pasar los clics a las ventanas de debajo.
    /// </summary>
    private void UpdateRegion()
    {
        if (_view is null)
        {
            return;
        }
        var r = _shownAlert is not null ? _view.AlertRegion
            : _expanded ? new RECT { Right = _geo.WindowPixelWidth, Bottom = _geo.WindowPixelHeight }
            : _view.CollapsedRegion;
        nint region = CreateRectRgn(r.Left, r.Top, r.Right, r.Bottom);
        if (SetWindowRgn(Handle, region, true) == 0)
        {
            DeleteObject(region); // Si falla, la región sigue siendo nuestra y hay que liberarla.
        }
    }

    /// <summary>Convierte píxeles de la ventana en DIP relativos al notch abierto.</summary>
    private (float X, float Y) ToLocal(int x, int y) => (x / _geo.Scale - _geo.MarginDip, y / _geo.Scale);

    /// <summary>
    /// Ejecuta una operación de dibujo. Si la GPU se ha perdido (driver actualizado, cambio de
    /// adaptador…), recrea los dispositivos y todas las capas.
    /// </summary>
    private void Render(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (_g.IsDeviceLost)
        {
            Log.Warn($"Se perdió el dispositivo gráfico ({ex.Message}); se recrea");
            try { _view?.Dispose(); } catch { }
            try { _theme?.Dispose(); } catch { }
            try { _target.Dispose(); } catch { }
            _view = null;
            _theme = null;
            _g.Recreate();
            _music.ResetDeviceResources();
            _target = _g.CreateTarget(Handle);
            Rebuild();
        }
    }

    private void ConfigureDwm()
    {
        int on = 1;
        // Sin animaciones de mostrar/ocultar del sistema, y fuera de «Vistazo al escritorio».
        DwmSetWindowAttribute(Handle, DWMWA_TRANSITIONS_FORCEDISABLED, &on, sizeof(int));
        DwmSetWindowAttribute(Handle, DWMWA_EXCLUDED_FROM_PEEK, &on, sizeof(int));
    }
}
