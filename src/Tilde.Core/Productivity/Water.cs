using System.Globalization;
using Tilde.Core.Settings;

namespace Tilde.Core.Productivity;

/// <summary>Un trago registrado.</summary>
public sealed class WaterEntry
{
    public DateTimeOffset At { get; set; }

    public int Ml { get; set; }
}

/// <summary>Registro de agua (water.json): días «yyyy-MM-dd» → tragos.</summary>
public sealed class WaterData
{
    public Dictionary<string, List<WaterEntry>> Days { get; set; } = [];

    /// <summary>Último aviso mostrado (para no repetir avisos seguidos).</summary>
    public DateTimeOffset? LastReminder { get; set; }
}

/// <summary>Cálculos del recordatorio de agua.</summary>
public sealed class WaterTracker(WaterData data)
{
    /// <summary>Días de historial que se conservan.</summary>
    public const int KeepDays = 60;

    public WaterData Data { get; } = data;

    public static string Key(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public IReadOnlyList<WaterEntry> EntriesOn(DateOnly day) =>
        Data.Days.TryGetValue(Key(day), out var list) ? list : [];

    public int TotalOn(DateOnly day) => EntriesOn(day).Sum(e => e.Ml);

    /// <summary>Mililitros por hora del día (24 posiciones), para el gráfico.</summary>
    public int[] HourlyOn(DateOnly day)
    {
        var hours = new int[24];
        foreach (var e in EntriesOn(day))
        {
            hours[e.At.LocalDateTime.Hour] += e.Ml;
        }
        return hours;
    }

    public DateTimeOffset? LastDrinkOn(DateOnly day) => EntriesOn(day) is { Count: > 0 } list ? list[^1].At : null;

    public void Drink(DateTimeOffset now, int ml)
    {
        var key = Key(DateOnly.FromDateTime(now.LocalDateTime));
        if (!Data.Days.TryGetValue(key, out var list))
        {
            Data.Days[key] = list = [];
        }
        list.Add(new WaterEntry { At = now, Ml = Math.Clamp(ml, 1, 5000) });
    }

    /// <summary>Deshace el último trago del día. Devuelve false si no había ninguno.</summary>
    public bool UndoLast(DateOnly day)
    {
        if (Data.Days.TryGetValue(Key(day), out var list) && list.Count > 0)
        {
            list.RemoveAt(list.Count - 1);
            return true;
        }
        return false;
    }

    /// <summary>Borra todo lo registrado en un día y devuelve lo borrado (para poder deshacerlo).</summary>
    public IReadOnlyList<WaterEntry> ResetDay(DateOnly day)
    {
        if (Data.Days.Remove(Key(day), out var removed))
        {
            return removed;
        }
        return [];
    }

    /// <summary>Vuelve a poner unos tragos borrados con <see cref="ResetDay"/>.</summary>
    public void Restore(IReadOnlyList<WaterEntry> entries)
    {
        foreach (var e in entries)
        {
            var key = Key(DateOnly.FromDateTime(e.At.LocalDateTime));
            if (!Data.Days.TryGetValue(key, out var list))
            {
                Data.Days[key] = list = [];
            }
            list.Add(e);
            list.Sort((a, b) => a.At.CompareTo(b.At));
        }
    }

    /// <summary>Borra los días más antiguos que <see cref="KeepDays"/>.</summary>
    public void Prune(DateOnly today)
    {
        var limit = Key(today.AddDays(-KeepDays));
        foreach (var key in Data.Days.Keys.Where(k => string.CompareOrdinal(k, limit) < 0).ToList())
        {
            Data.Days.Remove(key);
        }
    }

    /// <summary>
    /// Cuándo toca el próximo aviso. Reglas: solo dentro del horario; cada «intervalo» desde el
    /// último trago o aviso; si hoy aún no has bebido, al empezar el horario; si ya cumpliste la
    /// meta, hasta mañana nada. Nunca devuelve algo en el pasado.
    /// </summary>
    public DateTime? NextReminder(DateTime now, WaterSettings s)
    {
        if (!s.RemindersEnabled)
        {
            return null;
        }
        var start = ParseTime(s.ReminderStart, new TimeOnly(9, 0));
        var end = ParseTime(s.ReminderEnd, new TimeOnly(21, 0));
        if (end <= start)
        {
            end = start.AddHours(12);
        }
        var interval = TimeSpan.FromMinutes(Math.Clamp(s.ReminderIntervalMinutes, 10, 600));

        var today = DateOnly.FromDateTime(now);
        var windowStart = today.ToDateTime(start);
        var windowEnd = today.ToDateTime(end);
        var tomorrowStart = today.AddDays(1).ToDateTime(start);

        if (TotalOn(today) >= s.GoalMl || now >= windowEnd)
        {
            return tomorrowStart;
        }

        DateTime? lastActivity = Max(LastDrinkOn(today)?.LocalDateTime,
            Data.LastReminder is { } r && DateOnly.FromDateTime(r.LocalDateTime) == today ? r.LocalDateTime : null);
        var candidate = lastActivity is { } last ? last + interval : windowStart;
        if (candidate < windowStart)
        {
            candidate = windowStart;
        }
        if (candidate < now)
        {
            candidate = now.AddMinutes(2); // Si ya tocaba (p. ej. al encender el PC), dentro de un momento.
        }
        return candidate > windowEnd ? tomorrowStart : candidate;
    }

    public static TimeOnly ParseTime(string? text, TimeOnly fallback) =>
        TimeOnly.TryParseExact(text?.Trim(), ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : fallback;

    private static DateTime? Max(DateTime? a, DateTime? b) => a is null ? b : b is null ? a : a > b ? a : b;
}
