using Tilde.App.Notch;
using Tilde.Core;
using Tilde.Render;
using Vortice.Direct2D1;
using Vortice.DirectComposition;
using Vortice.DirectWrite;

namespace Tilde.App.Pages;

/// <summary>
/// Lo que una página necesita para dibujarse: dispositivo, tema, tamaño y su capa raíz.
/// Todo lo que la página crea a través del contexto se libera junto con él (al reconstruir el
/// notch por un cambio de escala, de tamaño, etc.).
/// </summary>
internal sealed class PageContext(GraphicsDevice graphics, Theme theme, float width, float height, float scale, IDCompositionVisual3 root)
    : IDisposable
{
    private readonly List<IDisposable> _owned = [];

    public GraphicsDevice Graphics { get; } = graphics;

    public IDCompositionDesktopDevice Device => Graphics.Composition;

    public Theme Theme { get; } = theme;

    /// <summary>Tamaño del área de la página en DIP.</summary>
    public float Width { get; } = width;

    public float Height { get; } = height;

    public float Scale { get; } = scale;

    /// <summary>Capa raíz de la página (origen en la esquina superior izquierda del área).</summary>
    public IDCompositionVisual3 Root { get; } = root;

    public float F => Theme.F;

    public float Px(float dip) => MathF.Round(dip * Scale);

    /// <summary>Crea una superficie de dibujo en <paramref name="rect"/>, encima de lo anterior.</summary>
    public DrawingSurface AddSurface(RectF rect, IDCompositionVisual? parent = null)
    {
        var surface = new DrawingSurface(Graphics);
        surface.Resize(rect.W, rect.H, Scale);
        surface.SetOffset(rect.X, rect.Y);
        (parent ?? Root).AddVisual(surface.Visual, false, null);
        _owned.Add(surface);
        return surface;
    }

    /// <summary>Crea una capa vacía (para agrupar, recortar o desplazar otras).</summary>
    public IDCompositionVisual3 AddVisual(IDCompositionVisual? parent = null)
    {
        var visual = Graphics.CreateVisual();
        (parent ?? Root).AddVisual(visual, false, null);
        _owned.Add(visual);
        return visual;
    }

    /// <summary>Registra un recurso para liberarlo con el contexto.</summary>
    public T Own<T>(T resource) where T : IDisposable
    {
        _owned.Add(resource);
        return resource;
    }

    public void Commit() => Graphics.Commit();

    public void Dispose()
    {
        for (int i = _owned.Count - 1; i >= 0; i--)
        {
            _owned[i].Dispose();
        }
        _owned.Clear();
    }
}

/// <summary>
/// Una página del notch (Música, Pomodoro…). Construye sus capas al «adjuntarse» y solo
/// redibuja cuando cambia su contenido. Las coordenadas de ratón son DIP relativos a su área.
/// </summary>
internal abstract class Page
{
    public abstract string Title { get; }

    public abstract IconKind Icon { get; }

    /// <summary>Contexto actual, o null si la página no tiene capas (aún no se ha mostrado).</summary>
    protected PageContext? Context { get; private set; }

    /// <summary>true mientras el notch está abierto mostrando esta página.</summary>
    public bool IsVisible { get; private set; }

    public void Attach(PageContext context)
    {
        Context = context;
        OnAttach(context);
    }

    public void Detach()
    {
        if (Context is null)
        {
            return;
        }
        SetVisible(false);
        OnDetach();
        Context = null;
    }

    public void SetVisible(bool visible)
    {
        if (visible != IsVisible)
        {
            IsVisible = visible;
            OnVisibilityChanged(visible);
        }
    }

    protected abstract void OnAttach(PageContext context);

    protected virtual void OnDetach()
    {
    }

    /// <summary>Se llama al abrir/cerrar el notch o cambiar de página. Útil para pausar temporizadores.</summary>
    protected virtual void OnVisibilityChanged(bool visible)
    {
    }

    public virtual void OnPointerMove(float x, float y)
    {
    }

    public virtual void OnPointerLeave()
    {
    }

    /// <summary>Botón pulsado. Devolver true para «capturar» el ratón (arrastres).</summary>
    public virtual bool OnPointerDown(float x, float y) => false;

    public virtual void OnPointerUp(float x, float y)
    {
    }

    /// <summary>¿Hay algo clicable bajo el puntero? (muestra la mano).</summary>
    public virtual bool IsClickable(float x, float y) => false;

    /// <summary>Tecla pulsada con el notch enfocado. Devolver true si se ha usado.</summary>
    public virtual bool OnKey(int key) => false;
}

/// <summary>Página que se dibuja entera en una sola superficie.</summary>
internal abstract class SimplePage : Page
{
    private DrawingSurface? _surface;

    protected override void OnAttach(PageContext context)
    {
        _surface = context.AddSurface(new RectF(0, 0, context.Width, context.Height));
        Redraw();
    }

    protected override void OnDetach() => _surface = null;

    protected void Redraw()
    {
        if (_surface is { } surface && Context is { } context)
        {
            surface.Draw(dc => Draw(dc, context.Theme, context.Width, context.Height));
            context.Commit();
        }
    }

    protected abstract void Draw(ID2D1DeviceContext dc, Theme theme, float width, float height);
}

/// <summary>Página reservada para una fase posterior: muestra su icono, nombre y fase.</summary>
internal sealed class PlaceholderPage(string title, IconKind icon, int phase) : SimplePage
{
    public override string Title => title;

    public override IconKind Icon => icon;

    protected override void Draw(ID2D1DeviceContext dc, Theme theme, float width, float height)
    {
        float f = theme.F;
        float iconSize = 26f * f;
        float titleHeight = 21f * f;
        float captionHeight = 17f * f;
        float gap = 8f * f;
        float y = (height - (iconSize + gap + titleHeight + captionHeight)) / 2;

        theme.Icons.Draw(dc, icon, (width - iconSize) / 2, y, iconSize, theme.Accent);
        y += iconSize + gap;
        Text.Draw(dc, theme.DWrite, title, theme.Title, new RectF(0, y, width, titleHeight), theme.Primary, TextAlignment.Center);
        y += titleHeight;
        Text.Draw(dc, theme.DWrite, Strings.ComingInPhase(phase), theme.Caption, new RectF(0, y, width, captionHeight),
            theme.Secondary, TextAlignment.Center);
    }
}
