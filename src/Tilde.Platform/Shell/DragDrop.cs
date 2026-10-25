using System.Runtime.InteropServices;
using Tilde.Core;
using Tilde.Platform.Interop;
using static Tilde.Platform.Interop.Native;

namespace Tilde.Platform.Shell;

/// <summary>Lo que trae un arrastre: rutas de archivos/carpetas o texto.</summary>
public sealed record DroppedData(IReadOnlyList<string> Paths, string? Text)
{
    public bool IsEmpty => Paths.Count == 0 && string.IsNullOrWhiteSpace(Text);
}

/// <summary>
/// Destino de «arrastrar y soltar» para una ventana (IDropTarget de OLE, implementado a mano
/// para Native AOT). También usa el ayudante del shell para que la imagen del arrastre de
/// Explorer se siga viendo encima del notch.
/// </summary>
public sealed unsafe class DropTarget : IDisposable
{
    private static readonly Guid IidUnknown = new("00000000-0000-0000-C000-000000000046");
    private static readonly Guid IidDropTarget = new("00000122-0000-0000-C000-000000000046");
    private static readonly Guid ClsidDragDropHelper = new("4657278A-411B-11D2-839A-00C04FD918D0");
    private static readonly Guid IidDropTargetHelper = new("4657278B-411B-11D2-839A-00C04FD918D0");

    private static DropTarget? s_instance;
    private readonly nint _hwnd;
    private readonly nint _object;
    private readonly nint _helper;
    private bool _accepting;

    public DropTarget(nint hwnd)
    {
        _hwnd = hwnd;
        s_instance = this;
        var vtbl = (nint*)NativeMemory.Alloc((nuint)(sizeof(nint) * 7));
        vtbl[0] = (nint)(delegate* unmanaged<nint, Guid*, nint*, int>)&QueryInterface;
        vtbl[1] = (nint)(delegate* unmanaged<nint, uint>)&AddRef;
        vtbl[2] = (nint)(delegate* unmanaged<nint, uint>)&Release;
        vtbl[3] = (nint)(delegate* unmanaged<nint, nint, uint, POINT, uint*, int>)&OnDragEnter;
        vtbl[4] = (nint)(delegate* unmanaged<nint, uint, POINT, uint*, int>)&OnDragOver;
        vtbl[5] = (nint)(delegate* unmanaged<nint, int>)&OnDragLeave;
        vtbl[6] = (nint)(delegate* unmanaged<nint, nint, uint, POINT, uint*, int>)&OnDrop;
        _object = (nint)NativeMemory.Alloc((nuint)sizeof(nint));
        *(nint*)_object = (nint)vtbl;

        if (CoCreateInstance(ClsidDragDropHelper, 0, CLSCTX_INPROC_SERVER, IidDropTargetHelper, out _helper) < 0)
        {
            _helper = 0;
        }
        int hr = RegisterDragDrop(hwnd, _object);
        if (hr < 0)
        {
            Log.Warn($"No se pudo registrar el destino de arrastre (0x{hr:X8})");
        }
    }

    /// <summary>Empieza un arrastre sobre la ventana. Devuelve true si se acepta (contiene archivos o texto).</summary>
    public Func<DroppedData, bool>? Entered { get; set; }

    /// <summary>El arrastre ha salido de la ventana sin soltar.</summary>
    public Action? Left { get; set; }

    /// <summary>Se ha soltado sobre la ventana.</summary>
    public Action<DroppedData>? Dropped { get; set; }

    public void Dispose()
    {
        RevokeDragDrop(_hwnd);
        if (_helper != 0)
        {
            ((delegate* unmanaged<nint, uint>)(*(nint**)_helper)[2])(_helper);
        }
        if (s_instance == this)
        {
            s_instance = null;
        }
    }

