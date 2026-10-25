using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace Tilde.Render;

/// <summary>Iconos propios de Tilde.</summary>
public enum IconKind
{
    Music,
    Timer,
    Water,
    Tasks,
    Shelf,
    Spark,
    Settings,
    Play,
    Pause,
    Previous,
    Next,
    Volume,
    VolumeMuted,
    Lyrics,
}

/// <summary>
/// Iconos vectoriales diseñados para Tilde, definidos en una rejilla de 24×24 con trazo
/// redondeado. Al ser geometría (no imágenes) se ven nítidos a cualquier tamaño y escala, y
/// no dependen de que haya una fuente de iconos instalada.
/// </summary>
public sealed class IconSet : IDisposable
{
    /// <summary>Grosor del trazo en unidades de la rejilla de 24.</summary>
    private const float StrokeWidth = 1.8f;

    private readonly Dictionary<IconKind, (ID2D1Geometry Geometry, bool Fill)[]> _icons = [];
    private readonly ID2D1StrokeStyle _stroke;

    public IconSet(ID2D1Factory1 factory)
    {
        _stroke = factory.CreateStrokeStyle(new StrokeStyleProperties1
        {
            StartCap = CapStyle.Round,
            EndCap = CapStyle.Round,
            DashCap = CapStyle.Round,
            LineJoin = LineJoin.Round,
            MiterLimit = 10f,
        });

        // Música: corchea.
        _icons[IconKind.Music] =
        [
            (Ellipse(factory, 9.4f, 17.3f, 3.1f, 2.5f), true),
            (Path(factory, p => p.Move(12.4f, 17f).Line(12.4f, 4.5f).Curve(14.6f, 5.9f, 17.6f, 6.3f, 18.6f, 9.6f)), false),
        ];

        // Pomodoro: cronómetro.
        _icons[IconKind.Timer] =
        [
            (Ellipse(factory, 12f, 13.6f, 7.2f, 7.2f), false),
            (Path(factory, p => p.Move(12f, 13.6f).Line(12f, 9.8f)
                                 .Move(9.6f, 3.6f).Line(14.4f, 3.6f)
                                 .Move(18.3f, 6.6f).Line(19.6f, 5.3f)), false),
        ];

        // Agua: gota con brillo.
        _icons[IconKind.Water] =
        [
            (Path(factory, p => p.Move(12f, 3.6f)
                                 .Curve(9.6f, 7f, 6.2f, 10.6f, 6.2f, 14.4f)
                                 .Arc(17.8f, 14.4f, 5.8f, clockwise: false)
                                 .Curve(17.8f, 10.6f, 14.4f, 7f, 12f, 3.6f).Close()), false),
            (Path(factory, p => p.Move(9.3f, 14.9f).Curve(9.3f, 16.3f, 10.3f, 17.3f, 11.6f, 17.5f)), false),
        ];

        // Tareas: lista con una casilla marcada.
        _icons[IconKind.Tasks] =
        [
            (Path(factory, p => p.Move(4f, 7.4f).Line(5.8f, 9.1f).Line(9f, 5.7f)
                                 .Move(12f, 7.4f).Line(20f, 7.4f)
                                 .Move(12f, 12.4f).Line(20f, 12.4f)
                                 .Move(12f, 17.4f).Line(20f, 17.4f)), false),
            (Ellipse(factory, 6.5f, 12.4f, 1.3f, 1.3f), true),
            (Ellipse(factory, 6.5f, 17.4f, 1.3f, 1.3f), true),
        ];

        // Estante: bandeja.
        _icons[IconKind.Shelf] =
        [
            (Path(factory, p => p.Move(3.6f, 12.6f).Line(6.7f, 5.6f).Line(17.3f, 5.6f).Line(20.4f, 12.6f)
                                 .Line(20.4f, 18.4f).Line(3.6f, 18.4f).Close()
                                 .Move(3.6f, 12.6f).Line(8.6f, 12.6f).Line(9.8f, 15f).Line(14.2f, 15f)
                                 .Line(15.4f, 12.6f).Line(20.4f, 12.6f)), false),
        ];

        // Claude: destello genérico de cuatro puntas (no es el logotipo de nadie).
        _icons[IconKind.Spark] =
        [
            (Path(factory, p => p.Move(12f, 3.6f)
                                 .Curve(12.6f, 8.6f, 15.4f, 11.4f, 20.4f, 12f)
                                 .Curve(15.4f, 12.6f, 12.6f, 15.4f, 12f, 20.4f)
                                 .Curve(11.4f, 15.4f, 8.6f, 12.6f, 3.6f, 12f)
                                 .Curve(8.6f, 11.4f, 11.4f, 8.6f, 12f, 3.6f).Close()), false),
            (Ellipse(factory, 18.6f, 5.4f, 1.2f, 1.2f), true),
        ];

        // Ajustes: deslizadores.
        _icons[IconKind.Settings] =
        [
            (Path(factory, p => p.Move(4f, 7f).Line(20f, 7f)
                                 .Move(4f, 12f).Line(20f, 12f)
                                 .Move(4f, 17f).Line(20f, 17f)), false),
            (Ellipse(factory, 15f, 7f, 2.3f, 2.3f), true),
            (Ellipse(factory, 9f, 12f, 2.3f, 2.3f), true),
            (Ellipse(factory, 13.5f, 17f, 2.3f, 2.3f), true),
        ];

        AddMediaIcons(factory);
    }

