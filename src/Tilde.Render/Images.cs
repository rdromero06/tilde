using System.Numerics;
using Tilde.Core;
using Vortice.Direct2D1;
using Vortice.WIC;

namespace Tilde.Render;

/// <summary>Decodificación y dibujo de imágenes (carátulas).</summary>
public static class Images
{
    private static IWICImagingFactory? s_wic;

    /// <summary>
    /// Decodifica una imagen (JPEG, PNG…) y la reduce a <paramref name="maxPixels"/> de lado.
    /// Reducirla aquí ahorra memoria: una carátula de 1000×1000 son 4 MB; a 192 px, 150 KB.
    /// </summary>
    public static ID2D1Bitmap1? Decode(GraphicsDevice device, byte[] data, int maxPixels)
    {
        try
        {
            s_wic ??= new IWICImagingFactory();
            using var stream = s_wic.CreateStream();
            stream.Initialize(data);
            using var decoder = s_wic.CreateDecoderFromStream(stream, DecodeOptions.CacheOnLoad);
            using var frame = decoder.GetFrame(0);
            var size = frame.Size;
            float k = Math.Min(1f, maxPixels / (float)Math.Max(size.Width, size.Height));
            using var scaler = s_wic.CreateBitmapScaler();
            scaler.Initialize(frame, (uint)Math.Max(1, size.Width * k), (uint)Math.Max(1, size.Height * k), Vortice.WIC.BitmapInterpolationMode.HighQualityCubic);
            using var converter = s_wic.CreateFormatConverter();
            converter.Initialize(scaler, PixelFormat.Format32bppPBGRA);
            return device.ResourceContext.CreateBitmapFromWicBitmap(converter, null);
        }
        catch (Exception ex)
        {
            Log.Warn($"No se pudo decodificar la imagen ({ex.Message})");
            return null;
        }
    }

    /// <summary>Dibuja la imagen recortada con esquinas redondeadas, rellenando el rectángulo.</summary>
    public static void DrawRounded(ID2D1DeviceContext dc, ID2D1Bitmap bitmap, RectF rect, float radius)
    {
        var size = bitmap.Size;
        // Rellenar como «cover»: escalamos al lado mayor y centramos.
        float k = Math.Max(rect.W / size.Width, rect.H / size.Height);
        float dx = rect.X + (rect.W - size.Width * k) / 2;
        float dy = rect.Y + (rect.H - size.Height * k) / 2;
        using var brush = dc.CreateBitmapBrush(bitmap,
            new BitmapBrushProperties1
            {
                ExtendModeX = ExtendMode.Clamp,
                ExtendModeY = ExtendMode.Clamp,
                InterpolationMode = InterpolationMode.HighQualityCubic,
            },
            new BrushProperties
            {
                Opacity = 1f,
                Transform = Matrix3x2.CreateScale(k) * Matrix3x2.CreateTranslation(dx, dy),
            });
        dc.FillRoundedRectangle(new RoundedRectangle(new Vortice.RawRectF(rect.X, rect.Y, rect.Right, rect.Bottom), radius, radius), brush);
    }
}