    /// <summary>Lee archivos (CF_HDROP) y texto (CF_UNICODETEXT) de un IDataObject.</summary>
    public static DroppedData Read(nint dataObject)
    {
        var paths = new List<string>();
        string? text = null;
        var vtbl = *(nint**)dataObject;
        var getData = (delegate* unmanaged<nint, FORMATETC*, STGMEDIUM*, int>)vtbl[3];

        var format = new FORMATETC { cfFormat = CF_HDROP, dwAspect = DVASPECT_CONTENT, lindex = -1, tymed = TYMED_HGLOBAL };
        STGMEDIUM medium;
        if (getData(dataObject, &format, &medium) >= 0)
        {
            uint count = DragQueryFileW(medium.handle, 0xFFFFFFFF, null, 0);
            char* buffer = stackalloc char[1024];
            for (uint i = 0; i < count; i++)
            {
                uint n = DragQueryFileW(medium.handle, i, buffer, 1024);
                if (n > 0) paths.Add(new string(buffer, 0, (int)n));
            }
            ReleaseStgMedium(&medium);
        }

        format.cfFormat = CF_UNICODETEXT;
        if (paths.Count == 0 && getData(dataObject, &format, &medium) >= 0)
        {
            var ptr = GlobalLock(medium.handle);
            if (ptr != 0)
            {
                int max = (int)(GlobalSize(medium.handle) / 2);
                text = new string((char*)ptr, 0, max).Split('\0')[0];
                GlobalUnlock(medium.handle);
            }
            ReleaseStgMedium(&medium);
        }
        return new DroppedData(paths, text);
    }

    private void HelperEnter(nint dataObject, POINT pt, uint effect)
    {
        if (_helper != 0)
            ((delegate* unmanaged<nint, nint, nint, POINT*, uint, int>)(*(nint**)_helper)[3])(_helper, _hwnd, dataObject, &pt, effect);
    }

    private void HelperOver(POINT pt, uint effect)
    {
        if (_helper != 0)
            ((delegate* unmanaged<nint, POINT*, uint, int>)(*(nint**)_helper)[5])(_helper, &pt, effect);
    }

    private void HelperLeave()
    {
        if (_helper != 0)
            ((delegate* unmanaged<nint, int>)(*(nint**)_helper)[4])(_helper);
    }

    private void HelperDrop(nint dataObject, POINT pt, uint effect)
    {
        if (_helper != 0)
            ((delegate* unmanaged<nint, nint, POINT*, uint, int>)(*(nint**)_helper)[6])(_helper, dataObject, &pt, effect);
    }

    // ── IDropTarget ──

    [UnmanagedCallersOnly]
    private static int QueryInterface(nint self, Guid* iid, nint* result)
    {
        if (*iid == IidUnknown || *iid == IidDropTarget)
        {
            *result = self;
            return 0;
        }
        *result = 0;
        return unchecked((int)0x80004002);
    }

    [UnmanagedCallersOnly]
    private static uint AddRef(nint self) => 1;

    [UnmanagedCallersOnly]
    private static uint Release(nint self) => 1;

