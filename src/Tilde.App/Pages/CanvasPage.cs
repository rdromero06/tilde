using Tilde.App.Notch;
using Tilde.Render;
using Vortice.Direct2D1;

namespace Tilde.App.Pages;

/// <summary>Lo que el notch ofrece a las páginas (p. ej. pedir un campo de texto).</summary>
internal interface INotchHost
{
    /// <summary>Muestra un campo de texto nativo sobre <paramref name="rectInPage"/>.</summary>
    void RequestTextInput(RectF rectInPage, string placeholder, string text, Action<string> onSubmit);
}

/// <summary>
/// Página interactiva dibujada en una sola superficie. Cada redibujado registra sus zonas
/// clicables (con un identificador de texto); el ratón se resuelve contra ellas, se resalta lo
/// que hay bajo el puntero y el clic llega a <see cref="Activate"/>.
/// </summary>
internal abstract class CanvasPage : Page
{
    private readonly HitZones<string> _zones = new();
    private DrawingSurface? _surface;
    private string? _pressed;

    /// <summary>Zona bajo el puntero (para resaltarla al dibujar).</summary>
    protected string? Hover { get; private set; }

    public INotchHost? Host { get; set; }

    protected override void OnAttach(PageContext context)
    {
        _surface = context.AddSurface(new RectF(0, 0, context.Width, context.Height));
        Redraw();
    }

    protected override void OnDetach() => _surface = null;

    protected void Redraw()
    {
        if (_surface is not { } surface || Context is not { } c)
        {
            return;
        }
        _zones.Clear();
        surface.Draw(dc => Draw(dc, c.Theme, c.Width, c.Height));
        c.Commit();
    }

    /// <summary>Registra una zona clicable mientras se dibuja.</summary>
    protected RectF Zone(RectF rect, string id) => _zones.Add(rect, id);

    protected bool IsHover(string id) => Hover == id;

    protected abstract void Draw(ID2D1DeviceContext dc, Theme theme, float width, float height);

    /// <summary>Se ha hecho clic en la zona <paramref name="id"/>.</summary>
    protected abstract void Activate(string id, RectF zone);

    public override void OnPointerMove(float x, float y) => SetHover(_zones.Find(x, y));

    public override void OnPointerLeave() => SetHover(null);

    public override bool OnPointerDown(float x, float y)
    {
        _pressed = _zones.Find(x, y);
        return false;
    }

    public override void OnPointerUp(float x, float y)
    {
        var id = _zones.Find(x, y);
        if (id is not null && id == _pressed)
        {
            Activate(id, ZoneRect(id));
        }
        _pressed = null;
    }

    public override bool IsClickable(float x, float y) => _zones.Any(x, y);

#if DEBUG
    /// <summary>Solo depuración: simula un clic en una zona (para pruebas automáticas).</summary>
    public void DebugActivate(string id) => Activate(id, ZoneRect(id));
#endif

    private RectF ZoneRect(string id) => _zones.RectOf(id) ?? default;

    private void SetHover(string? id)
    {
        if (id != Hover)
        {
            Hover = id;
            Redraw();
        }
    }
}
