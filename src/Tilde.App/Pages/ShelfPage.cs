using System.Numerics;
using Tilde.App.Notch;
using Tilde.App.Productivity;
using Tilde.Core;
using Tilde.Core.Productivity;
using Tilde.Platform;
using Tilde.Platform.Interop;
using Tilde.Platform.Shell;
using Tilde.Render;
using Vortice.Direct2D1;
using Vortice.DirectWrite;

namespace Tilde.App.Pages;

/// <summary>
/// Estante: tarjetas con la miniatura o el icono de cada archivo o carpeta, y tarjetas de texto.
/// <list type="bullet">
/// <item>Clic selecciona; Ctrl+clic alterna; Mayús+clic selecciona un rango; Ctrl+A todo; Supr quita.</item>
/// <item>Arrastrar una tarjeta (o la selección) a otra app la entrega; si cancelas, se queda.</item>
/// <item>Clic en una tarjeta de texto la copia al portapapeles; doble clic en un archivo lo abre.</item>
/// </list>
/// </summary>
internal sealed class ShelfPage : CanvasPage
{
    private readonly ShelfService _service;
    private readonly Dictionary<Guid, ID2D1Bitmap1?> _images = [];
    private readonly HashSet<Guid> _loading = [];
    private readonly UiTimer _copiedTimer;
    private Dictionary<Guid, bool> _exists = [];
    private int _first;
    private bool _dropHover;
    private Guid? _copied;

    private Guid? _downId;
    private Vector2 _downAt;
    private bool _downCtrl;
    private bool _downShift;
    private Guid? _lastClickId;
    private long _lastClickTicks;

    public ShelfPage(ShelfService service)
    {
        _service = service;
        _copiedTimer = new UiTimer(() => { _copied = null; Redraw(); });
        service.Changed += () => { RefreshExistence(); Redraw(); };
    }

    public override string Title => Strings.PageShelf;

    public override IconKind Icon => IconKind.Shelf;

    private float CardWidth => 66f * (Context?.F ?? 1);

    private float CardGap => 6f * (Context?.F ?? 1);

    /// <summary>Resalta el estante mientras se arrastra algo encima.</summary>
    public void SetDropHover(bool hover)
    {
        if (hover != _dropHover)
        {
            _dropHover = hover;
            if (hover)
            {
                _first = int.MaxValue; // Al soltar, que se vea el final (donde entra lo nuevo).
            }
            Redraw();
        }
    }

    protected override void OnAttach(PageContext context)
    {
        RefreshExistence();
        base.OnAttach(context);
    }

    protected override void OnDetach()
    {
        // Los bitmaps dependen del dispositivo gráfico y de la escala: se vuelven a pedir.
        foreach (var image in _images.Values)
        {
            image?.Dispose();
        }
        _images.Clear();
        _loading.Clear();
        base.OnDetach();
    }

    protected override void OnVisibilityChanged(bool visible)
    {
        if (visible)
        {
            RefreshExistence(); // Por si algún archivo se borró o movió mientras tanto.
            Redraw();
        }
    }

    private void RefreshExistence() =>
        _exists = _service.List.Items.Where(i => i.Kind != ShelfKind.Text)
            .ToDictionary(i => i.Id, i => i.Kind == ShelfKind.Folder ? Directory.Exists(i.Path) : File.Exists(i.Path));

    private bool Exists(ShelfItem item) => item.Kind == ShelfKind.Text || _exists.GetValueOrDefault(item.Id, true);

    // ── Ratón y teclado ──

    public override bool OnPointerDown(float x, float y)
    {
        base.OnPointerDown(x, y);
        _downId = null;
        if (FindZone(x, y) is { } id && id.StartsWith("card:", StringComparison.Ordinal) && Guid.TryParse(id[5..], out var cardId))
        {
            _downId = cardId;
            _downAt = new Vector2(x, y);
            _downCtrl = SystemActions.IsControlDown;
            _downShift = SystemActions.IsShiftDown;
            return true; // Capturamos el ratón para detectar el inicio de un arrastre.
        }
        return false;
    }