    private void AddMediaIcons(ID2D1Factory1 factory)
    {
        // Reproducir / pausa / anterior / siguiente: formas rellenas con esquinas suaves.
        _icons[IconKind.Play] =
        [
            (Filled(factory, p => p.Move(8f, 5.2f).Line(19f, 12f).Line(8f, 18.8f).Close()), true),
            (Path(factory, p => p.Move(8f, 5.2f).Line(19f, 12f).Line(8f, 18.8f).Close()), false),
        ];
        _icons[IconKind.Pause] =
        [
            (Rounded(factory, 6.5f, 5f, 4f, 14f, 1.4f), true),
            (Rounded(factory, 13.5f, 5f, 4f, 14f, 1.4f), true),
        ];
        _icons[IconKind.Previous] =
        [
            (Filled(factory, p => p.Move(18f, 6f).Line(9.5f, 12f).Line(18f, 18f).Close()), true),
            (Path(factory, p => p.Move(18f, 6f).Line(9.5f, 12f).Line(18f, 18f).Close().Move(6.5f, 6f).Line(6.5f, 18f)), false),
        ];
        _icons[IconKind.Next] =
        [
            (Filled(factory, p => p.Move(6f, 6f).Line(14.5f, 12f).Line(6f, 18f).Close()), true),
            (Path(factory, p => p.Move(6f, 6f).Line(14.5f, 12f).Line(6f, 18f).Close().Move(17.5f, 6f).Line(17.5f, 18f)), false),
        ];

        // Altavoz con ondas, y tachado para silencio.
        var speaker = Path(factory, p => p.Move(4.5f, 9.5f).Line(8f, 9.5f).Line(12f, 5.5f).Line(12f, 18.5f).Line(8f, 14.5f).Line(4.5f, 14.5f).Close());
        _icons[IconKind.Volume] =
        [
            (speaker, false),
            (Path(factory, p => p.Move(15.2f, 9.2f).Curve(16.6f, 10.8f, 16.6f, 13.2f, 15.2f, 14.8f)
                                 .Move(17.8f, 6.8f).Curve(20.6f, 9.8f, 20.6f, 14.2f, 17.8f, 17.2f)), false),
        ];
        _icons[IconKind.VolumeMuted] =
        [
            (Path(factory, p => p.Move(4.5f, 9.5f).Line(8f, 9.5f).Line(12f, 5.5f).Line(12f, 18.5f).Line(8f, 14.5f).Line(4.5f, 14.5f).Close()), false),
            (Path(factory, p => p.Move(15.5f, 9.5f).Line(20.5f, 14.5f).Move(20.5f, 9.5f).Line(15.5f, 14.5f)), false),
        ];

        // Letras: bocadillo con líneas de texto.
        _icons[IconKind.Lyrics] =
        [
            (Path(factory, p => p.Move(5f, 5f).Line(19f, 5f).Curve(19.8f, 5f, 20.4f, 5.6f, 20.4f, 6.4f).Line(20.4f, 15f)
                                 .Curve(20.4f, 15.8f, 19.8f, 16.4f, 19f, 16.4f).Line(10f, 16.4f).Line(6.5f, 19.5f).Line(6.5f, 16.4f)
                                 .Line(5f, 16.4f).Curve(4.2f, 16.4f, 3.6f, 15.8f, 3.6f, 15f).Line(3.6f, 6.4f)
                                 .Curve(3.6f, 5.6f, 4.2f, 5f, 5f, 5f).Close()
                                 .Move(7.5f, 9f).Line(16.5f, 9f).Move(7.5f, 12.4f).Line(13.5f, 12.4f)), false),
        ];
    }

