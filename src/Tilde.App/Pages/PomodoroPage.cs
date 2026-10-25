using System.Numerics;
using Tilde.App.Notch;
using Tilde.App.Productivity;
using Tilde.Core;
using Tilde.Core.Productivity;
using Tilde.Platform;
using Tilde.Platform.Interop;
using Tilde.Render;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Tilde.App.Pages;

/// <summary>
/// Pomodoro: anillo de progreso con la cuenta atrás, fase y sesión, y botones para empezar o
/// pausar, reiniciar y saltar. Solo se redibuja cada segundo mientras está visible y en marcha.
/// </summary>
internal sealed class PomodoroPage : CanvasPage
{
    private readonly PomodoroService _service;
    private readonly UiTimer _tick;

    public PomodoroPage(PomodoroService service)
    {
        _service = service;
        _tick = new UiTimer(OnTick);
        service.Changed += () => { Redraw(); ScheduleTick(); };
    }

    public override string Title => Strings.PagePomodoro;

    public override IconKind Icon => IconKind.Timer;

    protected override void OnVisibilityChanged(bool visible)
    {
        Redraw();
        ScheduleTick();
    }

    protected override void OnDetach()
    {
        _tick.Stop();
        base.OnDetach();
    }

    public override bool OnKey(int key)
    {
        if (key == Native.VK_SPACE)
        {
            _service.Toggle();
            return true;
        }
        return false;
    }

    protected override void Activate(string id, RectF zone)
    {
        switch (id)
        {
            case "toggle": _service.Toggle(); break;
            case "reset": _service.Reset(); break;
            case "skip": _service.Skip(); break;
        }
    }

    private void OnTick()
    {
        Redraw();
        ScheduleTick();
    }

    /// <summary>Próximo redibujado justo cuando cambia el segundo mostrado.</summary>
    private void ScheduleTick()
    {
        _tick.Stop();
        if (IsVisible && _service.Model.IsRunning)
        {
            var remaining = _service.Model.Remaining(DateTimeOffset.Now);
            double fraction = remaining.TotalSeconds % 1;
            _tick.Start(TimeSpan.FromSeconds(fraction < 0.02 ? 1 : fraction) + TimeSpan.FromMilliseconds(15));
        }
    }

    protected override void Draw(ID2D1DeviceContext dc, Theme theme, float w, float h)
    {
        float f = theme.F;
        var model = _service.Model;
        var now = DateTimeOffset.Now;
        var remaining = model.Remaining(now);
        bool isFocus = model.Phase == PomodoroPhase.Focus;
        var phaseBrush = isFocus ? theme.Accent : theme.Mint;

        // Anillo de progreso con la cuenta atrás dentro.
        float d = MathF.Min(82f * f, h - 6f * f);
        var ring = new RectF(4f * f, (h - d) / 2, d, d);
        float stroke = 5f * f;
        var center = new Vector2(ring.X + d / 2, ring.Y + d / 2);
        float radius = d / 2 - stroke / 2;
        dc.DrawEllipse(new Ellipse(center, radius, radius), theme.Subtle, stroke);
        DrawArc(dc, theme, center, radius, (float)model.Progress(now), phaseBrush, stroke);

        var countdown = $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";
        Ui.Label(dc, theme, countdown, theme.Countdown, new RectF(ring.X, center.Y - 15f * f, d, 24f * f), theme.Primary, TextAlignment.Center);
        Ui.Label(dc, theme, isFocus ? Strings.PomodoroFocus : Strings.Minutes((int)model.CurrentDuration.TotalMinutes),
            theme.Small, new RectF(ring.X, center.Y + 9f * f, d, 14f * f), theme.Secondary, TextAlignment.Center);

        // Fase y sesión.
        float x0 = ring.Right + 18f * f;
        var s = _service.Settings;
        string phase = model.Phase switch
        {
            PomodoroPhase.ShortBreak => Strings.PomodoroShortBreak,
            PomodoroPhase.LongBreak => Strings.PomodoroLongBreak,
            _ => Strings.PomodoroFocus,
        };
        Ui.Label(dc, theme, phase, theme.TrackTitle, new RectF(x0, 8f * f, w - x0 - 120f * f, 21f * f), theme.Primary);
        int every = Math.Max(1, s.SessionsBeforeLongBreak);
        int session = isFocus ? model.Data.CycleSessions % every + 1 : (model.Data.CycleSessions - 1 + every) % every + 1;
        Ui.Label(dc, theme, $"{Strings.PomodoroSession(session, every)} · {Strings.PomodoroToday(model.Data.CompletedToday)}",
            theme.Caption, new RectF(x0, 30f * f, w - x0, 18f * f), theme.Secondary);
        Ui.Label(dc, theme, $"{s.FocusMinutes} · {s.ShortBreakMinutes} · {s.LongBreakMinutes} min", theme.Small,
            new RectF(w - 120f * f, 10f * f, 120f * f, 16f * f), theme.Tertiary, TextAlignment.Trailing);

        // Botones.
        string label = model.IsRunning ? Strings.PomodoroPause : model.IsActive ? Strings.PomodoroResume : Strings.PomodoroStart;
        var icon = model.IsRunning ? IconKind.Pause : IconKind.Play;
        float bw = Ui.ButtonWidth(theme, label, icon: true);
        var toggle = Zone(new RectF(x0, h - 36f * f, bw, 28f * f), "toggle");
        Ui.Button(dc, theme, toggle, label, primary: true, IsHover("toggle"), icon);
        var reset = Zone(new RectF(toggle.Right + 10f * f, toggle.Y, 28f * f, 28f * f), "reset");
        Ui.IconButton(dc, theme, reset, IconKind.Restart, IsHover("reset"), 0.62f, theme.Secondary);
        var skip = Zone(new RectF(reset.Right + 4f * f, toggle.Y, 28f * f, 28f * f), "skip");
        Ui.IconButton(dc, theme, skip, IconKind.Next, IsHover("skip"), 0.58f, theme.Secondary);
    }

    /// <summary>Arco desde las 12 en punto en sentido horario, proporcional a <paramref name="progress"/>.</summary>
    private void DrawArc(ID2D1DeviceContext dc, Theme theme, Vector2 center, float radius, float progress, ID2D1Brush brush, float stroke)
    {
        if (Context is not { } c || progress <= 0.001f)
        {
            return;
        }
        progress = MathF.Min(progress, 0.9999f);
        float angle = progress * MathF.Tau;
        var start = new Vector2(center.X, center.Y - radius);
        var end = new Vector2(center.X + radius * MathF.Sin(angle), center.Y - radius * MathF.Cos(angle));
        using var geometry = c.Graphics.D2DFactory.CreatePathGeometry();
        using (var sink = geometry.Open())
        {
            sink.BeginFigure(start, FigureBegin.Hollow);
            sink.AddArc(new ArcSegment
            {
                Point = end,
                Size = new Size(radius, radius),
                SweepDirection = SweepDirection.Clockwise,
                ArcSize = progress > 0.5f ? ArcSize.Large : ArcSize.Small,
            });
            sink.EndFigure(FigureEnd.Open);
            sink.Close();
        }
        using var style = c.Graphics.D2DFactory.CreateStrokeStyle(new StrokeStyleProperties1 { StartCap = CapStyle.Round, EndCap = CapStyle.Round });
        dc.DrawGeometry(geometry, brush, stroke, style);
    }
}
