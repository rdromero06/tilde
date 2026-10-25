using Tilde.App.Alerts;
using Tilde.Core;
using Tilde.Core.Productivity;
using Tilde.Core.Settings;
using Tilde.Core.Storage;
using Tilde.Platform;
using Tilde.Render;

namespace Tilde.App.Productivity;

/// <summary>Formatos de cantidades de agua.</summary>
internal static class Amounts
{
    /// <summary>750 → «750 ml»; 1250 → «1,25 L» (con el separador decimal del idioma).</summary>
    public static string Ml(int ml)
    {
        if (ml < 1000)
        {
            return $"{ml} ml";
        }
        var liters = (ml / 1000.0).ToString(ml % 100 == 0 ? "0.#" : "0.##", System.Globalization.CultureInfo.InvariantCulture);
        return liters.Replace(".", Strings.DecimalSeparator) + " L";
    }
}

/// <summary>
/// Pomodoro con avisos. Un temporizador de un solo disparo para el final de la fase y otro para
/// el cambio de minuto que muestra la píldora: nada se comprueba periódicamente.
/// </summary>
internal sealed class PomodoroService : IDisposable
{
    private readonly SettingsStore _settings;
    private readonly AlertCenter _alerts;
    private readonly JsonStore<PomodoroData> _store;
    private readonly UiTimer _endTimer;
    private readonly UiTimer _minuteTimer;

    public PomodoroService(SettingsStore settings, AlertCenter alerts)
    {
        _settings = settings;
        _alerts = alerts;
        _store = new JsonStore<PomodoroData>(Path.Combine(AppPaths.DataDirectory, "pomodoro.json"), DataJsonContext.Default.PomodoroData);
        Model = new Pomodoro(_store.Load(), () => _settings.Current.Pomodoro);
        _endTimer = new UiTimer(OnPhaseEnd);
        _minuteTimer = new UiTimer(OnMinute);

        var now = DateTimeOffset.Now;
        Model.Rollover(DateOnly.FromDateTime(now.LocalDateTime));
        if (Model.IsDue(now))
        {
            // Terminó mientras Tilde estaba cerrado: se cuenta, pero la siguiente fase no arranca sola.
            Model.Complete(now, allowAutoStart: false);
        }
        Commit();
    }

    public Pomodoro Model { get; }

    /// <summary>Cambió el estado (empezar, pausar, cambio de fase).</summary>
    public event Action? Changed;

    /// <summary>Cambió el minuto restante (para la píldora).</summary>
    public event Action? MinuteChanged;

    public PomodoroSettings Settings => _settings.Current.Pomodoro;

    /// <summary>Texto para la píldora («18 min»), o null si no hay Pomodoro en curso.</summary>
    public string? PillText
    {
        get
        {
            if (!Settings.ShowInPill || !Model.IsActive)
            {
                return null;
            }
            int minutes = (int)Math.Ceiling(Model.Remaining(DateTimeOffset.Now).TotalMinutes);
            return Strings.Minutes(Math.Max(1, minutes));
        }
    }

    public void Toggle()
    {
        Model.Toggle(DateTimeOffset.Now);
        _alerts.Remove("pomodoro");
        Commit();
    }

    public void Reset()
    {
        Model.Reset();
        Commit();
    }

    public void Skip()
    {
        Model.Skip(DateTimeOffset.Now);
        _alerts.Remove("pomodoro");
        Commit();
    }

    /// <summary>Los ajustes cambiaron (p. ej. otra duración): recalculamos los avisos.</summary>
    public void ApplySettings() => Commit();

    public void Dispose()
    {
        _endTimer.Dispose();
        _minuteTimer.Dispose();
    }

    private void Commit()
    {
        _store.Save(Model.Data);
        Schedule();
        Changed?.Invoke();
        MinuteChanged?.Invoke();
    }

    private void Schedule()
    {
        _endTimer.Stop();
        _minuteTimer.Stop();
        if (!Model.IsRunning || Model.Data.EndsAt is not { } end)
        {
            return;
        }
        var now = DateTimeOffset.Now;
        _endTimer.Start(end - now);
        // El texto de la píldora cambia cuando el resto baja de un minuto entero al anterior.
        var remaining = end - now;
        double shown = Math.Ceiling(remaining.TotalMinutes);
        var untilChange = remaining - TimeSpan.FromMinutes(shown - 1);
        _minuteTimer.Start(untilChange + TimeSpan.FromMilliseconds(20));
    }