    public override void OnPointerMove(float x, float y)
    {
        if (_downId is { } id && Context is { } c)
        {
            float threshold = MathF.Max(4f, Native.GetSystemMetrics(Native.SM_CXDRAG)) / c.Scale;
            if (Vector2.Distance(_downAt, new Vector2(x, y)) >= threshold)
            {
                _downId = null;
                StartDrag(id);
            }
            return;
        }
        base.OnPointerMove(x, y);
    }

    public override void OnPointerUp(float x, float y)
    {
        if (_downId is { } id)
        {
            _downId = null;
            OnCardClick(id);
            return;
        }
        base.OnPointerUp(x, y);
    }

    public override bool OnKey(int key)
    {
        if (key == Native.VK_DELETE && _service.List.Selected.Count > 0)
        {
            _service.Remove([.. _service.List.Selected]);
            return true;
        }
        if (key == 'A' && SystemActions.IsControlDown)
        {
            _service.SelectAll();
            return true;
        }
        return false;
    }

    protected override void Activate(string id, RectF zone)
    {
        switch (id)
        {
            case "clear": _service.Clear(); break;
            case "left": _first--; Redraw(); break;
            case "right": _first++; Redraw(); break;
            case "empty": _service.ClearSelection(); break;
            default:
                if (id.StartsWith("remove:", StringComparison.Ordinal) && Guid.TryParse(id[7..], out var removeId))
                {
                    _service.Remove([removeId]);
                }
                break;
        }
    }

    private void OnCardClick(Guid id)
    {
        if (_service.List.Items.FirstOrDefault(i => i.Id == id) is not { } item)
        {
            return;
        }
        if (item.Kind == ShelfKind.Text && !_downCtrl && !_downShift)
        {
            // Texto: un clic lo copia al portapapeles.
            if (Host?.CopyText(item.Text ?? string.Empty) == true)
            {
                _copied = id;
                _copiedTimer.Start(TimeSpan.FromSeconds(1.2));
            }
            _service.Click(id, ctrl: false, shift: false);
            return;
        }

        long now = Environment.TickCount64;
        bool isDouble = _lastClickId == id && now - _lastClickTicks <= Native.GetDoubleClickTime();
        _lastClickId = id;
        _lastClickTicks = now;
        if (isDouble && !_downCtrl && !_downShift && item.Path is { } path && Exists(item))
        {
            SystemActions.Open(path);
            return;
        }
        _service.Click(id, _downCtrl, _downShift);
    }

    private void StartDrag(Guid id)
    {
        var items = _service.List.DragSet(id).Where(i => i.Kind != ShelfKind.Text && i.Path is not null && Exists(i)).ToList();
        Redraw();
        if (items.Count == 0 || Host is null)
        {
            return;
        }
        // Si se suelta en algún sitio, salen del estante; si se cancela, se quedan.
        if (Host.BeginFileDrag([.. items.Select(i => i.Path!)]))
        {
            _service.Remove(items.Select(i => i.Id));
        }
    }

    // ── Dibujo ──

