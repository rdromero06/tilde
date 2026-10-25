using Tilde.App.Notch;
using Tilde.App.Productivity;
using Tilde.Core;
using Tilde.Platform;
using Tilde.Render;
using Vortice.Direct2D1;
using Vortice.DirectWrite;

namespace Tilde.App.Pages;

/// <summary>
/// Agua: un vaso que se llena, el total del día frente a la meta, el botón «Beber», deshacer,
/// el tamaño del vaso y la meta (clic para cambiarlos) y un gráfico por horas del día.
/// </summary>
internal sealed class WaterPage : CanvasPage
{
    /// <summary>Tiempo que se mantiene la confirmación de «poner a cero» antes de desaparecer.</summary>
    private static readonly TimeSpan ConfirmTimeout = TimeSpan.FromSeconds(4);

    private readonly WaterService _service;
    private readonly UiTimer _confirmTimer;
    private bool _confirmReset;

    public WaterPage(WaterService service)
    {
        _service = service;
        _confirmTimer = new UiTimer(() => SetConfirm(false));
        service.Changed += Redraw;
    }

    public override string Title => Strings.PageWater;

    public override IconKind Icon => IconKind.Water;

    protected override void Activate(string id, RectF zone)
    {
        switch (id)
        {
            case "drink": _service.Drink(); break;
            case "undo": _service.Undo(); break;
            case "reset": SetConfirm(true); break; // Primer clic: pide confirmación.
            case "reset-cancel": SetConfirm(false); break;
            case "reset-confirm":
                SetConfirm(false);
                _service.ResetToday();
                break;
            case "glass": _service.CycleGlass(); Redraw(); break;
            case "goal": _service.CycleGoal(); break;
        }
    }

    protected override void OnVisibilityChanged(bool visible)
    {
        if (!visible)
        {
            SetConfirm(false);
        }
    }

    /// <summary>
    /// Poner el día a cero va en dos pasos para no hacerlo sin querer: el primer clic convierte el
    /// botón en «¿Poner a cero?» durante unos segundos y el segundo lo confirma. Aun así se puede
    /// deshacer con el botón de deshacer.
    /// </summary>
    private void SetConfirm(bool confirm)
    {
        _confirmTimer.Stop();
        if (confirm)
        {
            _confirmTimer.Start(ConfirmTimeout);
        }
        if (confirm != _confirmReset)
        {
            _confirmReset = confirm;
            Redraw();
        }
    }

    protected override void Draw(ID2D1DeviceContext dc, Theme theme, float w, float h)
    {
        float f = theme.F;
        var s = _service.Settings;
        int total = _service.TodayTotal;
        float fraction = s.GoalMl > 0 ? Math.Clamp(total / (float)s.GoalMl, 0, 1) : 0;

        // Vaso: contorno y agua recortada a su forma.
        var glass = new RectF(6f * f, (h - 76f * f) / 2, 46f * f, 76f * f);
        float r = 10f * f;
        if (fraction > 0)
        {
            var inner = glass.Inflate(-3.5f * f, -3.5f * f);
            float top = inner.Bottom - inner.H * fraction;
            dc.PushAxisAlignedClip(new Vortice.RawRectF(inner.X, top, inner.Right, inner.Bottom), AntialiasMode.Aliased);
            dc.FillRoundedRectangle(Ui.Rounded(inner, r - 3f * f), theme.Water);
            dc.PopAxisAlignedClip();
        }
        dc.DrawRoundedRectangle(Ui.Rounded(glass, r), theme.Tertiary, 1.6f * f);

        // Totales.
        float x0 = glass.Right + 18f * f;
        float chartW = 176f * f;
        float textW = w - x0 - chartW - 16f * f;
        Ui.Label(dc, theme, Amounts.Ml(total), theme.Countdown, new RectF(x0, 4f * f, textW, 26f * f), theme.Primary);
        string detail = total >= s.GoalMl
            ? Strings.WaterGoalReached
            : Strings.WaterOf(Amounts.Ml(s.GoalMl), (int)Math.Round(fraction * 100));
        if (_service.NextReminder is { } next && next.Date == DateTime.Today && total < s.GoalMl)
        {
            detail += " · " + Strings.WaterNext(RegionalFormat.ShortTime(next));
        }
        Ui.Label(dc, theme, detail, theme.Caption, new RectF(x0, 31f * f, textW, 18f * f), total >= s.GoalMl ? theme.Mint : theme.Secondary);

        // Botones: beber, deshacer y poner a cero (este último pide confirmación en su lugar).
        if (_confirmReset)
        {
            string confirm = Strings.WaterResetConfirm;
            var confirmRect = Zone(new RectF(x0, h - 36f * f, Ui.ButtonWidth(theme, confirm, icon: true), 28f * f), "reset-confirm");
            Ui.Button(dc, theme, confirmRect, confirm, primary: true, IsHover("reset-confirm"), IconKind.Restart);
            var cancel = Zone(new RectF(confirmRect.Right + 8f * f, confirmRect.Y, 28f * f, 28f * f), "reset-cancel");
            Ui.IconButton(dc, theme, cancel, IconKind.Close, IsHover("reset-cancel"), 0.55f, theme.Secondary);
        }
        else
        {
            string drink = Strings.WaterDrink("+" + Amounts.Ml(s.GlassMl));
            var drinkRect = Zone(new RectF(x0, h - 36f * f, Ui.ButtonWidth(theme, drink, icon: true), 28f * f), "drink");
            Ui.Button(dc, theme, drinkRect, drink, primary: true, IsHover("drink"), IconKind.Water);
            var undo = Zone(new RectF(drinkRect.Right + 8f * f, drinkRect.Y, 28f * f, 28f * f), "undo");
            Ui.IconButton(dc, theme, undo, IconKind.Undo, IsHover("undo"), 0.58f, _service.CanUndo ? theme.Secondary : theme.Tertiary);
            if (total > 0)
            {
                var reset = Zone(new RectF(undo.Right + 2f * f, drinkRect.Y, 28f * f, 28f * f), "reset");
                Ui.IconButton(dc, theme, reset, IconKind.Restart, IsHover("reset"), 0.58f, theme.Secondary);
            }
        }

        DrawChart(dc, theme, new RectF(w - chartW, 4f * f, chartW, h - 8f * f));
    }

