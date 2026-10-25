using Tilde.App.Pages;
using Tilde.Core.Animation;
using Tilde.Platform.Interop;
using Tilde.Render;
using Vortice.Direct2D1;
using Vortice.DirectComposition;
using Vortice.DirectWrite;

namespace Tilde.App.Notch;

/// <summary>
/// Árbol de capas (visuales) del notch y sus animaciones:
/// <code>
/// raíz
/// └─ fondo negro, recortado por un rectángulo redondeado animable (la «forma» del notch)
///    ├─ píldora cerrada: reloj
///    └─ contenido abierto: cabecera (fecha y hora) · páginas · barra de puntos/iconos
/// </code>
/// Abrir o cerrar el notch no redibuja nada: solo anima el recorte y la opacidad de las
/// capas, y esas animaciones las ejecuta el compositor de Windows.
/// </summary>
internal sealed class NotchView : IDisposable
{
    public const int HitNone = -1;
    public const int HitSettings = -2;

    private const double ExpandDuration = 0.42;
    private const double CollapseDuration = 0.30;

    private readonly GraphicsDevice _g;
    private readonly IDCompositionDesktopDevice _dev;
    private readonly IDCompositionTarget _target;
    private readonly NotchGeometry _geo;
    private readonly Theme _theme;
    private readonly IReadOnlyList<Page> _pages;

    private readonly IDCompositionVisual3 _root;
    private readonly DrawingSurface _fill;
    private readonly IDCompositionRectangleClip _clip;
    private readonly AnimatedValue _left;
    private readonly AnimatedValue _right;
    private readonly AnimatedValue _bottom;
    private readonly AnimatedValue _radius;

    private readonly DrawingSurface _clock;
    private readonly AnimatedValue _clockOpacity;

    private readonly IDCompositionVisual3 _expanded;
    private readonly AnimatedValue _expandedOpacity;
    private readonly AnimatedValue _expandedOffsetY;
    private readonly DrawingSurface _header;
    private readonly IDCompositionVisual3 _pageHost;
    private readonly PageSlot?[] _slots;
    private readonly DrawingSurface _dots;
    private readonly DrawingSurface _icons;
    private readonly AnimatedValue _dotsOpacity;
    private readonly AnimatedValue _iconsOpacity;

    private string _time = string.Empty;
    private string _date = string.Empty;
    private float _collapsedWidth;
    private int _active;
    private bool _isExpanded;
    private bool _barHover;