    [UnmanagedCallersOnly]
    private static int OnDragEnter(nint self, nint dataObject, uint keys, POINT pt, uint* effect)
    {
        try
        {
            if (s_instance is { } t)
            {
                var data = Read(dataObject);
                t._accepting = !data.IsEmpty && (t.Entered?.Invoke(data) ?? false);
                *effect = t._accepting ? DROPEFFECT_COPY : DROPEFFECT_NONE;
                t.HelperEnter(dataObject, pt, *effect);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Error al empezar a soltar", ex);
            *effect = DROPEFFECT_NONE;
        }
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int OnDragOver(nint self, uint keys, POINT pt, uint* effect)
    {
        var t = s_instance;
        *effect = t is { _accepting: true } ? DROPEFFECT_COPY : DROPEFFECT_NONE;
        t?.HelperOver(pt, *effect);
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int OnDragLeave(nint self)
    {
        try
        {
            if (s_instance is { } t)
            {
                t.HelperLeave();
                t._accepting = false;
                t.Left?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Error al salir del arrastre", ex);
        }
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int OnDrop(nint self, nint dataObject, uint keys, POINT pt, uint* effect)
    {
        try
        {
            if (s_instance is { } t)
            {
                // Respondemos «copiar»: guardamos la ruta y el origen nunca borra nada.
                *effect = t._accepting ? DROPEFFECT_COPY : DROPEFFECT_NONE;
                t.HelperDrop(dataObject, pt, *effect);
                if (t._accepting)
                {
                    t.Dropped?.Invoke(Read(dataObject));
                }
                t._accepting = false;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Error al soltar", ex);
            *effect = DROPEFFECT_NONE;
        }
        return 0;
    }
}

/// <summary>Arrastrar archivos desde Tilde hacia otras apps.</summary>
public static unsafe class FileDrag
{
    private static readonly Guid BhidDataObject = new("B8C0BD9F-ED24-455C-83E6-D5390C4FE8C4");
    private static readonly Guid IidDataObject = new("0000010E-0000-0000-C000-000000000046");

    /// <summary>
    /// Arrastra estos archivos (bloquea hasta soltar o cancelar, mientras Windows sigue
    /// atendiendo mensajes). Se ofrece copiar o crear acceso directo, nunca mover, para que
    /// sacar algo del estante jamás cambie de sitio el original. Devuelve true si se soltó.
    /// </summary>
    public static bool Start(nint hwnd, IReadOnlyList<string> paths)
    {
        var pidls = new List<nint>();
        nint array = 0, dataObject = 0;
        try
        {
            foreach (var path in paths)
            {
                if (SHParseDisplayName(path, 0, out var pidl, 0, out _) >= 0)
                {
                    pidls.Add(pidl);
                }
            }
            if (pidls.Count == 0)
            {
                return false;
            }
            var raw = pidls.ToArray();
            fixed (nint* p = raw)
            {
                if (SHCreateShellItemArrayFromIDLists((uint)raw.Length, p, out array) < 0)
                {
                    return false;
                }
            }
            // IShellItemArray::BindToHandler(BHID_DataObject) → IDataObject con todos los formatos del shell.
            Guid bhid = BhidDataObject, iid = IidDataObject;
            int hr = ((delegate* unmanaged<nint, nint, Guid*, Guid*, nint*, int>)(*(nint**)array)[3])(array, 0, &bhid, &iid, &dataObject);
            if (hr < 0)
            {
                return false;
            }
            // Sin IDropSource propio: SHDoDragDrop usa el del shell, con miniaturas de arrastre.
            hr = SHDoDragDrop(hwnd, dataObject, 0, DROPEFFECT_COPY | DROPEFFECT_LINK, out _);
            return hr == DRAGDROP_S_DROP;
        }
        catch (Exception ex)
        {
            Log.Error("Error al arrastrar archivos", ex);
            return false;
        }
        finally
        {
            if (dataObject != 0) ((delegate* unmanaged<nint, uint>)(*(nint**)dataObject)[2])(dataObject);
            if (array != 0) ((delegate* unmanaged<nint, uint>)(*(nint**)array)[2])(array);
            foreach (var pidl in pidls) ILFree(pidl);
        }
    }
}

/// <summary>Portapapeles y otras ayudas del sistema.</summary>
public static unsafe class SystemActions
{
    public static bool CopyText(nint hwnd, string text)
    {
        if (!OpenClipboard(hwnd))
        {
            return false;
        }
        try
        {
            EmptyClipboard();
            nuint bytes = (nuint)((text.Length + 1) * 2);
            nint memory = GlobalAlloc(GMEM_MOVEABLE, bytes);
            var target = (char*)GlobalLock(memory);
            text.AsSpan().CopyTo(new Span<char>(target, text.Length));
            target[text.Length] = '\0';
            GlobalUnlock(memory);
            if (SetClipboardData(CF_UNICODETEXT, memory) == 0)
            {
                GlobalFree(memory); // Si Windows no se la queda, la liberamos nosotros.
                return false;
            }
            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>Abre un archivo o carpeta con su aplicación predeterminada.</summary>
    public static void Open(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn($"No se pudo abrir el elemento ({ex.Message})");
        }
    }

    public static bool IsControlDown => (GetKeyState(VK_CONTROL) & 0x8000) != 0;

    public static bool IsShiftDown => (GetKeyState(VK_SHIFT) & 0x8000) != 0;
}

/// <summary>Iconos y miniaturas del shell (lo mismo que muestra el Explorador).</summary>
public static unsafe class ShellImages
{
    private static readonly Guid IidImageFactory = new("BCC18B79-BA16-442F-80C4-8A59C30C463B");

    /// <summary>
    /// Devuelve un HBITMAP de 32 bits con la miniatura (imágenes, vídeos, PDF…) o el icono del
    /// archivo. Puede tardar (lee el disco): llamar fuera del hilo de interfaz. El llamante
    /// libera el HBITMAP con DeleteObject.
    /// </summary>
    public static nint GetImage(string path, int sizePx)
    {
        if (SHCreateItemFromParsingName(path, 0, IidImageFactory, out var factory) < 0)
        {
            return 0;
        }
        try
        {
            nint bitmap;
            var size = new SIZE { cx = sizePx, cy = sizePx };
            // IShellItemImageFactory::GetImage (vtable 3). SIIGBF_RESIZETOFIT = 0.
            int hr = ((delegate* unmanaged<nint, SIZE, int, nint*, int>)(*(nint**)factory)[3])(factory, size, 0, &bitmap);
            return hr >= 0 ? bitmap : 0;
        }
        finally
        {
            ((delegate* unmanaged<nint, uint>)(*(nint**)factory)[2])(factory);
        }
    }
}