    /// <summary>Dibuja un icono con su esquina superior izquierda en (x, y) y el lado indicado en DIP.</summary>
    public void Draw(ID2D1DeviceContext dc, IconKind kind, float x, float y, float size, ID2D1Brush brush)
    {
        var previous = dc.Transform;
        dc.Transform = Matrix3x2.CreateScale(size / 24f) * Matrix3x2.CreateTranslation(x, y) * previous;
        foreach (var (geometry, fill) in _icons[kind])
        {
            if (fill)
            {
                dc.FillGeometry(geometry, brush);
            }
            else
            {
                dc.DrawGeometry(geometry, brush, StrokeWidth, _stroke);
            }
        }
        dc.Transform = previous;
    }

    public void Dispose()
    {
        foreach (var parts in _icons.Values)
        {
            foreach (var (geometry, _) in parts)
            {
                geometry.Dispose();
            }
        }
        _stroke.Dispose();
    }

    private static ID2D1Geometry Ellipse(ID2D1Factory1 f, float cx, float cy, float rx, float ry) =>
        f.CreateEllipseGeometry(new Vortice.Direct2D1.Ellipse(new Vector2(cx, cy), rx, ry));

    private static ID2D1Geometry Rounded(ID2D1Factory1 f, float x, float y, float w, float h, float r) =>
        f.CreateRoundedRectangleGeometry(new RoundedRectangle(new Vortice.RawRectF(x, y, x + w, y + h), r, r));

    /// <summary>Trazado relleno (las figuras se abren como «Filled»).</summary>
    private static ID2D1Geometry Filled(ID2D1Factory1 f, Action<PathBuilder> build) => Path(f, build, filled: true);

    private static ID2D1Geometry Path(ID2D1Factory1 f, Action<PathBuilder> build, bool filled = false)
    {
        var geometry = f.CreatePathGeometry();
        using (var builder = new PathBuilder(geometry.Open(), filled))
        {
            build(builder);
        }
        return geometry;
    }

    /// <summary>Constructor de trazados al estilo SVG (Move, Line, Curve, Arc, Close).</summary>
    private sealed class PathBuilder(ID2D1GeometrySink sink, bool filled = false) : IDisposable
    {
        private bool _open;
        private Vector2 _last;

        public PathBuilder Move(float x, float y)
        {
            EndOpenFigure();
            sink.BeginFigure(new Vector2(x, y), filled ? FigureBegin.Filled : FigureBegin.Hollow);
            _open = true;
            _last = new Vector2(x, y);
            return this;
        }

        public PathBuilder Line(float x, float y)
        {
            sink.AddLine(_last = new Vector2(x, y));
            return this;
        }

        public PathBuilder Curve(float x1, float y1, float x2, float y2, float x, float y)
        {
            sink.AddBezier(new BezierSegment
            {
                Point1 = new Vector2(x1, y1),
                Point2 = new Vector2(x2, y2),
                Point3 = _last = new Vector2(x, y),
            });
            return this;
        }

        public PathBuilder Arc(float x, float y, float radius, bool clockwise)
        {
            sink.AddArc(new ArcSegment
            {
                Point = _last = new Vector2(x, y),
                Size = new Size(radius, radius),
                RotationAngle = 0,
                SweepDirection = clockwise ? SweepDirection.Clockwise : SweepDirection.CounterClockwise,
                ArcSize = ArcSize.Small,
            });
            return this;
        }

        public PathBuilder Close()
        {
            sink.EndFigure(FigureEnd.Closed);
            _open = false;
            return this;
        }

        public void Dispose()
        {
            EndOpenFigure();
            sink.Close();
            sink.Dispose();
        }

        private void EndOpenFigure()
        {
            if (_open)
            {
                sink.EndFigure(FigureEnd.Open);
                _open = false;
            }
        }
    }
}
