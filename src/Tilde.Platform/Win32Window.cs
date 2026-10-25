using System.Runtime.InteropServices;
using Tilde.Core;
using Tilde.Platform.Interop;
using static Tilde.Platform.Interop.Native;

namespace Tilde.Platform;

/// <summary>
/// Base para ventanas Win32 nativas. Registra la clase de ventana y enruta los mensajes al
/// método virtual <see cref="WndProc"/> de la instancia. El procedimiento estático usa un
/// puntero a función [UnmanagedCallersOnly], compatible con Native AOT.
/// </summary>
public abstract unsafe class Win32Window : IDisposable
{
    private static readonly HashSet<string> RegisteredClasses = [];
    private GCHandle _self;

    public nint Handle { get; private set; }

    protected static nint ModuleHandle { get; } = GetModuleHandleW(0);

    protected void CreateWindow(string className, uint exStyle, uint style, int x, int y, int width, int height, nint parent = 0)
    {
        if (RegisteredClasses.Add(className))
        {
            fixed (char* name = className)
            {
                var wc = new WNDCLASSEXW
                {
                    cbSize = (uint)sizeof(WNDCLASSEXW),
                    lpfnWndProc = &StaticWndProc,
                    hInstance = ModuleHandle,
                    hCursor = LoadCursorW(0, IDC_ARROW),
                    lpszClassName = name,
                };
                if (RegisterClassExW(wc) == 0)
                {
                    throw new InvalidOperationException($"RegisterClassEx falló ({Marshal.GetLastPInvokeError()})");
                }
            }
        }

        _self = GCHandle.Alloc(this);
        Handle = CreateWindowExW(exStyle, className, "Tilde", style, x, y, width, height, parent, 0, ModuleHandle, GCHandle.ToIntPtr(_self));
        if (Handle == 0)
        {
            throw new InvalidOperationException($"CreateWindowEx falló ({Marshal.GetLastPInvokeError()})");
        }
    }

    /// <summary>Procesa un mensaje. Por defecto delega en DefWindowProc.</summary>
    protected virtual nint WndProc(uint msg, nint wParam, nint lParam) => DefWindowProcW(Handle, msg, wParam, lParam);

    public virtual void Dispose()
    {
        if (Handle != 0)
        {
            DestroyWindow(Handle);
            Handle = 0;
        }
        if (_self.IsAllocated)
        {
            _self.Free();
        }
        GC.SuppressFinalize(this);
    }

    [UnmanagedCallersOnly]
    private static nint StaticWndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            Win32Window? self;
            if (msg == WM_NCCREATE)
            {
                // lParam apunta a CREATESTRUCTW; su primer campo es lpCreateParams (nuestro GCHandle).
                nint param = *(nint*)lParam;
                SetWindowLongPtrW(hwnd, GWLP_USERDATA, param);
                self = (Win32Window?)GCHandle.FromIntPtr(param).Target;
                if (self is not null)
                {
                    self.Handle = hwnd;
                }
            }
            else
            {
                nint ptr = GetWindowLongPtrW(hwnd, GWLP_USERDATA);
                self = ptr != 0 ? (Win32Window?)GCHandle.FromIntPtr(ptr).Target : null;
            }

            if (self is not null)
            {
                return self.WndProc(msg, wParam, lParam);
            }
        }
        catch (Exception ex)
        {
            // Una excepción que cruce a código nativo cerraría el proceso: la registramos y seguimos.
            Log.Error($"Error procesando el mensaje 0x{msg:X4}", ex);
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}