    private void OnMinute()
    {
        MinuteChanged?.Invoke();
        Schedule();
    }

    private void OnPhaseEnd()
    {
        var now = DateTimeOffset.Now;
        if (!Model.IsDue(now))
        {
            Schedule();
            return;
        }
        var phase = Model.Complete(now);
        Commit();

        bool running = Model.IsRunning;
        int minutes = (int)Model.CurrentDuration.TotalMinutes;
        Alert alert = phase == PomodoroPhase.Focus
            ? new Alert(IconKind.Timer, Strings.PomodoroBreakOver,
                running ? Strings.PomodoroFocusRunning(minutes) : Strings.PomodoroAnother(minutes),
                running ? [] : [new AlertAction(Strings.PomodoroStart, Toggle, Primary: true)],
                TimeSpan.FromSeconds(15), SoundKind.Chime, "pomodoro")
            : new Alert(IconKind.Timer, Strings.PomodoroFocusDone,
                running ? Strings.PomodoroBreakRunning(minutes) : Strings.PomodoroBreakReady(minutes),
                running ? [new AlertAction(Strings.PomodoroSkipBreak, Skip)] : [new AlertAction(Strings.PomodoroStartBreak, Toggle, Primary: true)],
                TimeSpan.FromSeconds(15), SoundKind.Chime, "pomodoro");
        _alerts.Show(alert);
    }
}

/// <summary>Recordatorio de agua: registro del día, avisos dentro del horario y gráfico.</summary>
internal sealed class WaterService : IDisposable
{
    private static readonly int[] GlassSizes = [150, 200, 250, 330, 500, 750, 1000];
    private static readonly int[] Goals = [1500, 2000, 2500, 3000, 3500];

    private readonly SettingsStore _settings;
    private readonly AlertCenter _alerts;
    private readonly JsonStore<WaterData> _store;
    private readonly UiTimer _reminderTimer;
    private readonly UiTimer _midnightTimer;

    public WaterService(SettingsStore settings, AlertCenter alerts)
    {
        _settings = settings;
        _alerts = alerts;
        _store = new JsonStore<WaterData>(Path.Combine(AppPaths.DataDirectory, "water.json"), DataJsonContext.Default.WaterData);
        Tracker = new WaterTracker(_store.Load());
        Tracker.Prune(Today);
        _reminderTimer = new UiTimer(OnReminder);
        _midnightTimer = new UiTimer(OnMidnight);
        Schedule();
    }

    public WaterTracker Tracker { get; }

    public WaterSettings Settings => _settings.Current.Water;

    public DateTime? NextReminder { get; private set; }

    public event Action? Changed;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    public int TodayTotal => Tracker.TotalOn(Today);

    public int[] TodayHourly => Tracker.HourlyOn(Today);

    public void Drink()
    {
        Tracker.Drink(DateTimeOffset.Now, Settings.GlassMl);
        _alerts.Remove("water");
        Save();
    }

    public void Undo()
    {
        if (Tracker.UndoLast(Today))
        {
            Save();
        }
    }

    /// <summary>Cambia al siguiente tamaño de vaso o botella.</summary>
    public void CycleGlass() => _settings.Update(s => s.Water.GlassMl = NextOf(GlassSizes, s.Water.GlassMl));

    public void CycleGoal()
    {
        _settings.Update(s => s.Water.GoalMl = NextOf(Goals, s.Water.GoalMl));
        Schedule();
        Changed?.Invoke();
    }

    public void ApplySettings()
    {
        Schedule();
        Changed?.Invoke();
    }

    public void Dispose()
    {
        _reminderTimer.Dispose();
        _midnightTimer.Dispose();
    }

    private static int NextOf(int[] values, int current) => values.FirstOrDefault(v => v > current, values[0]);

    private void Save()
    {
        _store.Save(Tracker.Data);
        Schedule();
        Changed?.Invoke();
    }