    protected override void Draw(ID2D1DeviceContext dc, Theme theme, float w, float h)
    {
        float f = theme.F;
        var items = _service.List.Items;
        if (items.Count == 0)
        {
            DrawEmpty(dc, theme, w, h);
            return;
        }
        Zone(new RectF(0, 0, w, h), "empty"); // Clic en el hueco: quita la selección.

        // Cabecera: recuento y «Vaciar todo».
        int selected = _service.List.Selected.Count;
        Ui.Label(dc, theme, selected > 0 ? Strings.ShelfSelected(selected, items.Count) : Strings.ShelfCount(items.Count),
            theme.Small, new RectF(0, 0, w / 2, 16f * f), theme.Tertiary);
        float clearW = Text.Measure(theme.DWrite, Strings.ShelfClear, theme.Small) + 14f * f;
        var clear = Zone(new RectF(w - clearW, -1f * f, clearW, 18f * f), "clear");
        if (IsHover("clear"))
        {
            dc.FillRoundedRectangle(Ui.Rounded(clear, clear.H / 2), theme.Subtle);
        }
        Ui.Label(dc, theme, Strings.ShelfClear, theme.Small, clear, IsHover("clear") ? theme.Primary : theme.Secondary, TextAlignment.Center);

        // Tarjetas.
        float top = 20f * f;
        float cardH = h - top;
        float arrow = 18f * f;
        int fits = (int)((w + CardGap) / (CardWidth + CardGap));
        bool overflow = items.Count > fits;
        if (overflow)
        {
            fits = Math.Max(1, (int)((w - 2 * arrow + CardGap) / (CardWidth + CardGap)));
        }
        _first = Math.Clamp(_first, 0, Math.Max(0, items.Count - fits));
        float x = overflow ? arrow : 0;
        for (int i = _first; i < items.Count && i < _first + fits; i++)
        {
            DrawCard(dc, theme, items[i], new RectF(x, top, CardWidth, cardH));
            x += CardWidth + CardGap;
        }
        if (overflow)
        {
            var left = Zone(new RectF(0, top, arrow, cardH), "left");
            Ui.IconButton(dc, theme, left, IconKind.ChevronLeft, IsHover("left"), 0.9f, _first > 0 ? theme.Primary : theme.Tertiary);
            var right = Zone(new RectF(w - arrow, top, arrow, cardH), "right");
            Ui.IconButton(dc, theme, right, IconKind.ChevronRight, IsHover("right"), 0.9f,
                _first + fits < items.Count ? theme.Primary : theme.Tertiary);
        }

        if (_dropHover)
        {
            DrawDropOverlay(dc, theme, new RectF(0, 0, w, h), showText: false);
        }
    }

    private void DrawCard(ID2D1DeviceContext dc, Theme theme, ShelfItem item, RectF r)
    {
        float f = theme.F;
        string key = item.Id.ToString();
        var card = Zone(r, "card:" + key);
        bool selected = _service.List.IsSelected(item.Id);
        bool hover = IsHover("card:" + key) || IsHover("remove:" + key);
        bool exists = Exists(item);

        if (selected || hover)
        {
            dc.FillRoundedRectangle(Ui.Rounded(card, 10f * f), selected ? theme.Raised : theme.Subtle);
        }
        if (selected)
        {
            dc.DrawRoundedRectangle(Ui.Rounded(card.Inflate(-0.75f * f, -0.75f * f), 10f * f), theme.Accent, 1.5f * f);
        }

        var image = new RectF(r.X + (r.W - 38f * f) / 2, r.Y + 5f * f, 38f * f, 38f * f);
        if (_copied == item.Id)
        {
            theme.Icons.Draw(dc, IconKind.Check, image.X + 7f * f, image.Y + 7f * f, 24f * f, theme.Mint);
        }
        else if (item.Kind == ShelfKind.Text)
        {
            dc.FillRoundedRectangle(Ui.Rounded(image, 8f * f), theme.Subtle);
            theme.Icons.Draw(dc, IconKind.Lyrics, image.X + 9f * f, image.Y + 9f * f, 20f * f, theme.Secondary);
        }
        else if (GetImage(item) is { } bitmap)
        {
            Images.DrawFit(dc, bitmap, image, exists ? 1f : 0.35f);
        }

        string name = _copied == item.Id ? Strings.ShelfCopied : exists ? item.DisplayName : Strings.ShelfMissing;
        using var layout = theme.DWrite.CreateTextLayout(name, theme.Small, r.W - 6f * f, 26f * f);
        layout.WordWrapping = WordWrapping.EmergencyBreak;
        layout.TextAlignment = TextAlignment.Center;
        using var ellipsis = theme.DWrite.CreateEllipsisTrimmingSign(layout);
        layout.SetTrimming(new Trimming { Granularity = TrimmingGranularity.Character }, ellipsis);
        dc.DrawTextLayout(new Vector2(r.X + 3f * f, image.Bottom + 3f * f), layout,
            exists ? (selected ? theme.Primary : theme.Secondary) : theme.Tertiary, DrawTextOptions.Clip);

        if (hover)
        {
            var remove = Zone(new RectF(r.Right - 18f * f, r.Y + 2f * f, 16f * f, 16f * f), "remove:" + key);
            dc.FillEllipse(new Ellipse(new Vector2(remove.X + remove.W / 2, remove.Y + remove.H / 2), 8f * f, 8f * f),
                IsHover("remove:" + key) ? theme.Raised : theme.Fill);
            theme.Icons.Draw(dc, IconKind.Close, remove.X + 2f * f, remove.Y + 2f * f, 12f * f, theme.Primary);
        }
    }

