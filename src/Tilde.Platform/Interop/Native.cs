using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// Todas las estructuras de este ensamblado son «blittable» (misma forma en memoria que en C),
// así que desactivamos el marshalling en tiempo de ejecución: más rápido y compatible con AOT.
[assembly: DisableRuntimeMarshalling]

namespace Tilde.Platform.Interop;

// ───────────────────────────── Estructuras ─────────────────────────────

[StructLayout(LayoutKind.Sequential)]
public struct POINT
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public readonly int Width => Right - Left;
    public readonly int Height => Bottom - Top;
}

[StructLayout(LayoutKind.Sequential)]
public struct MSG
{
    public nint hwnd;
    public uint message;
    public nint wParam;
    public nint lParam;
    public uint time;
    public POINT pt;
    public uint lPrivate;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WNDCLASSEXW
{
    public uint cbSize;
    public uint style;
    public delegate* unmanaged<nint, uint, nint, nint, nint> lpfnWndProc;
    public int cbClsExtra;
    public int cbWndExtra;
    public nint hInstance;
    public nint hIcon;
    public nint hCursor;
    public nint hbrBackground;
    public char* lpszMenuName;
    public char* lpszClassName;
    public nint hIconSm;
}

[StructLayout(LayoutKind.Sequential)]
public struct MONITORINFO
{
    public uint cbSize;
    public RECT rcMonitor;
    public RECT rcWork;
    public uint dwFlags;
}

[StructLayout(LayoutKind.Sequential)]
public struct TRACKMOUSEEVENT
{
    public uint cbSize;
    public uint dwFlags;
    public nint hwndTrack;
    public uint dwHoverTime;
}

[StructLayout(LayoutKind.Sequential)]
public struct APPBARDATA
{
    public uint cbSize;
    public nint hWnd;
    public uint uCallbackMessage;
    public uint uEdge;
    public RECT rc;
    public nint lParam;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct NOTIFYICONDATAW
{
    public uint cbSize;
    public nint hWnd;
    public uint uID;
    public uint uFlags;
    public uint uCallbackMessage;
    public nint hIcon;
    public fixed char szTip[128];
    public uint dwState;
    public uint dwStateMask;
    public fixed char szInfo[256];
    public uint uVersion;
    public fixed char szInfoTitle[64];
    public uint dwInfoFlags;
    public Guid guidItem;
    public nint hBalloonIcon;
}

[StructLayout(LayoutKind.Sequential)]
public struct FORMATETC
{
    public ushort cfFormat;
    public nint ptd;
    public uint dwAspect;
    public int lindex;
    public uint tymed;
}

[StructLayout(LayoutKind.Sequential)]
public struct STGMEDIUM
{
    public uint tymed;
    public nint handle;
    public nint pUnkForRelease;
}

[StructLayout(LayoutKind.Sequential)]
public struct SIZE
{
    public int cx;
    public int cy;
}

[StructLayout(LayoutKind.Sequential)]
public struct SYSTEMTIME
{
    public ushort wYear;
    public ushort wMonth;
    public ushort wDayOfWeek;
    public ushort wDay;
    public ushort wHour;
    public ushort wMinute;
    public ushort wSecond;
    public ushort wMilliseconds;

    public static SYSTEMTIME From(DateTime t) => new()
    {
        wYear = (ushort)t.Year,
        wMonth = (ushort)t.Month,
        wDayOfWeek = (ushort)t.DayOfWeek,
        wDay = (ushort)t.Day,
        wHour = (ushort)t.Hour,
        wMinute = (ushort)t.Minute,
        wSecond = (ushort)t.Second,
        wMilliseconds = (ushort)t.Millisecond,
    };
}

// ───────────────────────────── Funciones ─────────────────────────────

/// <summary>
/// Declaraciones de las APIs de Win32 que usamos. LibraryImport genera el código de llamada
/// en compilación (sin reflexión), requisito para Native AOT.
/// </summary>
public static unsafe partial class Native
{
    // Mensajes
    public const uint WM_NULL = 0x0000;
    public const uint WM_CREATE = 0x0001;
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_ACTIVATE = 0x0006;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_SETFOCUS = 0x0007;
    public const uint WM_KILLFOCUS = 0x0008;
    public const uint WM_SETFONT = 0x0030;
    public const uint WM_SETTINGCHANGE = 0x001A;
    public const uint WM_TIMECHANGE = 0x001E;
    public const uint WM_SETCURSOR = 0x0020;
    public const uint WM_MOUSEACTIVATE = 0x0021;
    public const uint WM_DISPLAYCHANGE = 0x007E;
    public const uint WM_NCCREATE = 0x0081;
    public const uint WM_CONTEXTMENU = 0x007B;
    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_CHAR = 0x0102;
    public const uint WM_CTLCOLOREDIT = 0x0133;
    public const uint EM_SETSEL = 0x00B1;
    public const uint EM_SETMARGINS = 0x00D3;
    public const uint EM_SETCUEBANNER = 0x1501;
    public const uint EM_LIMITTEXT = 0x00C5;
    public const uint WM_TIMER = 0x0113;
    public const uint WM_MOUSEMOVE = 0x0200;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_MOUSEWHEEL = 0x020A;
    public const uint WM_MOUSEHWHEEL = 0x020E;
    public const uint WM_POWERBROADCAST = 0x0218;
    public const uint WM_CAPTURECHANGED = 0x0215;
    public const uint WM_MOUSELEAVE = 0x02A3;
    public const uint WM_DPICHANGED = 0x02E0;
    public const uint WM_USER = 0x0400;
    public const uint WM_APP = 0x8000;