    public NotchView(GraphicsDevice g, IDCompositionTarget target, NotchGeometry geo, Theme theme,
        IReadOnlyList<Page> pages, int activePage, bool expanded)
    {
        _g = g;
        _dev = g.Composition;
        _target = target;
        _geo = geo;
        _theme = theme;
        _pages = pages;
        _active = activePage;
        _isExpanded = expanded;
        _slots = new PageSlot?[pages.Count];
        float s = geo.Scale;

        _root = g.CreateVisual();

        // Fondo negro del tamaño de la ventana; el recorte le da la forma.
        _fill = new DrawingSurface(g);
        _fill.Resize(geo.WindowWidthDip, geo.WindowHeightDip, s);
        _fill.Draw(dc => dc.FillRectangle(new Vortice.RawRectF(0, 0, geo.WindowWidthDip, geo.WindowHeightDip), theme.Fill));
        _fill.Visual.SetBorderMode(Vortice.DirectComposition.BorderMode.Soft); // Bordes del recorte suavizados (antialias).
        _clip = _dev.CreateRectangleClip();
        _clip.SetTop(0);
        _fill.Visual.SetClip(_clip);
        _root.AddVisual(_fill.Visual, false, null);

        _collapsedWidth = geo.CollapsedWidthDip(0);
        var shape = expanded ? ExpandedShape() : CollapsedShape();
        _left = new AnimatedValue(v => _clip.SetLeft(v), a => _clip.SetLeft(a), shape.Left);
        _right = new AnimatedValue(v => _clip.SetRight(v), a => _clip.SetRight(a), shape.Right);
        _bottom = new AnimatedValue(v => _clip.SetBottom(v), a => _clip.SetBottom(a), shape.Bottom);
        _radius = new AnimatedValue(SetRadius, SetRadius, shape.Radius);

        // Reloj de la píldora cerrada: ocupa todo el ancho y el texto va centrado.
        _clock = new DrawingSurface(g);
        _clock.Resize(geo.WindowWidthDip, geo.ClockPillHeightDip, s);
        _clockOpacity = new AnimatedValue(v => _clock.Visual.SetOpacity(v), a => _clock.Visual.SetOpacity(a), expanded || geo.Minimal ? 0 : 1);
        _fill.Visual.AddVisual(_clock.Visual, false, null);

        // Contenido del notch abierto.
        _expanded = g.CreateVisual();
        _expanded.SetOffsetX(geo.Px(geo.MarginDip));
        _expandedOpacity = new AnimatedValue(v => _expanded.SetOpacity(v), a => _expanded.SetOpacity(a), expanded ? 1 : 0);
        _expandedOffsetY = new AnimatedValue(v => _expanded.SetOffsetY(v), a => _expanded.SetOffsetY(a), expanded ? 0 : -geo.Px(8f * geo.F));
        _fill.Visual.AddVisual(_expanded, false, null);

        _header = new DrawingSurface(g);
        _header.Resize(geo.Header.W, geo.Header.H, s);
        _header.SetOffset(geo.Header.X, geo.Header.Y);
        _expanded.AddVisual(_header.Visual, false, null);

        _pageHost = g.CreateVisual();
        _pageHost.SetOffsetX(geo.Px(geo.Content.X));
        _pageHost.SetOffsetY(geo.Px(geo.Content.Y));
        _pageHost.SetClip(new Vortice.RawRectF(0, 0, geo.Px(geo.Content.W), geo.Px(geo.Content.H)));
        _expanded.AddVisual(_pageHost, false, null);

        _dots = new DrawingSurface(g);
        _icons = new DrawingSurface(g);
        foreach (var bar in new[] { _dots, _icons })
        {
            bar.Resize(geo.Bar.W, geo.Bar.H, s);
            bar.SetOffset(geo.Bar.X, geo.Bar.Y);
            _expanded.AddVisual(bar.Visual, false, null);
        }
        _dotsOpacity = new AnimatedValue(v => _dots.Visual.SetOpacity(v), a => _dots.Visual.SetOpacity(a), 1);
        _iconsOpacity = new AnimatedValue(v => _icons.Visual.SetOpacity(v), a => _icons.Visual.SetOpacity(a), 0);
        DrawBar();

        var slot = EnsureSlot(_active);
        slot.Opacity.Set(1);

        _target.SetRoot(_root);
        _dev.Commit();
    }

    /// <summary>Zona de la ventana (en píxeles) que debe recibir el ratón con el notch cerrado.</summary>
    public RECT CollapsedRegion
    {
        get
        {
            var shape = CollapsedShape();
            // En modo mínimo la línea es muy fina: damos algo más de alto para poder «tocarla».
            int bottom = (int)MathF.Max(shape.Bottom, _geo.Px(6f));
            return new RECT { Left = (int)shape.Left, Top = 0, Right = (int)shape.Right, Bottom = bottom };
        }
    }

    /// <summary>Actualiza el reloj y la fecha (y la anchura de la píldora si cambia).</summary>
    public void SetClock(string time, string date)
    {
        if (time == _time && date == _date)
        {
            return;
        }
        _time = time;
        _date = date;
        float width = _geo.CollapsedWidthDip(Text.Measure(_theme.DWrite, time, _theme.Clock));
        bool widthChanged = MathF.Abs(width - _collapsedWidth) > 0.5f;
        _collapsedWidth = width;

        _clock.Draw(dc => Text.Draw(dc, _theme.DWrite, _time, _theme.Clock,
            new RectF(0, 0, _clock.WidthDip, _clock.HeightDip), _theme.Primary, TextAlignment.Center));
        DrawHeader();

        if (widthChanged && !_isExpanded)
        {
            AnimateShape(CollapsedShape(), CollapseDuration, Ease.OutCubic);
        }
        _dev.Commit();
    }

