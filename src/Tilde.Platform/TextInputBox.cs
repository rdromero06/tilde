using System.Runtime.InteropServices;
using Tilde.Platform.Interop;
using static Tilde.Platform.Interop.Native;

namespace Tilde.Platform;

/// <summary>
/// Campo de texto nativo que se coloca encima del notch cuando hace falta escribir.
///
/// La ventana del notch no tiene «superficie GDI» (todo lo dibuja DirectComposition), así que
/// un control EDIT hijo no se vería. Usamos una pequeña ventana emergente propia con un EDIT
/// de Windows dentro: así funcionan gratis el IME (acentos, chino, japonés…), el portapapeles,
/// deshacer, la selección y los lectores de pantalla. Intro acepta, Esc cancela y hacer clic
/// fuera también cancela.
/// </summary>
public sealed unsafe class TextInputBox : Win32Window
{
    private const uint Background = 0x001E1C1C;   // COLORREF (BGR): gris muy oscuro
    private const uint Foreground = 0x00F2F2F2;

    private readonly nint _brush;
    private nint _edit;
    private nint _font;
    private int _fontPx;
    private Action<string>? _onSubmit;
    private Action? _onClose;
    private GCHandle _self;

    public TextInputBox(nint owner)
    {
        _brush = CreateSolidBrush(Background);
        CreateWindow("TildeTextInput", WS_EX_TOOLWINDOW | WS_EX_TOPMOST, WS_POPUP, 0, 0, 10, 10, owner);
        int corner = 3; // DWMWCP_ROUNDSMALL: esquinas redondeadas en Windows 11.
        DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, &corner, sizeof(int));
        _edit = CreateWindowExW(0, "EDIT", string.Empty, WS_CHILD | WS_VISIBLE | ES_AUTOHSCROLL, 0, 0, 10, 10, Handle, 1, ModuleHandle, 0);
        SendMessageW(_edit, EM_LIMITTEXT, 300, 0);
        _self = GCHandle.Alloc(this);
        SetWindowSubclass(_edit, &EditProc, 1, (nuint)GCHandle.ToIntPtr(_self));
    }

    public bool IsOpen { get; private set; }

    /// <summary>
    /// Muestra el campo en <paramref name="screen"/> (píxeles de pantalla) con el texto y la
    /// sugerencia indicados. <paramref name="onSubmit"/> recibe el texto al pulsar Intro.
    /// </summary>
    public void Open(RECT screen, int fontPx, string placeholder, string text, Action<string> onSubmit, Action? onClose = null)
    {
        _onSubmit = onSubmit;
        _onClose = onClose;
        if (fontPx != _fontPx || _font == 0)
        {
            if (_font != 0) DeleteObject(_font);
            _fontPx = fontPx;
            // 5 = CLEARTYPE_QUALITY. El nombre se resuelve con la fuente de Windows 11 o la de 10.
            _font = CreateFontW(-fontPx, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI Variable Text");
            SendMessageW(_edit, WM_SETFONT, _font, 1);
        }
        int padding = Math.Max(6, fontPx / 2);
        SendMessageW(_edit, EM_SETMARGINS, 3, (nint)(padding | (padding << 16)));
        SendMessageString(_edit, EM_SETCUEBANNER, 1, placeholder);
        SetWindowTextW(_edit, text);

        int height = screen.Height;
        SetWindowPos(Handle, HWND_TOPMOST, screen.Left, screen.Top, screen.Width, height, 0x0040 /* SHOWWINDOW */);
        int editHeight = fontPx + fontPx / 2 + 4;
        MoveWindow(_edit, 0, (height - editHeight) / 2, screen.Width, editHeight, true);

        IsOpen = true;
        SetForegroundWindow(Handle);
        SetFocus(_edit);
        SendMessageW(_edit, EM_SETSEL, 0, -1);
    }

    /// <summary>Cierra el campo (enviando el texto si <paramref name="submit"/>).</summary>
    public void Close(bool submit)
    {
        if (!IsOpen)
        {
            return;
        }
        IsOpen = false;
        string text = submit ? ReadText() : string.Empty;
        ShowWindow(Handle, SW_HIDE);
        var onSubmit = _onSubmit;
        var onClose = _onClose;
        _onSubmit = null;
        _onClose = null;
        if (submit && text.Trim().Length > 0)
        {
            onSubmit?.Invoke(text.Trim());
        }
        onClose?.Invoke();
    }

#if DEBUG
    /// <summary>Solo depuración: escribe un texto y pulsa Intro (pruebas automáticas).</summary>
    public void DebugSubmit(string text)
    {
        SetWindowTextW(_edit, text);
        Close(submit: true);
    }
#endif

    public override void Dispose()
    {
        RemoveWindowSubclass(_edit, &EditProc, 1);
        if (_self.IsAllocated) _self.Free();
        if (_font != 0) DeleteObject(_font);
        DeleteObject(_brush);
        base.Dispose();
    }

    protected override nint WndProc(uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_CTLCOLOREDIT:
                SetTextColor(wParam, Foreground);
                SetBkColor(wParam, Background);
                return _brush;
            case WM_ACTIVATE when LoWord(wParam) == 0: // WA_INACTIVE: clic fuera
                Close(submit: false);
                return 0;
            case WM_SETFOCUS:
                SetFocus(_edit);
                return 0;
        }
        return base.WndProc(msg, wParam, lParam);
    }

    private string ReadText()
    {
        int length = GetWindowTextLengthW(_edit);
        if (length <= 0)
        {
            return string.Empty;
        }
        var buffer = new char[length + 1];
        fixed (char* p = buffer)
        {
            int n = GetWindowTextW(_edit, p, buffer.Length);
            return new string(p, 0, n);
        }
    }

    [UnmanagedCallersOnly]
    private static nint EditProc(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nuint refData)
    {
        try
        {
            var self = (TextInputBox?)GCHandle.FromIntPtr((nint)refData).Target;
            if (self is not null)
            {
                if (msg == WM_KEYDOWN && (int)wParam == VK_RETURN)
                {
                    self.Close(submit: true);
                    return 0;
                }
                if (msg == WM_KEYDOWN && (int)wParam == VK_ESCAPE)
                {
                    self.Close(submit: false);
                    return 0;
                }
                // Intro y Esc también generan un carácter: lo descartamos para evitar el pitido.
                if (msg == WM_CHAR && (wParam == '\r' || wParam == 27))
                {
                    return 0;
                }
            }
        }
        catch (Exception ex)
        {
            Tilde.Core.Log.Error("Error en el campo de texto", ex);
        }
        return DefSubclassProc(hwnd, msg, wParam, lParam);
    }
}
