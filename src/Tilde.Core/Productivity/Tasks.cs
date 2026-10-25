using System.Globalization;
using System.Text.RegularExpressions;

namespace Tilde.Core.Productivity;

/// <summary>Una tarea.</summary>
public sealed class TaskItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;

    /// <summary>Índice de color (0–4).</summary>
    public int Color { get; set; }

    /// <summary>Hora del recordatorio, si tiene.</summary>
    public DateTimeOffset? RemindAt { get; set; }

    /// <summary>Ya se mostró el aviso de este recordatorio.</summary>
    public bool Notified { get; set; }

    public bool Done { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? DoneAt { get; set; }
}

/// <summary>Lista de tareas (tasks.json).</summary>
public sealed class TasksData
{
    public List<TaskItem> Tasks { get; set; } = [];
}

/// <summary>Operaciones sobre la lista de tareas.</summary>
public sealed class TaskList(TasksData data)
{
    public const int ColorCount = 5;

    public TasksData Data { get; } = data;

    /// <summary>Pendientes primero (por hora de aviso, luego por creación) y hechas al final.</summary>
    public IReadOnlyList<TaskItem> Ordered() =>
    [
        .. Data.Tasks.Where(t => !t.Done)
            .OrderBy(t => t.RemindAt ?? DateTimeOffset.MaxValue)
            .ThenBy(t => t.CreatedAt),
        .. Data.Tasks.Where(t => t.Done).OrderBy(t => t.DoneAt),
    ];

    /// <summary>Añade una tarea escrita en lenguaje natural («Llamar a Ana 18:30»).</summary>
    public TaskItem? Add(string text, DateTime now)
    {
        var (title, at) = QuickEntry.Parse(text, now);
        if (title.Length == 0)
        {
            return null;
        }
        var task = new TaskItem
        {
            Title = title,
            RemindAt = at,
            CreatedAt = now,
            Color = Data.Tasks.Count % ColorCount,
        };
        Data.Tasks.Add(task);
        return task;
    }

    /// <summary>Reescribe título y hora a partir de texto.</summary>
    public void Edit(Guid id, string text, DateTime now)
    {
        if (Find(id) is not { } task)
        {
            return;
        }
        var (title, at) = QuickEntry.Parse(text, now);
        if (title.Length == 0)
        {
            return;
        }
        task.Title = title;
        if (at != task.RemindAt?.LocalDateTime)
        {
            task.RemindAt = at;
            task.Notified = false;
        }
    }

    public void Toggle(Guid id, DateTimeOffset now)
    {
        if (Find(id) is { } task)
        {
            task.Done = !task.Done;
            task.DoneAt = task.Done ? now : null;
        }
    }

    public void Delete(Guid id) => Data.Tasks.RemoveAll(t => t.Id == id);

    public void CycleColor(Guid id)
    {
        if (Find(id) is { } task)
        {
            task.Color = (task.Color + 1) % ColorCount;
        }
    }

    /// <summary>Pospone el aviso unos minutos desde ahora.</summary>
    public void Snooze(Guid id, DateTimeOffset now, int minutes)
    {
        if (Find(id) is { } task)
        {
            task.RemindAt = now.AddMinutes(minutes);
            task.Notified = false;
        }
    }

    /// <summary>Tareas cuyo aviso ya ha llegado y aún no se mostró.</summary>
    public IReadOnlyList<TaskItem> Due(DateTimeOffset now) =>
        [.. Data.Tasks.Where(t => !t.Done && !t.Notified && t.RemindAt is { } at && at <= now).OrderBy(t => t.RemindAt)];

    /// <summary>Próximo aviso pendiente (para programar un único temporizador).</summary>
    public DateTimeOffset? NextReminder() =>
        Data.Tasks.Where(t => !t.Done && !t.Notified && t.RemindAt is not null).Min(t => t.RemindAt);

    /// <summary>Al empezar el día, quita las tareas hechas en días anteriores.</summary>
    public int PruneCompleted(DateOnly today) =>
        Data.Tasks.RemoveAll(t => t.Done && t.DoneAt is { } d && DateOnly.FromDateTime(d.LocalDateTime) < today);

    public TaskItem? Find(Guid id) => Data.Tasks.Find(t => t.Id == id);
}

/// <summary>
/// Entrada rápida de tareas en lenguaje natural, en español e inglés. Reconoce al final del
/// texto una hora («18:30», «a las 9», «7pm», «mañana 9:00») o un plazo («en 20 min», «in 2h»).
/// </summary>
public static partial class QuickEntry
{
    public static (string Title, DateTime? At) Parse(string text, DateTime now)
    {
        text = text.Trim();
        if (Relative().Match(text) is { Success: true } rel)
        {
            int n = int.Parse(rel.Groups["n"].Value, CultureInfo.InvariantCulture);
            bool hours = rel.Groups["u"].Value.StartsWith('h');
            var at = now.AddMinutes(hours ? n * 60 : n);
            return (CleanTitle(text[..rel.Index]), Truncate(at));
        }
        if (Absolute().Match(text) is { Success: true } abs)
        {
            int hour = int.Parse(abs.Groups["h"].Value, CultureInfo.InvariantCulture);
            int minute = abs.Groups["m"].Success ? int.Parse(abs.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
            var meridiem = abs.Groups["ampm"].Value.Replace(".", "").Replace(" ", "").ToLowerInvariant();
            if (meridiem == "pm" && hour < 12) hour += 12;
            if (meridiem == "am" && hour == 12) hour = 0;
            if (hour <= 23 && minute <= 59)
            {
                var at = now.Date.AddHours(hour).AddMinutes(minute);
                bool tomorrow = abs.Groups["tomorrow"].Success;
                if (tomorrow || at <= now)
                {
                    at = at.AddDays(1); // Una hora que ya pasó se entiende como mañana.
                }
                var title = CleanTitle(text[..abs.Index]);
                if (title.Length > 0)
                {
                    return (title, at);
                }
            }
        }
        return (CleanTitle(text), null);
    }

    /// <summary>Texto para editar una tarea: título más su hora («Llamar 18:30»).</summary>
    public static string Format(string title, DateTime? at, DateTime now)
    {
        if (at is not { } time)
        {
            return title;
        }
        var prefix = time.Date > now.Date ? (Strings.Spanish ? "mañana " : "tomorrow ") : string.Empty;
        return $"{title} {prefix}{time:HH:mm}";
    }

    private static string CleanTitle(string s) => s.Trim().TrimEnd(',', '-', '·', ':').Trim();

    private static DateTime Truncate(DateTime t) => new(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0, t.Kind);

    // «en 20 min», «dentro de 2 horas», «in 45m», «+10m»
    [GeneratedRegex(@"\s*(?:\ben\b|\bin\b|\bdentro de\b|\+)\s*(?<n>\d{1,3})\s*(?<u>min(?:uto)?s?|m|h(?:ora|our)?s?|hrs?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Relative();

    // «18:30», «18.30», «a las 9», «at 7pm», «mañana 9:00», «tomorrow at 8», «7 pm»
    [GeneratedRegex(@"\s*(?<tomorrow>\bmañana\b|\btomorrow\b)?\s*(?:\ba las\b|\bat\b|@)?\s*(?<h>\d{1,2})(?:[:.](?<m>\d{2})|(?=\s*(?<ampm>a\.?\s?m\.?|p\.?\s?m\.?)))\s*(?<ampm>a\.?\s?m\.?|p\.?\s?m\.?)?\s*$|\s*(?<tomorrow>\bmañana\b|\btomorrow\b)?\s*(?:\ba las\b|\bat\b)\s*(?<h>\d{1,2})\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Absolute();
}
