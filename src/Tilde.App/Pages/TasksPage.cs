using System.Numerics;
using Tilde.App.Notch;
using Tilde.App.Productivity;
using Tilde.Core;
using Tilde.Core.Productivity;
using Tilde.Platform;
using Tilde.Render;
using Vortice.Direct2D1;
using Vortice.DirectWrite;

namespace Tilde.App.Pages;

/// <summary>
/// Tareas: campo «Nueva tarea» (se escribe en lenguaje natural, p. ej. «Llamar a Ana 18:30»),
/// lista con casilla del color de cada tarea, hora del aviso y, al pasar el ratón, botones para
/// cambiar el color y borrar. Clic en el texto para editarla. Flechas para desplazarse.
/// </summary>
internal sealed class TasksPage : CanvasPage
{
    private readonly TasksService _service;
    private int _first;

    public TasksPage(TasksService service)
    {
        _service = service;
        service.Changed += Redraw;
    }

    public override string Title => Strings.PageTasks;

    public override IconKind Icon => IconKind.Tasks;

    private float RowHeight => 21f * (Context?.F ?? 1);

    private float ListTop => 30f * (Context?.F ?? 1);

    private int VisibleRows => Context is { } c ? Math.Max(1, (int)((c.Height - ListTop) / RowHeight)) : 3;

    protected override void Activate(string id, RectF zone)
    {
        if (id == "add")
        {
            Host?.RequestTextInput(zone, Strings.TasksHint, string.Empty, text => _service.Add(text));
            return;
        }
        if (id == "up" || id == "down")
        {
            _first += id == "up" ? -1 : 1;
            Redraw();
            return;
        }
        int colon = id.IndexOf(':');
        if (colon < 0 || !Guid.TryParse(id[(colon + 1)..], out var taskId))
        {
            return;
        }
        switch (id[..colon])
        {
            case "check": _service.Toggle(taskId); break;
            case "color": _service.CycleColor(taskId); break;
            case "delete": _service.Delete(taskId); break;
            case "title":
                if (_service.List.Find(taskId) is { } task)
                {
                    var text = QuickEntry.Format(task.Title, task.RemindAt?.LocalDateTime, DateTime.Now);
                    Host?.RequestTextInput(zone with { X = 0, W = Context?.Width ?? zone.W }, Strings.TasksHint, text, t => _service.Edit(taskId, t));
                }
                break;
        }
    }

    protected override void Draw(ID2D1DeviceContext dc, Theme theme, float w, float h)
    {
        float f = theme.F;
        var tasks = _service.List.Ordered();
        int visible = VisibleRows;
        _first = Math.Clamp(_first, 0, Math.Max(0, tasks.Count - visible));
        bool scroll = tasks.Count > visible;

        // Campo «Nueva tarea».
        var add = Zone(new RectF(0, 0, w - (scroll ? 64f * f : 0), 24f * f), "add");
        dc.FillRoundedRectangle(Ui.Rounded(add, add.H / 2), IsHover("add") ? theme.Raised : theme.Subtle);
        float plus = 14f * f;
        theme.Icons.Draw(dc, IconKind.Plus, add.X + 9f * f, add.Y + (add.H - plus) / 2, plus, theme.Secondary);
        Ui.Label(dc, theme, Strings.TasksNew, theme.Caption, new RectF(add.X + 30f * f, add.Y, add.W - 40f * f, add.H), theme.Secondary);

        if (scroll)
        {
            var up = Zone(new RectF(w - 58f * f, 0, 26f * f, 24f * f), "up");
            Ui.IconButton(dc, theme, up, IconKind.ChevronUp, IsHover("up"), 0.7f, _first > 0 ? theme.Primary : theme.Tertiary);
            var down = Zone(new RectF(w - 28f * f, 0, 26f * f, 24f * f), "down");
            Ui.IconButton(dc, theme, down, IconKind.ChevronDown, IsHover("down"), 0.7f,
                _first + visible < tasks.Count ? theme.Primary : theme.Tertiary);
        }

        if (tasks.Count == 0)
        {
            Ui.Label(dc, theme, Strings.TasksEmpty, theme.Caption, new RectF(0, ListTop, w, h - ListTop), theme.Tertiary, TextAlignment.Center);
            return;
        }

        var now = DateTimeOffset.Now;
        for (int i = 0; i < visible && _first + i < tasks.Count; i++)
        {
            var task = tasks[_first + i];
            var row = new RectF(0, ListTop + i * RowHeight, w, RowHeight);
            DrawRow(dc, theme, task, row, now);
        }
    }

