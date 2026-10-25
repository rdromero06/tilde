using Tilde.Core.Settings;

namespace Tilde.Core.Productivity;

public enum PomodoroPhase
{
    Focus,
    ShortBreak,
    LongBreak,
}

/// <summary>Estado persistente del Pomodoro (pomodoro.json).</summary>
public sealed class PomodoroData
{
    public PomodoroPhase Phase { get; set; }

    public bool Running { get; set; }

    /// <summary>Instante en que acaba la fase si está en marcha.</summary>
    public DateTimeOffset? EndsAt { get; set; }

    /// <summary>Segundos que quedaban al pausar (0 = fase sin empezar).</summary>
    public double PausedRemainingSeconds { get; set; }

    /// <summary>Focos completados en el ciclo actual (para saber cuándo toca descanso largo).</summary>
    public int CycleSessions { get; set; }

    /// <summary>Focos completados hoy.</summary>
    public int CompletedToday { get; set; }

    /// <summary>Día al que se refiere <see cref="CompletedToday"/> (yyyy-MM-dd).</summary>
    public string Day { get; set; } = string.Empty;
}

/// <summary>
/// Lógica del Pomodoro. Se basa en la hora de fin y no en contar segundos: si el equipo se
/// suspende o el reloj salta, el tiempo restante sigue siendo correcto.
/// </summary>
public sealed class Pomodoro(PomodoroData data, Func<PomodoroSettings> settings)
{
    public PomodoroData Data { get; } = data;

    public PomodoroPhase Phase => Data.Phase;

    public bool IsRunning => Data.Running;

    public TimeSpan Duration(PomodoroPhase phase)
    {
        var s = settings();
        int minutes = phase switch
        {
            PomodoroPhase.ShortBreak => s.ShortBreakMinutes,
            PomodoroPhase.LongBreak => s.LongBreakMinutes,
            _ => s.FocusMinutes,
        };
        return TimeSpan.FromMinutes(Math.Clamp(minutes, 1, 240));
    }

    public TimeSpan CurrentDuration => Duration(Data.Phase);

    public TimeSpan Remaining(DateTimeOffset now)
    {
        if (Data.Running && Data.EndsAt is { } end)
        {
            var left = end - now;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
        return Data.PausedRemainingSeconds > 0 ? TimeSpan.FromSeconds(Data.PausedRemainingSeconds) : CurrentDuration;
    }

    /// <summary>Progreso de la fase actual de 0 a 1.</summary>
    public double Progress(DateTimeOffset now) => 1 - Remaining(now) / CurrentDuration;

    /// <summary>¿La fase en marcha ya ha llegado a su fin?</summary>
    public bool IsDue(DateTimeOffset now) => Data.Running && Data.EndsAt is { } end && end <= now;

    /// <summary>¿Hay algo en curso o a medias (para mostrarlo en la píldora)?</summary>
    public bool IsActive => Data.Running || Data.PausedRemainingSeconds > 0;

    public void Start(DateTimeOffset now)
    {
        if (Data.Running)
        {
            return;
        }
        Data.EndsAt = now + Remaining(now);
        Data.Running = true;
        Data.PausedRemainingSeconds = 0;
    }

    public void Pause(DateTimeOffset now)
    {
        if (!Data.Running)
        {
            return;
        }
        Data.PausedRemainingSeconds = Math.Max(1, Remaining(now).TotalSeconds);
        Data.Running = false;
        Data.EndsAt = null;
    }

    public void Toggle(DateTimeOffset now)
    {
        if (Data.Running) Pause(now);
        else Start(now);
    }

    /// <summary>Vuelve al principio de la fase actual, parado.</summary>
    public void Reset()
    {
        Data.Running = false;
        Data.EndsAt = null;
        Data.PausedRemainingSeconds = 0;
    }

    /// <summary>Pasa a la siguiente fase sin contar la actual como completada.</summary>
    public void Skip(DateTimeOffset now) => Advance(now, completed: false, allowAutoStart: Data.Running);

    /// <summary>La fase ha terminado: la cuenta y prepara (o arranca) la siguiente.</summary>
    public PomodoroPhase Complete(DateTimeOffset now, bool allowAutoStart = true)
    {
        Advance(now, completed: true, allowAutoStart);
        return Data.Phase;
    }

    /// <summary>Cambio de día: el contador de «hoy» vuelve a cero.</summary>
    public void Rollover(DateOnly today)
    {
        var key = today.ToString("yyyy-MM-dd");
        if (Data.Day != key)
        {
            Data.Day = key;
            Data.CompletedToday = 0;
        }
    }

    private void Advance(DateTimeOffset now, bool completed, bool allowAutoStart)
    {
        var s = settings();
        PomodoroPhase next;
        if (Data.Phase == PomodoroPhase.Focus)
        {
            if (completed)
            {
                Rollover(DateOnly.FromDateTime(now.LocalDateTime));
                Data.CycleSessions++;
                Data.CompletedToday++;
            }
            int every = Math.Max(1, s.SessionsBeforeLongBreak);
            next = completed && Data.CycleSessions % every == 0 ? PomodoroPhase.LongBreak : PomodoroPhase.ShortBreak;
        }
        else
        {
            if (Data.Phase == PomodoroPhase.LongBreak)
            {
                Data.CycleSessions = 0;
            }
            next = PomodoroPhase.Focus;
        }

        Data.Phase = next;
        Data.PausedRemainingSeconds = 0;
        bool autoStart = allowAutoStart && (next == PomodoroPhase.Focus ? s.AutoStartFocus : s.AutoStartBreaks);
        Data.Running = autoStart;
        Data.EndsAt = autoStart ? now + Duration(next) : null;
    }
}