    /// <summary>Texto pequeño que se resalta al pasar el ratón (para opciones que se cambian con un clic).</summary>
    private void Chip(ID2D1DeviceContext dc, Theme theme, RectF r, string text, string id)
    {
        bool hover = IsHover(id);
        dc.FillRoundedRectangle(Ui.Rounded(r, r.H / 2), hover ? theme.Raised : theme.Subtle);
        Ui.Label(dc, theme, text, theme.Small, r, hover ? theme.Primary : theme.Secondary, TextAlignment.Center);
    }

    /// <summary>Barras por hora dentro del horario de avisos; la hora actual en color.</summary>
    private void DrawChart(ID2D1DeviceContext dc, Theme theme, RectF area)
    {
        float f = theme.F;
        var s = _service.Settings;
        int startHour = Tilde.Core.Productivity.WaterTracker.ParseTime(s.ReminderStart, new TimeOnly(9, 0)).Hour;
        int endHour = Math.Max(startHour + 1, Tilde.Core.Productivity.WaterTracker.ParseTime(s.ReminderEnd, new TimeOnly(21, 0)).Hour);
        var hourly = _service.TodayHourly;

        // Cabecera: tamaño del vaso y meta, ambos clicables para cambiarlos.
        string glass = Strings.WaterGlass(Amounts.Ml(s.GlassMl));
        float glassW = Ui.ButtonWidth(theme, glass, icon: false) - 6f * f;
        Chip(dc, theme, Zone(new RectF(area.X, area.Y - 2f * f, glassW, 20f * f), "glass"), glass, "glass");
        string goal = Strings.WaterGoal(Amounts.Ml(s.GoalMl));
        float goalW = Ui.ButtonWidth(theme, goal, icon: false) - 6f * f;
        Chip(dc, theme, Zone(new RectF(area.Right - goalW, area.Y - 2f * f, goalW, 20f * f), "goal"), goal, "goal");

        var bars = new RectF(area.X, area.Y + 22f * f, area.W, area.H - 40f * f);
        int count = endHour - startHour + 1;
        float slot = bars.W / count;
        float barW = MathF.Max(2f * f, slot * 0.62f);
        int max = Math.Max(s.GlassMl * 2, hourly.Skip(startHour).Take(count).DefaultIfEmpty(0).Max());
        int nowHour = DateTime.Now.Hour;
        for (int i = 0; i < count; i++)
        {
            int hour = startHour + i;
            float x = bars.X + i * slot + (slot - barW) / 2;
            float value = hour < 24 ? hourly[hour] / (float)max : 0;
            float barH = MathF.Max(2f * f, bars.H * value);
            var bar = new RectF(x, bars.Bottom - barH, barW, barH);
            var brush = value > 0 ? (hour == nowHour ? theme.Water : theme.Secondary) : hour == nowHour ? theme.Tertiary : theme.Subtle;
            dc.FillRoundedRectangle(Ui.Rounded(bar, MathF.Min(barW, barH) / 2), brush);
        }
        var labels = new RectF(area.X, bars.Bottom + 3f * f, area.W, 14f * f);
        Ui.Label(dc, theme, $"{startHour} h", theme.Small, labels, theme.Tertiary);
        Ui.Label(dc, theme, $"{endHour} h", theme.Small, labels, theme.Tertiary, TextAlignment.Trailing);
    }
}
