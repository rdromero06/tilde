using System.Numerics;
using Vortice.Direct2D1;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Tilde.Render;

/// <summary>
/// Una capa con contenido dibujado por nosotros: un visual de DirectComposition más una
/// superficie del tamaño exacto en píxeles físicos. Se dibuja en DIP con el DPI correcto,
/// así el texto y las formas salen nítidos a cualquier escala (100 %, 125 %, 150 %…).
/// Solo se vuelve a dibujar cuando su contenido cambia.
/// </summary>
public sealed class DrawingSurface : IDisposable
{
    private readonly GraphicsDevice _device;
    private IDCompositionSurface? _surface;

    public DrawingSurface(GraphicsDevice device)
    {
        _device = device;
        Visual = device.CreateVisual();
    }

    public IDCompositionVisual3 Visual { get; }

    public int PixelWidth { get; private set; }

    public int PixelHeight { get; private set; }

    /// <summary>Escala de pantalla (1 = 100 %, 1,5 = 150 %).</summary>
    public float Scale { get; private set; } = 1f;

    public float WidthDip => PixelWidth / Scale;

    public float HeightDip => PixelHeight / Scale;

    /// <summary>Ajusta el tamaño en DIP para una escala dada (recrea la superficie si cambia).</summary>
    public void Resize(float widthDip, float heightDip, float scale)
    {
        int pw = Math.Max(1, (int)MathF.Ceiling(widthDip * scale));
        int ph = Math.Max(1, (int)MathF.Ceiling(heightDip * scale));
        if (_surface is not null && pw == PixelWidth && ph == PixelHeight && scale == Scale)
        {
            return;
        }
        _surface?.Dispose();
        PixelWidth = pw;
        PixelHeight = ph;
        Scale = scale;
        _surface = _device.Composition.CreateSurface((uint)pw, (uint)ph, Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied);
        Visual.SetContent(_surface);
    }

    /// <summary>
    /// Dibuja el contenido completo. El pintor recibe un contexto en DIP con origen (0, 0)
    /// y el fondo ya borrado a transparente.
    /// </summary>
    public void Draw(Action<ID2D1DeviceContext> painter)
    {
        if (_surface is null)
        {
            return;
        }
        // DComp puede darnos un trozo de una textura compartida: hay que desplazar el dibujo.
        using var dc = _surface.BeginDraw<ID2D1DeviceContext>(null, out Int2 offset);
        try
        {
            dc.Dpi = new Size(96f * Scale, 96f * Scale);
            dc.Transform = Matrix3x2.CreateTranslation(offset.X / Scale, offset.Y / Scale);
            // Sobre fondo transparente no se puede usar ClearType: suavizado en escala de grises.
            dc.TextAntialiasMode = TextAntialiasMode.Grayscale;
            dc.PushAxisAlignedClip(new Vortice.RawRectF(0, 0, WidthDip, HeightDip), AntialiasMode.Aliased);
            dc.Clear(new Color4(0, 0, 0, 0));
            painter(dc);
            dc.PopAxisAlignedClip();
        }
        finally
        {
            _surface.EndDraw();
        }
    }

    /// <summary>Coloca la capa en una posición en DIP (se redondea a píxel entero para no emborronar).</summary>
    public void SetOffset(float xDip, float yDip)
    {
        Visual.SetOffsetX(MathF.Round(xDip * Scale));
        Visual.SetOffsetY(MathF.Round(yDip * Scale));
    }

    public void Dispose()
    {
        Visual.SetContent(null);
        _surface?.Dispose();
        Visual.Dispose();
    }
}