    private void DrawRow(ID2D1DeviceContext dc, Theme theme, TaskItem task, RectF row, DateTimeOffset now)
    {
        float f = theme.F;
        string key = task.Id.ToString();
        bool rowHover = Hover is { } hover && hover.EndsWith(key, StringComparison.Ordinal);
        var color = theme.TaskColors[Math.Clamp(task.Color, 0, theme.TaskColors.Length - 1)];
        Zone(row, "row:" + key); // Fondo de la fila: mantiene visibles sus botones al pasar el ratón.
        float cy = row.Y + row.H / 2;

        // Casilla: anillo del color de la tarea; rellena con ✓ si está hecha.
        float box = 14f * f;
        var check = Zone(new RectF(row.X, row.Y, 26f * f, row.H), "check:" + key);
        var center = new Vector2(check.X + 4f * f + box / 2, cy);
        if (task.Done)
        {
            dc.FillEllipse(new Ellipse(center, box / 2, box / 2), color);
            theme.Icons.Draw(dc, IconKind.Check, center.X - box * 0.4f, cy - box * 0.4f, box * 0.8f, theme.Fill);
        }
        else
        {
            dc.DrawEllipse(new Ellipse(center, box / 2 - 0.8f * f, box / 2 - 0.8f * f), color, IsHover("check:" + key) ? 2.4f * f : 1.6f * f);
        }

        // Hora del aviso (en coral si ya pasó).
        float right = row.Right;
        if (rowHover)
        {
            var delete = Zone(new RectF(right - 22f * f, row.Y, 22f * f, row.H), "delete:" + key);
            Ui.IconButton(dc, theme, delete, IconKind.Close, IsHover("delete:" + key), 0.6f,
                IsHover("delete:" + key) ? theme.Primary : theme.Tertiary);
            var colorZone = Zone(new RectF(delete.X - 22f * f, row.Y, 22f * f, row.H), "color:" + key);
            float dot = IsHover("color:" + key) ? 11f * f : 9f * f;
            dc.FillEllipse(new Ellipse(new Vector2(colorZone.X + colorZone.W / 2, cy), dot / 2, dot / 2), color);
            right = colorZone.X - 4f * f;
        }
        if (task.RemindAt is { } at)
        {
            var local = at.LocalDateTime;
            string time = RegionalFormat.ShortTime(local);
            if (local.Date > DateTime.Today)
            {
                time = $"{Strings.Tomorrow} {time}";
            }
            var brush = task.Done ? theme.Tertiary : at <= now ? theme.TaskColors[1] : theme.Secondary;
            float tw = Text.Measure(theme.DWrite, time, theme.Small) + 4f * f;
            Ui.Label(dc, theme, time, theme.Small, new RectF(right - tw, row.Y, tw, row.H), brush, TextAlignment.Trailing);
            right -= tw + 8f * f;
        }

        // Título (tachado si está hecha). Clic para editar.
        var title = Zone(new RectF(28f * f, row.Y, MathF.Max(10f, right - 28f * f), row.H), "title:" + key);
        Ui.Label(dc, theme, task.Title, theme.Caption, title, task.Done ? theme.Tertiary : IsHover("title:" + key) ? theme.Primary : theme.Primary);
        if (task.Done)
        {
            float width = MathF.Min(title.W, Text.Measure(theme.DWrite, task.Title, theme.Caption));
            dc.DrawLine(new Vector2(title.X, cy + 0.5f * f), new Vector2(title.X + width, cy + 0.5f * f), theme.Tertiary, 1.2f * f);
        }
        else if (IsHover("title:" + key))
        {
            float width = MathF.Min(title.W, Text.Measure(theme.DWrite, task.Title, theme.Caption));
            dc.DrawLine(new Vector2(title.X, cy + 8f * f), new Vector2(title.X + width, cy + 8f * f), theme.Subtle, 1f * f);
        }
    }
}