    private void DrawEmpty(ID2D1DeviceContext dc, Theme theme, float w, float h)
    {
        float f = theme.F;
        if (_dropHover)
        {
            DrawDropOverlay(dc, theme, new RectF(0, 0, w, h), showText: true);
            return;
        }
        DrawDashedBox(dc, theme, new RectF(2f * f, 2f * f, w - 4f * f, h - 4f * f), theme.Tertiary);
        float icon = 24f * f;
        float y = (h - (icon + 6f * f + 18f * f + 16f * f)) / 2;
        theme.Icons.Draw(dc, IconKind.Shelf, (w - icon) / 2, y, icon, theme.Secondary);
        y += icon + 6f * f;
        Ui.Label(dc, theme, Strings.ShelfDropHint, theme.Caption, new RectF(0, y, w, 18f * f), theme.Primary, TextAlignment.Center);
        Ui.Label(dc, theme, Strings.ShelfDropDetail, theme.Small, new RectF(0, y + 18f * f, w, 16f * f), theme.Tertiary, TextAlignment.Center);
    }

    private void DrawDropOverlay(ID2D1DeviceContext dc, Theme theme, RectF r, bool showText)
    {
        float f = theme.F;
        dc.FillRoundedRectangle(Ui.Rounded(r, 12f * f), theme.Subtle);
        DrawDashedBox(dc, theme, r.Inflate(-2f * f, -2f * f), theme.Accent);
        if (showText)
        {
            float icon = 24f * f;
            float y = (r.H - (icon + 6f * f + 18f * f)) / 2;
            theme.Icons.Draw(dc, IconKind.Shelf, (r.W - icon) / 2, y, icon, theme.Accent);
            Ui.Label(dc, theme, Strings.ShelfDropNow, theme.Caption, new RectF(0, y + icon + 6f * f, r.W, 18f * f), theme.Primary, TextAlignment.Center);
        }
    }

    private void DrawDashedBox(ID2D1DeviceContext dc, Theme theme, RectF r, ID2D1Brush brush)
    {
        if (Context is not { } c)
        {
            return;
        }
        float f = theme.F;
        using var style = c.Graphics.D2DFactory.CreateStrokeStyle(new StrokeStyleProperties1
        {
            DashStyle = DashStyle.Custom,
            StartCap = CapStyle.Round,
            EndCap = CapStyle.Round,
            DashCap = CapStyle.Round,
        }, [3f, 3f]);
        dc.DrawRoundedRectangle(Ui.Rounded(r, 12f * f), brush, 1.4f * f, style);
    }

    /// <summary>Miniatura o icono (se pide en segundo plano la primera vez y luego se redibuja).</summary>
    private ID2D1Bitmap1? GetImage(ShelfItem item)
    {
        if (_images.TryGetValue(item.Id, out var cached))
        {
            return cached;
        }
        if (Context is { } c && item.Path is { } path && _loading.Add(item.Id))
        {
            int size = (int)MathF.Ceiling(38f * c.F * c.Scale);
            _ = LoadImageAsync(item.Id, path, size, c);
        }
        return null;
    }

    private async Task LoadImageAsync(Guid id, string path, int size, PageContext context)
    {
        nint hbitmap = await Task.Run(() => ShellImages.GetImage(path, size));
        try
        {
            if (!ReferenceEquals(Context, context))
            {
                return; // La página se reconstruyó mientras tanto.
            }
            _images[id] = hbitmap != 0 ? Images.FromHBitmap(context.Graphics, hbitmap) : null;
            Redraw();
        }
        finally
        {
            if (hbitmap != 0)
            {
                Native.DeleteObject(hbitmap);
            }
        }
    }
}