    // Estilos de ventana
    public const uint WS_POPUP = 0x80000000;
    public const uint WS_CHILD = 0x40000000;
    public const uint WS_VISIBLE = 0x10000000;
    public const uint ES_AUTOHSCROLL = 0x0080;
    public const uint WS_EX_TOPMOST = 0x00000008;
    public const uint WS_EX_TOOLWINDOW = 0x00000080;
    public const uint WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    public const uint WS_EX_NOACTIVATE = 0x08000000;

    // SetWindowPos / ShowWindow
    public static readonly nint HWND_TOPMOST = -1;
    public static readonly nint HWND_MESSAGE = -3;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const int SW_HIDE = 0;
    public const int SW_SHOWNOACTIVATE = 4;

    // Otros
    public const int GWLP_USERDATA = -21;
    public const uint TME_LEAVE = 0x00000002;
    public const int MA_NOACTIVATE = 3;
    public const uint MONITOR_DEFAULTTOPRIMARY = 1;
    public const int VK_ESCAPE = 0x1B;
    public const int VK_RETURN = 0x0D;
    public const int VK_TAB = 0x09;
    public const int VK_LEFT = 0x25;
    public const int VK_RIGHT = 0x27;
    public const uint PBT_APMRESUMEAUTOMATIC = 0x0012;
    public const uint IMAGE_ICON = 1;
    public const uint LR_LOADFROMFILE = 0x0010;
    public const int SM_CXSMICON = 49;
    public const nint IDC_ARROW = 32512;
    public const nint IDC_HAND = 32649;
    public const int VK_SPACE = 0x20;
    public const uint MB_ICONINFORMATION = 0x00000040;

    // Bandeja del sistema
    public const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    public const uint NIF_MESSAGE = 0x01, NIF_ICON = 0x02, NIF_TIP = 0x04, NIF_SHOWTIP = 0x80;
    public const uint NOTIFYICON_VERSION_4 = 4;
    public const uint NIN_SELECT = WM_USER + 0;
    public const uint NIN_KEYSELECT = WM_USER + 1;

    // Menús
    public const uint MF_STRING = 0x0000, MF_GRAYED = 0x0001, MF_CHECKED = 0x0008, MF_POPUP = 0x0010, MF_SEPARATOR = 0x0800;
    public const uint TPM_RIGHTBUTTON = 0x0002, TPM_BOTTOMALIGN = 0x0020, TPM_NONOTIFY = 0x0080, TPM_RETURNCMD = 0x0100;

    // AppBar (detección de pantalla completa)
    public const uint ABM_NEW = 0x00, ABM_REMOVE = 0x01;
    public const uint ABN_FULLSCREENAPP = 0x02;

    // Eventos de accesibilidad
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000, WINEVENT_SKIPOWNPROCESS = 0x0002;

    // DWM
    public const uint DWMWA_TRANSITIONS_FORCEDISABLED = 3;
    public const uint DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    public const uint DWMWA_EXCLUDED_FROM_PEEK = 12;
    public const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    // Configuración regional
    public const uint LOCALE_SMONTHDAY = 0x00000078;
    public const uint LOCALE_SSHORTTIME = 0x00000079;

