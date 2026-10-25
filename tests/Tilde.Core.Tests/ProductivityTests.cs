using Tilde.Core.Productivity;
using Tilde.Core.Settings;

namespace Tilde.Core.Tests;

public class QuickEntryTests
{
    private static readonly DateTime Now = new(2026, 10, 25, 14, 10, 0);

    [Theory]
    [InlineData("Llamar a Ana 18:30", "Llamar a Ana", 18, 30, 0)]
    [InlineData("Llamar a Ana a las 18:30", "Llamar a Ana", 18, 30, 0)]
    [InlineData("Revisar correo 9.05", "Revisar correo", 9, 5, 1)]   // Ya pasó: mañana.
    [InlineData("Dentista mañana 10:00", "Dentista", 10, 0, 1)]
    [InlineData("Gym at 7pm", "Gym", 19, 0, 0)]
    [InlineData("Pagar luz a las 20", "Pagar luz", 20, 0, 0)]
    [InlineData("Call mom tomorrow at 8", "Call mom", 8, 0, 1)]
    public void Reconoce_horas(string text, string title, int hour, int minute, int addDays)
    {
        var (t, at) = QuickEntry.Parse(text, Now);
        Assert.Equal(title, t);
        Assert.Equal(Now.Date.AddDays(addDays).AddHours(hour).AddMinutes(minute), at);
    }

    [Theory]
    [InlineData("Sacar la pizza en 20 min", "Sacar la pizza", 20)]
    [InlineData("Estirar dentro de 2 horas", "Estirar", 120)]
    [InlineData("Stand up in 45m", "Stand up", 45)]
    [InlineData("Té +5m", "Té", 5)]
    public void Reconoce_plazos(string text, string title, int minutes)
    {
        var (t, at) = QuickEntry.Parse(text, Now);
        Assert.Equal(title, t);
        Assert.Equal(Now.AddMinutes(minutes), at);
    }

    [Theory]
    [InlineData("Comprar 3 manzanas")]
    [InlineData("Leer el capítulo 12")]
    [InlineData("18:30")]
    public void Sin_hora_clara_no_inventa_recordatorio(string text)
    {
        var (t, at) = QuickEntry.Parse(text, Now);
        Assert.Equal(text, t);
        Assert.Null(at);
    }

    [Fact]
    public void Format_permite_editar_y_volver_a_leer()
    {
        var at = Now.Date.AddDays(1).AddHours(9);
        var text = QuickEntry.Format("Dentista", at, Now);
        Assert.Equal(("Dentista", (DateTime?)at), QuickEntry.Parse(text, Now));
    }
}

public class TaskListTests
{
    private static readonly DateTime Now = new(2026, 10, 25, 14, 0, 0);

    [Fact]
    public void Ordena_pendientes_por_hora_y_hechas_al_final()
    {
        var list = new TaskList(new TasksData());
        var a = list.Add("Sin hora", Now)!;
        var b = list.Add("Tarde 19:00", Now)!;
        var c = list.Add("Pronto 15:00", Now)!;
        list.Toggle(a.Id, Now);
        Assert.Equal([c.Id, b.Id, a.Id], list.Ordered().Select(t => t.Id));
    }

    [Fact]
    public void Avisos_vencidos_proximo_y_posponer()
    {
        var list = new TaskList(new TasksData());
        var t = list.Add("Algo en 10 min", Now)!;
        Assert.Equal(Now.AddMinutes(10), list.NextReminder()!.Value.LocalDateTime);
        Assert.Empty(list.Due(Now));
        Assert.Single(list.Due(Now.AddMinutes(10)));

        t.Notified = true;
        Assert.Null(list.NextReminder());
        list.Snooze(t.Id, Now.AddMinutes(10), 10);
        Assert.Equal(Now.AddMinutes(20), list.NextReminder()!.Value.LocalDateTime);
    }

    [Fact]
    public void Colores_ciclicos_y_limpieza_de_hechas_antiguas()
    {
        var list = new TaskList(new TasksData());
        var t = list.Add("x", Now)!;
        int start = t.Color;
        for (int i = 0; i < TaskList.ColorCount; i++) list.CycleColor(t.Id);
        Assert.Equal(start, t.Color);

        list.Toggle(t.Id, Now.AddDays(-1));
        Assert.Equal(1, list.PruneCompleted(DateOnly.FromDateTime(Now)));
        Assert.Empty(list.Data.Tasks);
    }
}

