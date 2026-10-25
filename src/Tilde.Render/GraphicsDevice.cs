using System.Runtime.InteropServices;
using Tilde.Core;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DirectWrite;
using Vortice.DXGI;
using FeatureLevel = Vortice.Direct3D.FeatureLevel;

namespace Tilde.Render;

/// <summary>
/// Agrupa los dispositivos gráficos: Direct3D 11 (la GPU), Direct2D (dibujo 2D), DirectWrite
/// (texto) y DirectComposition (árbol de capas que compone el gestor de ventanas de Windows).
///
/// Por qué DirectComposition: las animaciones que le entregamos las ejecuta el compositor del
/// sistema (DWM) a la frecuencia del monitor. Nuestro proceso solo dibuja cuando cambia el
/// contenido (p. ej. una vez por minuto el reloj) y el resto del tiempo duerme: 0 % de CPU.
/// </summary>
public sealed partial class GraphicsDevice : IDisposable
{
    public GraphicsDevice()
    {
        // Las fábricas no dependen de la GPU: se crean una vez y sobreviven a un «device lost».
        D2DFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>(Vortice.Direct2D1.FactoryType.SingleThreaded, DebugLevel.None);
        DWrite = Vortice.DirectWrite.DWrite.DWriteCreateFactory<IDWriteFactory>(Vortice.DirectWrite.FactoryType.Shared);
        CreateDeviceResources();
    }

    public ID2D1Factory1 D2DFactory { get; }

    public IDWriteFactory DWrite { get; }

    public ID3D11Device D3D { get; private set; } = null!;

    /// <summary>true si dibujamos con WARP (CPU) en lugar de la GPU.</summary>
    public bool IsSoftware { get; private set; }

    public ID2D1Device D2D { get; private set; } = null!;

    /// <summary>Contexto auxiliar para crear recursos (pinceles) compartidos por todas las superficies.</summary>
    public ID2D1DeviceContext ResourceContext { get; private set; } = null!;

    public IDCompositionDesktopDevice Composition { get; private set; } = null!;

    /// <summary>
    /// true si la GPU se ha «perdido» (actualización de drivers, cambio de adaptador…). En ese
    /// caso hay que llamar a <see cref="Recreate"/> y reconstruir todas las capas.
    /// </summary>
    public bool IsDeviceLost
    {
        get
        {
            try { return D3D.DeviceRemovedReason.Failure; }
            catch { return true; }
        }
    }

    /// <summary>Crea una capa (visual) con soporte de opacidad animable.</summary>
    public IDCompositionVisual3 CreateVisual()
    {
        using var visual = Composition.CreateVisual();
        return visual.QueryInterface<IDCompositionVisual3>();
    }

    /// <summary>Crea el destino de composición de una ventana (solo puede haber uno por ventana).</summary>
    public IDCompositionTarget CreateTarget(nint hwnd)
    {
        Composition.CreateTargetForHwnd(hwnd, true, out var target).CheckError();
        return target;
    }

    /// <summary>Envía al compositor todos los cambios pendientes de una vez.</summary>
    public void Commit() => Composition.Commit();

    /// <summary>Vuelve a crear los recursos de GPU tras perder el dispositivo.</summary>
    public void Recreate()
    {
        Log.Warn("Dispositivo gráfico perdido: se recrea");
        ReleaseDeviceResources();
        CreateDeviceResources();
    }

    public void Dispose()
    {
        ReleaseDeviceResources();
        DWrite.Dispose();
        D2DFactory.Dispose();
    }

    private void CreateDeviceResources()
    {
        FeatureLevel[] levels =
        [
            FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1,
            FeatureLevel.Level_10_0, FeatureLevel.Level_9_3,
        ];
        // BgraSupport es obligatorio para que Direct2D pueda dibujar sobre texturas de D3D.
        const DeviceCreationFlags flags = DeviceCreationFlags.BgraSupport;
        // Por defecto usamos WARP, el rasterizador de Direct3D por software. Parece contraintuitivo,
        // pero es lo más eficiente para Tilde: crear un dispositivo en la GPU hace que el driver
        // reserve ~45 MB en nuestro proceso, y nosotros apenas dibujamos (el reloj, una vez por
        // minuto). Las animaciones no cambian: las sigue ejecutando DWM en la GPU.
        // TILDE_RENDERER=gpu fuerza la GPU (el selector llegará a Ajustes en la fase 7).
        bool preferGpu = string.Equals(Environment.GetEnvironmentVariable("TILDE_RENDERER"), "gpu", StringComparison.OrdinalIgnoreCase);
        DriverType first = preferGpu ? DriverType.Hardware : DriverType.Warp;
        DriverType second = preferGpu ? DriverType.Warp : DriverType.Hardware;
        DriverType used = first;
        if (D3D11.D3D11CreateDevice(IntPtr.Zero, first, flags, levels, out ID3D11Device? device).Failure)
        {
            used = second;
            D3D11.D3D11CreateDevice(IntPtr.Zero, second, flags, levels, out device).CheckError();
        }
        IsSoftware = used == DriverType.Warp;
        Log.Info($"Renderizador: {(IsSoftware ? "WARP (software)" : "GPU")}");
        D3D = device!;

        using var dxgi = D3D.QueryInterface<IDXGIDevice>();
        D2D = D2DFactory.CreateDevice(dxgi);
        ResourceContext = D2D.CreateDeviceContext(DeviceContextOptions.None);
        // Creando DComp con el dispositivo D2D, cada superficie nos da directamente un contexto D2D.
        Composition = CreateDesktopDevice(D2D);
    }

    // Vortice restringe el genérico DCompositionCreateDevice2<T> a IDCompositionDevice (v1), y
    // el dispositivo «de escritorio» deriva de IDCompositionDevice2: lo pedimos a mano.
    private static IDCompositionDesktopDevice CreateDesktopDevice(ID2D1Device d2d)
    {
        var iid = new Guid("5F4633FE-1E08-4CB8-8C75-CE24333F5602"); // IID_IDCompositionDesktopDevice
        Marshal.ThrowExceptionForHR(DCompositionCreateDevice2(d2d.NativePointer, iid, out nint device));
        return new IDCompositionDesktopDevice(device);
    }

    [LibraryImport("dcomp.dll")]
    private static partial int DCompositionCreateDevice2(nint renderingDevice, in Guid iid, out nint device);

    private void ReleaseDeviceResources()
    {
        Composition?.Dispose();
        ResourceContext?.Dispose();
        D2D?.Dispose();
        D3D?.Dispose();
    }
}