    /// <summary>Abre o cierra el notch.</summary>
    public void SetExpanded(bool expanded, bool animate = true)
    {
        _isExpanded = expanded;
        float clockTarget = expanded || _geo.Minimal ? 0 : 1;
        float hiddenOffset = -_geo.Px(8f * _geo.F);
        if (!animate)
        {
            var shape = expanded ? ExpandedShape() : CollapsedShape();
            _left.Set(shape.Left);
            _right.Set(shape.Right);
            _bottom.Set(shape.Bottom);
            _radius.Set(shape.Radius);
            _clockOpacity.Set(clockTarget);
            _expandedOpacity.Set(expanded ? 1 : 0);
            _expandedOffsetY.Set(expanded ? 0 : hiddenOffset);
        }
        else if (expanded)
        {
            AnimateShape(ExpandedShape(), ExpandDuration, Ease.OutBack);
            _clockOpacity.AnimateTo(_dev, 0, 0.10, Ease.OutCubic);
            _expandedOpacity.AnimateTo(_dev, 1, 0.22, Ease.OutCubic, delay: 0.08);
            _expandedOffsetY.AnimateTo(_dev, 0, 0.34, Ease.OutCubic, delay: 0.04);
        }
        else
        {
            AnimateShape(CollapsedShape(), CollapseDuration, Ease.OutCubic);
            _expandedOpacity.AnimateTo(_dev, 0, 0.12, Ease.OutCubic);
            _expandedOffsetY.AnimateTo(_dev, hiddenOffset, 0.20, Ease.OutCubic);
            _clockOpacity.AnimateTo(_dev, clockTarget, 0.20, Ease.OutCubic, delay: 0.12);
        }
        if (!expanded)
        {
            SetBarHover(false, commit: false);
        }
        _dev.Commit();
    }

    /// <summary>Muestra la página indicada deslizándola desde la dirección de navegación.</summary>
    public void SetActivePage(int index, int direction)
    {
        if (index == _active)
        {
            return;
        }
        float shift = _geo.Px(36f * _geo.F) * direction;
        if (_slots[_active] is { } old)
        {
            old.Opacity.AnimateTo(_dev, 0, 0.16, Ease.OutCubic);
            old.OffsetX.AnimateTo(_dev, -shift, 0.28, Ease.OutCubic);
        }
        var next = EnsureSlot(index);
        if (next.Opacity.Current < 0.01f)
        {
            next.OffsetX.Set(shift); // Entra desde el lado hacia el que navegamos.
        }
        next.Opacity.AnimateTo(_dev, 1, 0.24, Ease.OutCubic, delay: 0.05);
        next.OffsetX.AnimateTo(_dev, 0, 0.32, Ease.OutCubic);
        _active = index;
        DrawBar();
        _dev.Commit();
    }

    /// <summary>Con el ratón sobre la barra, los puntos se convierten en iconos clicables.</summary>
    public void SetBarHover(bool hover, bool commit = true)
    {
        if (hover == _barHover)
        {
            return;
        }
        _barHover = hover;
        _dotsOpacity.AnimateTo(_dev, hover ? 0 : 1, 0.16, Ease.OutCubic);
        _iconsOpacity.AnimateTo(_dev, hover ? 1 : 0, 0.16, Ease.OutCubic);
        if (commit)
        {
            _dev.Commit();
        }
    }

    /// <summary>¿Está el punto (DIP relativos al notch abierto) sobre la barra inferior?</summary>
    public bool IsOverBar(float x, float y) => _geo.Bar.Inflate(0, 4f * _geo.F).Contains(x, y);

    /// <summary>Índice de la página bajo el punto, <see cref="HitSettings"/> o <see cref="HitNone"/>.</summary>
    public int HitTestBar(float x, float y)
    {
        var bar = _geo.Bar;
        float bx = x - bar.X;
        float by = y - bar.Y;
        if (_geo.BarSettings.Inflate(8f * _geo.F, 6f * _geo.F).Contains(bx, by))
        {
            return HitSettings;
        }
        for (int i = 0; i < _pages.Count; i++)
        {
            if (_geo.BarIcon(i, _pages.Count).Inflate(8f * _geo.F, 6f * _geo.F).Contains(bx, by))
            {
                return i;
            }
        }
        return HitNone;
    }

    /// <summary>Redibuja una página cuyo contenido ha cambiado.</summary>
    public void InvalidatePage(int index)
    {
        if (_slots[index] is { } slot)
        {
            DrawPage(index, slot.Surface);
            _dev.Commit();
        }
    }

    public void Dispose()
    {
        _target.SetRoot(null);
        _dev.Commit();
        foreach (var slot in _slots)
        {
            slot?.Surface.Dispose();
        }
        _dots.Dispose();
        _icons.Dispose();
        _header.Dispose();
        _pageHost.Dispose();
        _expanded.Dispose();
        _clock.Dispose();
        _clip.Dispose();
        _fill.Dispose();
        _root.Dispose();
    }

    // ── Forma del notch ──

    private readonly record struct Shape(float Left, float Right, float Bottom, float Radius);

    private Shape CollapsedShape()
    {
        float half = _collapsedWidth / 2;
        return new Shape(_geo.Px(_geo.CenterDip - half), _geo.Px(_geo.CenterDip + half),
            _geo.Px(_geo.CollapsedHeightDip), _geo.CollapsedRadiusDip * _geo.Scale);
    }

