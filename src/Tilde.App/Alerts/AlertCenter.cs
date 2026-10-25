using Tilde.Platform;
using Tilde.Render;

namespace Tilde.App.Alerts;

/// <summary>Un botón de un aviso.</summary>
internal sealed record AlertAction(string Label, Action Invoke, bool Primary = false);

/// <summary>
/// Un aviso que aparece saliendo de la píldora (fin de Pomodoro, beber agua, tarea vencida).
/// </summary>
/// <param name="AutoDismiss">Se cierra solo tras este tiempo (null = hasta que actúes).</param>
/// <param name="Key">Identificador para no repetir el mismo aviso en la cola.</param>
internal sealed record Alert(
    IconKind Icon,
    string Title,
    string Message,
    IReadOnlyList<AlertAction> Actions,
    TimeSpan? AutoDismiss,
    SoundKind? Sound,
    string? Key = null);

/// <summary>
/// Cola de avisos: se muestran de uno en uno. Si llega un aviso con la misma clave que otro
/// en espera, sustituye al anterior (p. ej. dos recordatorios de agua seguidos).
/// </summary>
internal sealed class AlertCenter
{
    private readonly List<Alert> _queue = [];

    public Alert? Current { get; private set; }

    /// <summary>Cambió el aviso actual (o ya no hay ninguno).</summary>
    public event Action? Changed;

    public void Show(Alert alert)
    {
        if (alert.Key is { } key)
        {
            _queue.RemoveAll(a => a.Key == key);
            if (Current?.Key == key)
            {
                Current = alert;
                Changed?.Invoke();
                return;
            }
        }
        _queue.Add(alert);
        if (Current is null)
        {
            Next();
        }
    }

    /// <summary>Cierra el aviso actual y pasa al siguiente, si hay.</summary>
    public void Dismiss() => Next();

    /// <summary>Quita un aviso pendiente o visible por su clave (p. ej. la tarea ya está hecha).</summary>
    public void Remove(string key)
    {
        _queue.RemoveAll(a => a.Key == key);
        if (Current?.Key == key)
        {
            Next();
        }
    }

    private void Next()
    {
        if (_queue.Count > 0)
        {
            Current = _queue[0];
            _queue.RemoveAt(0);
        }
        else
        {
            Current = null;
        }
        Changed?.Invoke();
    }
}