public class WaterTrackerTests
{
    private static readonly WaterSettings Settings = new()
    {
        GoalMl = 1000, ReminderStart = "09:00", ReminderEnd = "21:00", ReminderIntervalMinutes = 60, RemindersEnabled = true,
    };

    private static DateTime At(int h, int m = 0) => new(2026, 10, 25, h, m, 0);

    [Fact]
    public void Suma_por_dia_y_por_hora_y_deshace()
    {
        var w = new WaterTracker(new WaterData());
        w.Drink(At(10, 5), 250);
        w.Drink(At(10, 40), 250);
        w.Drink(At(13), 330);
        var day = DateOnly.FromDateTime(At(0));
        Assert.Equal(830, w.TotalOn(day));
        Assert.Equal(500, w.HourlyOn(day)[10]);
        Assert.True(w.UndoLast(day));
        Assert.Equal(500, w.TotalOn(day));
    }

    [Fact]
    public void Aviso_una_hora_despues_del_ultimo_trago()
    {
        var w = new WaterTracker(new WaterData());
        w.Drink(At(10), 250);
        Assert.Equal(At(11), w.NextReminder(At(10, 30), Settings));
    }

    [Fact]
    public void Sin_beber_hoy_avisa_al_empezar_el_horario_o_enseguida()
    {
        var w = new WaterTracker(new WaterData());
        Assert.Equal(At(9), w.NextReminder(At(7), Settings));
        Assert.Equal(At(14, 2), w.NextReminder(At(14), Settings));
    }

    [Fact]
    public void Fuera_de_horario_o_con_la_meta_cumplida_hasta_manana()
    {
        var w = new WaterTracker(new WaterData());
        var tomorrow9 = At(9).AddDays(1);
        Assert.Equal(tomorrow9, w.NextReminder(At(22), Settings));
        w.Drink(At(12), 1000);
        Assert.Equal(tomorrow9, w.NextReminder(At(12, 30), Settings));
    }

    [Fact]
    public void Desactivado_no_avisa() =>
        Assert.Null(new WaterTracker(new WaterData()).NextReminder(At(10), new WaterSettings { RemindersEnabled = false }));
}

public class PomodoroTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 25, 10, 0, 0, TimeSpan.FromHours(1));

    private static (Pomodoro P, PomodoroSettings S) Create()
    {
        var s = new PomodoroSettings { FocusMinutes = 25, ShortBreakMinutes = 5, LongBreakMinutes = 15, SessionsBeforeLongBreak = 2 };
        return (new Pomodoro(new PomodoroData(), () => s), s);
    }

    [Fact]
    public void Cuenta_atras_pausa_y_reanuda()
    {
        var (p, _) = Create();
        Assert.Equal(TimeSpan.FromMinutes(25), p.Remaining(T0));
        p.Start(T0);
        Assert.Equal(TimeSpan.FromMinutes(15), p.Remaining(T0.AddMinutes(10)));
        p.Pause(T0.AddMinutes(10));
        Assert.Equal(TimeSpan.FromMinutes(15), p.Remaining(T0.AddHours(3)));
        p.Start(T0.AddHours(3));
        Assert.True(p.IsDue(T0.AddHours(3).AddMinutes(15)));
    }

    [Fact]
    public void Ciclo_foco_descanso_corto_y_largo()
    {
        var (p, _) = Create();
        p.Start(T0);
        Assert.Equal(PomodoroPhase.ShortBreak, p.Complete(T0.AddMinutes(25)));
        Assert.True(p.IsRunning); // Los descansos empiezan solos.
        Assert.Equal(PomodoroPhase.Focus, p.Complete(T0.AddMinutes(30)));
        Assert.False(p.IsRunning); // El foco espera a que lo empieces.
        p.Start(T0.AddMinutes(31));
        Assert.Equal(PomodoroPhase.LongBreak, p.Complete(T0.AddMinutes(56)));
        Assert.Equal(2, p.Data.CompletedToday);
    }

    [Fact]
    public void Saltar_no_cuenta_como_completado()
    {
        var (p, _) = Create();
        p.Start(T0);
        p.Skip(T0.AddMinutes(3));
        Assert.Equal(PomodoroPhase.ShortBreak, p.Phase);
        Assert.Equal(0, p.Data.CompletedToday);
    }

    [Fact]
    public void Reiniciar_vuelve_al_principio_de_la_fase()
    {
        var (p, _) = Create();
        p.Start(T0);
        p.Reset();
        Assert.False(p.IsActive);
        Assert.Equal(TimeSpan.FromMinutes(25), p.Remaining(T0.AddMinutes(9)));
    }
}