    private void Schedule()
    {
        var now = DateTime.Now;
        NextReminder = Tracker.NextReminder(now, Settings);
        _reminderTimer.Stop();
        if (NextReminder is { } next)
        {
            _reminderTimer.Start(next - now);
        }
        // A medianoche el gráfico y el total vuelven a cero.
        _midnightTimer.Start(now.Date.AddDays(1) - now + TimeSpan.FromSeconds(1));
    }

    private void OnMidnight()
    {
        Tracker.Prune(Today);
        Schedule();
        Changed?.Invoke();
    }

    private void OnReminder()
    {
        var now = DateTime.Now;
        if (NextReminder is { } due && due > now.AddSeconds(5))
        {
            Schedule(); // Se adelantó (p. ej. cambiaron los ajustes): reprogramamos.
            return;
        }
        Tracker.Data.LastReminder = DateTimeOffset.Now;
        _store.Save(Tracker.Data);
        var s = Settings;
        _alerts.Show(new Alert(IconKind.Water, Strings.WaterReminderTitle,
            Strings.WaterReminderMessage(Amounts.Ml(TodayTotal), Amounts.Ml(s.GoalMl)),
            [new AlertAction(Strings.Later, () => { }), new AlertAction(Strings.WaterDrink(Amounts.Ml(s.GlassMl)), Drink, Primary: true)],
            TimeSpan.FromSeconds(20), SoundKind.Drop, "water"));
        Schedule();
        Changed?.Invoke();
    }
}

/// <summary>Tareas con recordatorio: un único temporizador para el próximo aviso.</summary>
internal sealed class TasksService : IDisposable
{
    public const int SnoozeMinutes = 10;

    private readonly AlertCenter _alerts;
    private readonly JsonStore<TasksData> _store;
    private readonly UiTimer _timer;

    public TasksService(AlertCenter alerts)
    {
        _alerts = alerts;
        _store = new JsonStore<TasksData>(Path.Combine(AppPaths.DataDirectory, "tasks.json"), DataJsonContext.Default.TasksData);
        List = new TaskList(_store.Load());
        if (List.PruneCompleted(DateOnly.FromDateTime(DateTime.Now)) > 0)
        {
            _store.Save(List.Data);
        }
        _timer = new UiTimer(OnDue);
        Schedule();
    }

    public TaskList List { get; }

    public event Action? Changed;

    public void Add(string text)
    {
        if (List.Add(text, DateTime.Now) is not null)
        {
            Commit();
        }
    }

    public void Edit(Guid id, string text)
    {
        List.Edit(id, text, DateTime.Now);
        _alerts.Remove(Key(id));
        Commit();
    }

    public void Toggle(Guid id)
    {
        List.Toggle(id, DateTimeOffset.Now);
        _alerts.Remove(Key(id));
        Commit();
    }

    public void Delete(Guid id)
    {
        List.Delete(id);
        _alerts.Remove(Key(id));
        Commit();
    }

    public void CycleColor(Guid id)
    {
        List.CycleColor(id);
        Commit();
    }

    public void Snooze(Guid id)
    {
        List.Snooze(id, DateTimeOffset.Now, SnoozeMinutes);
        Commit();
    }

    public void Dispose() => _timer.Dispose();

    private static string Key(Guid id) => "task:" + id.ToString("N");

    private void Commit()
    {
        _store.Save(List.Data);
        Schedule();
        Changed?.Invoke();
    }

    private void Schedule()
    {
        _timer.Stop();
        if (List.NextReminder() is { } next)
        {
            var wait = next - DateTimeOffset.Now;
            _timer.Start(wait > TimeSpan.Zero ? wait : TimeSpan.FromMilliseconds(500));
        }
    }

    private void OnDue()
    {
        foreach (var task in List.Due(DateTimeOffset.Now))
        {
            task.Notified = true;
            var id = task.Id;
            _alerts.Show(new Alert(IconKind.Bell, Strings.TasksTimeUp, task.Title,
                [new AlertAction(Strings.TasksSnooze(SnoozeMinutes), () => Snooze(id)), new AlertAction(Strings.TasksDone, () => Toggle(id), Primary: true)],
                AutoDismiss: null, SoundKind.Bell, Key(id)));
        }
        Commit();
    }
}