    private Shape ExpandedShape() => new(_geo.Px(_geo.MarginDip), _geo.Px(_geo.MarginDip + _geo.ExpandedWidthDip),
        _geo.Px(_geo.ExpandedHeightDip), _geo.ExpandedRadiusDip * _geo.Scale);

    private void AnimateShape(Shape to, double duration, Ease ease)
    {
        _left.AnimateTo(_dev, to.Left, duration, ease);
        _right.AnimateTo(_dev, to.Right, duration, ease);
        _bottom.AnimateTo(_dev, to.Bottom, duration, ease);
        // El radio no rebota: un radio que «se pasa» se ve raro.
        _radius.AnimateTo(_dev, to.Radius, duration, Ease.OutCubic);
    }

    // Solo las esquinas inferiores son redondas: las de arriba están pegadas al borde.
    private void SetRadius(float r)
    {
        _clip.SetBottomLeftRadiusX(r);
        _clip.SetBottomLeftRadiusY(r);
        _clip.SetBottomRightRadiusX(r);
        _clip.SetBottomRightRadiusY(r);
    }

    private void SetRadius(IDCompositionAnimation a)
    {
        _clip.SetBottomLeftRadiusX(a);
        _clip.SetBottomLeftRadiusY(a);
        _clip.SetBottomRightRadiusX(a);
        _clip.SetBottomRightRadiusY(a);
    }

    // ── Dibujo ──

    private void DrawHeader() => _header.Draw(dc =>
    {
        var rect = new RectF(0, 0, _header.WidthDip, _header.HeightDip);
        Text.Draw(dc, _theme.DWrite, _date, _theme.HeaderDate, rect, _theme.Secondary);
        Text.Draw(dc, _theme.DWrite, _time, _theme.HeaderTime, rect, _theme.Primary, TextAlignment.Trailing);
    });

    private void DrawBar()
    {
        int n = _pages.Count;
        float f = _geo.F;
        var settings = _geo.BarSettings;

        _dots.Draw(dc =>
        {
            float dot = 6f * f;
            float activeWidth = 16f * f;
            float gap = 6f * f;
            float total = (n - 1) * (dot + gap) + activeWidth;
            float x = (_dots.WidthDip - total) / 2;
            float cy = _dots.HeightDip / 2;
            for (int i = 0; i < n; i++)
            {
                float w = i == _active ? activeWidth : dot;
                var pill = new RoundedRectangle(new Vortice.RawRectF(x, cy - dot / 2, x + w, cy + dot / 2), dot / 2, dot / 2);
                dc.FillRoundedRectangle(pill, i == _active ? _theme.Primary : _theme.Tertiary);
                x += w + gap;
            }
            _theme.Icons.Draw(dc, IconKind.Settings, settings.X, settings.Y, settings.W, _theme.Tertiary);
        });

        _icons.Draw(dc =>
        {
            for (int i = 0; i < n; i++)
            {
                var r = _geo.BarIcon(i, n);
                if (i == _active)
                {
                    var halo = r.Inflate(5f * f, 5f * f);
                    dc.FillRoundedRectangle(new RoundedRectangle(new Vortice.RawRectF(halo.X, halo.Y, halo.Right, halo.Bottom),
                        8f * f, 8f * f), _theme.Subtle);
                }
                _theme.Icons.Draw(dc, _pages[i].Icon, r.X, r.Y, r.W, i == _active ? _theme.Accent : _theme.Secondary);
            }
            _theme.Icons.Draw(dc, IconKind.Settings, settings.X, settings.Y, settings.W, _theme.Secondary);
        });
    }

    private PageSlot EnsureSlot(int index)
    {
        if (_slots[index] is { } existing)
        {
            return existing;
        }
        var surface = new DrawingSurface(_g);
        surface.Resize(_geo.Content.W, _geo.Content.H, _geo.Scale);
        var slot = new PageSlot(surface,
            new AnimatedValue(v => surface.Visual.SetOpacity(v), a => surface.Visual.SetOpacity(a), 0),
            new AnimatedValue(v => surface.Visual.SetOffsetX(v), a => surface.Visual.SetOffsetX(a), 0));
        DrawPage(index, surface);
        _pageHost.AddVisual(surface.Visual, false, null);
        return _slots[index] = slot;
    }

    private void DrawPage(int index, DrawingSurface surface) =>
        surface.Draw(dc => _pages[index].Draw(dc, _theme, surface.WidthDip, surface.HeightDip));

    private sealed record PageSlot(DrawingSurface Surface, AnimatedValue Opacity, AnimatedValue OffsetX);
}