    // ── user32 ──
    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial ushort RegisterClassExW(in WNDCLASSEXW wc);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial int GetMessageW(out MSG msg, nint hwnd, uint filterMin, uint filterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(in MSG msg);

    [LibraryImport("user32.dll")]
    public static partial nint DispatchMessageW(in MSG msg);

    [LibraryImport("user32.dll")]
    public static partial void PostQuitMessage(int exitCode);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessageW(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint hwnd, int cmd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    public static partial int SetWindowRgn(nint hwnd, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(nint hwnd, out RECT rect);

    [LibraryImport("user32.dll")]
    public static partial nint SetWindowLongPtrW(nint hwnd, int index, nint value);

    [LibraryImport("user32.dll")]
    public static partial nint GetWindowLongPtrW(nint hwnd, int index);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromWindow(nint hwnd, uint flags);

    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromPoint(POINT pt, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfoW(nint monitor, ref MONITORINFO info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TrackMouseEvent(ref TRACKMOUSEEVENT tme);

    [LibraryImport("user32.dll")]
    public static partial nuint SetTimer(nint hwnd, nuint id, uint elapseMs, nint timerProc);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool KillTimer(nint hwnd, nuint id);

    [LibraryImport("user32.dll")]
    public static partial nint LoadImageW(nint instance, nint name, uint type, int cx, int cy, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "LoadImageW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint LoadImageFromFile(nint instance, string path, uint type, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    public static partial nint LoadCursorW(nint instance, nint name);

    [LibraryImport("user32.dll")]
    public static partial nint SetCursor(nint cursor);

    [LibraryImport("user32.dll")]
    public static partial nint SetCapture(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReleaseCapture();

    [LibraryImport("user32.dll")]
    public static partial nint GetCapture();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out POINT point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ScreenToClient(nint hwnd, ref POINT point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(nint icon);

    [LibraryImport("user32.dll")]
    public static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AppendMenuW(nint menu, uint flags, nuint idOrSubmenu, string? text);

    [LibraryImport("user32.dll")]
    public static partial int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint hwnd, nint tpmParams);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyMenu(nint menu);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsZoomed(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial int GetClassNameW(nint hwnd, char* buffer, int maxCount);

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterWindowMessageW(string name);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int MessageBoxW(nint hwnd, string text, string caption, uint type);

    [LibraryImport("user32.dll")]
    public static partial nint SetWinEventHook(uint eventMin, uint eventMax, nint module,
        delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void> callback,
        uint processId, uint threadId, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWinEvent(nint hook);

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetricsForDpi(int index, uint dpi);

    [LibraryImport("user32.dll")]
    public static partial nint SendMessageW(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint SendMessageString(nint hwnd, uint msg, nint wParam, string lParam);

    [LibraryImport("user32.dll")]
    public static partial nint SetFocus(nint hwnd);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowTextW(nint hwnd, string text);

    [LibraryImport("user32.dll")]
    public static partial int GetWindowTextW(nint hwnd, char* buffer, int maxCount);

    [LibraryImport("user32.dll")]
    public static partial int GetWindowTextLengthW(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool MoveWindow(nint hwnd, int x, int y, int width, int height, [MarshalAs(UnmanagedType.Bool)] bool repaint);

    // ── comctl32 (subclases) ──
    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowSubclass(nint hwnd, delegate* unmanaged<nint, uint, nint, nint, nuint, nuint, nint> proc, nuint id, nuint refData);

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RemoveWindowSubclass(nint hwnd, delegate* unmanaged<nint, uint, nint, nint, nuint, nuint, nint> proc, nuint id);

    [LibraryImport("comctl32.dll")]
    public static partial nint DefSubclassProc(nint hwnd, uint msg, nint wParam, nint lParam);

    // ── winmm ──
    public const uint SND_ASYNC = 0x0001, SND_NODEFAULT = 0x0002, SND_MEMORY = 0x0004;

    [LibraryImport("winmm.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PlaySoundW(nint sound, nint module, uint flags);

    // ── gdi32 ──
    [LibraryImport("gdi32.dll")]
    public static partial nint CreateSolidBrush(uint color);

    [LibraryImport("gdi32.dll")]
    public static partial uint SetTextColor(nint hdc, uint color);

    [LibraryImport("gdi32.dll")]
    public static partial uint SetBkColor(nint hdc, uint color);

    [LibraryImport("gdi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateFontW(int height, int width, int escapement, int orientation, int weight, uint italic,
        uint underline, uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string face);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateRectRgn(int left, int top, int right, int bottom);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(nint obj);

    // ── shell32 ──
    [LibraryImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATAW data);

    [LibraryImport("shell32.dll")]
    public static partial nuint SHAppBarMessage(uint message, ref APPBARDATA data);

    // ── ole32 ──
    public const uint CLSCTX_ALL = 0x17;
    public const uint CLSCTX_INPROC_SERVER = 0x1;

    [LibraryImport("ole32.dll")]
    public static partial int OleInitialize(nint reserved);

    [LibraryImport("ole32.dll")]
    public static partial void OleUninitialize();

    [LibraryImport("ole32.dll")]
    public static partial int RegisterDragDrop(nint hwnd, nint dropTarget);

    [LibraryImport("ole32.dll")]
    public static partial int RevokeDragDrop(nint hwnd);

    [LibraryImport("ole32.dll")]
    public static partial void ReleaseStgMedium(STGMEDIUM* medium);

    // ── Arrastrar y soltar, shell ──
    public const ushort CF_UNICODETEXT = 13;
    public const ushort CF_HDROP = 15;
    public const uint TYMED_HGLOBAL = 1;
    public const uint DVASPECT_CONTENT = 1;
    public const uint DROPEFFECT_NONE = 0, DROPEFFECT_COPY = 1, DROPEFFECT_MOVE = 2, DROPEFFECT_LINK = 4;
    public const int DRAGDROP_S_DROP = 0x00040100;

    [LibraryImport("shell32.dll")]
    public static partial uint DragQueryFileW(nint drop, uint index, char* buffer, uint size);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SHParseDisplayName(string name, nint bindContext, out nint pidl, uint sfgaoIn, out uint sfgaoOut);

    [LibraryImport("shell32.dll")]
    public static partial int SHCreateShellItemArrayFromIDLists(uint count, nint* pidls, out nint itemArray);

    [LibraryImport("shell32.dll")]
    public static partial int SHDoDragDrop(nint hwnd, nint dataObject, nint dropSource, uint effects, out uint effect);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SHCreateItemFromParsingName(string path, nint bindContext, in Guid iid, out nint item);

    [LibraryImport("shell32.dll")]
    public static partial void ILFree(nint pidl);

    // ── Portapapeles y memoria global ──
    public const uint GMEM_MOVEABLE = 0x0002;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenClipboard(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EmptyClipboard();

    [LibraryImport("user32.dll")]
    public static partial nint SetClipboardData(uint format, nint data);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseClipboard();

    [LibraryImport("kernel32.dll")]
    public static partial nint GlobalAlloc(uint flags, nuint bytes);

    [LibraryImport("kernel32.dll")]
    public static partial nint GlobalLock(nint memory);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GlobalUnlock(nint memory);

    [LibraryImport("kernel32.dll")]
    public static partial nuint GlobalSize(nint memory);

    [LibraryImport("kernel32.dll")]
    public static partial nint GlobalFree(nint memory);

    // ── Teclado y ratón ──
    public const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_DELETE = 0x2E;
    public const int SM_CXDRAG = 68, SM_CYDRAG = 69;

    [LibraryImport("user32.dll")]
    public static partial short GetKeyState(int key);

    [LibraryImport("user32.dll")]
    public static partial uint GetDoubleClickTime();

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetrics(int index);

    [LibraryImport("ole32.dll")]
    public static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);

    // ── kernel32 ──
    [LibraryImport("kernel32.dll")]
    public static partial nint GetModuleHandleW(nint moduleName);

    [LibraryImport("kernel32.dll")]
    public static partial ushort GetUserDefaultUILanguage();

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GetTimeFormatEx(string? locale, uint flags, in SYSTEMTIME time, string? format, char* buffer, int size);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GetDateFormatEx(string? locale, uint flags, in SYSTEMTIME date, string? format, char* buffer, int size, string? calendar);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GetLocaleInfoEx(string? locale, uint type, char* buffer, int size);

    // ── shcore / dwmapi ──
    [LibraryImport("shcore.dll")]
    public static partial int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(nint hwnd, uint attribute, void* value, uint size);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmGetWindowAttribute(nint hwnd, uint attribute, void* value, uint size);

    // ── Ayudas ──
    public static int LoWord(nint value) => (short)((long)value & 0xFFFF);

    public static int HiWord(nint value) => (short)(((long)value >> 16) & 0xFFFF);

    public static string GetClassName(nint hwnd)
    {
        char* buffer = stackalloc char[256];
        int n = GetClassNameW(hwnd, buffer, 256);
        return n > 0 ? new string(buffer, 0, n) : string.Empty;
    }
}
