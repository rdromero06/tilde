using Tilde.Platform.Interop;
using static Tilde.Platform.Interop.Native;

namespace Tilde.Platform;

/// <summary>Elemento de un menú contextual nativo.</summary>
public sealed record MenuItem(int Id, string Text, bool Checked = false, bool Enabled = true, IReadOnlyList<MenuItem>? Children = null)
{
    public static MenuItem Separator { get; } = new(0, "-");

    public bool IsSeparator => Text == "-";
}

/// <summary>
/// Icono en el área de notificación con menú contextual. Usa su propia ventana oculta
/// (no la del notch) porque el menú necesita una ventana que pueda pasar a primer plano, y
/// para recibir «TaskbarCreated» y volver a añadirse si el Explorador se reinicia.
/// </summary>
public sealed unsafe class TrayIcon : Win32Window
{
    private const uint WM_TRAY = WM_APP + 1;
    private readonly uint _taskbarCreated;
    private readonly string _tooltip;
    private nint _icon;

    public TrayIcon(nint icon, string tooltip)
    {
        _icon = icon;
        _tooltip = tooltip;
        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        CreateWindow("TildeTrayHost", WS_EX_TOOLWINDOW, WS_POPUP, 0, 0, 0, 0);
        Add();
    }

    /// <summary>El usuario ha hecho clic (izquierdo o derecho) en el icono. Coordenadas de pantalla.</summary>
    public event Action<int, int>? Clicked;

    /// <summary>Muestra un menú en la posición indicada y devuelve el Id elegido (0 si se cancela).</summary>
    public int ShowMenu(IReadOnlyList<MenuItem> items, int x, int y)
    {
        nint menu = BuildMenu(items);
        try
        {
            // Truco documentado: sin esto el menú no se cierra al hacer clic fuera.
            SetForegroundWindow(Handle);
            int id = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_BOTTOMALIGN | TPM_NONOTIFY, x, y, Handle, 0);
            PostMessageW(Handle, WM_NULL, 0, 0);
            return id;
        }
        finally
        {
            DestroyMenu(menu); // Destruye también los submenús.
        }
    }

    public override void Dispose()
    {
        var data = NewData();
        Shell_NotifyIconW(NIM_DELETE, ref data);
        base.Dispose();
    }

    protected override nint WndProc(uint msg, nint wParam, nint lParam)
    {
        if (msg == WM_TRAY)
        {
            // Con NOTIFYICON_VERSION_4: el evento va en LOWORD(lParam) y las coordenadas en wParam.
            uint ev = (uint)LoWord(lParam);
            if (ev is WM_CONTEXTMENU or NIN_SELECT or NIN_KEYSELECT)
            {
                Clicked?.Invoke(LoWord(wParam), HiWord(wParam));
            }
            return 0;
        }
        if (msg == _taskbarCreated && _taskbarCreated != 0)
        {
            Add(); // El Explorador se ha reiniciado.
            return 0;
        }
        return base.WndProc(msg, wParam, lParam);
    }

    private void Add()
    {
        var data = NewData();
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP;
        data.uCallbackMessage = WM_TRAY;
        data.hIcon = _icon;
        var tip = _tooltip.AsSpan(0, Math.Min(_tooltip.Length, 127));
        tip.CopyTo(new Span<char>(data.szTip, 128));
        Shell_NotifyIconW(NIM_ADD, ref data);
        data.uVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIconW(NIM_SETVERSION, ref data);
    }

    private NOTIFYICONDATAW NewData() => new()
    {
        cbSize = (uint)sizeof(NOTIFYICONDATAW),
        hWnd = Handle,
        uID = 1,
    };

    private static nint BuildMenu(IReadOnlyList<MenuItem> items)
    {
        nint menu = CreatePopupMenu();
        foreach (var item in items)
        {
            if (item.IsSeparator)
            {
                AppendMenuW(menu, MF_SEPARATOR, 0, null);
                continue;
            }
            uint flags = MF_STRING;
            if (item.Checked) flags |= MF_CHECKED;
            if (!item.Enabled) flags |= MF_GRAYED;
            if (item.Children is { Count: > 0 })
            {
                AppendMenuW(menu, flags | MF_POPUP, (nuint)BuildMenu(item.Children), item.Text);
            }
            else
            {
                AppendMenuW(menu, flags, (nuint)item.Id, item.Text);
            }
        }
        return menu;
    }
}
