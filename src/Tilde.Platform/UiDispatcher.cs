using System.Collections.Concurrent;
using Tilde.Core;
using static Tilde.Platform.Interop.Native;

namespace Tilde.Platform;

/// <summary>
/// Lleva trabajo al hilo de interfaz. Windows entrega muchos avisos (multimedia, audio,
/// temporizadores) en hilos del sistema; la interfaz solo se toca desde su propio hilo. Se usa
/// una ventana de solo mensajes: encolar un trabajo publica un mensaje y el bucle de mensajes lo
/// ejecuta. Sin hilos extra ni esperas activas.
/// </summary>
public sealed class UiDispatcher : Win32Window
{
    private const uint WM_APP_INVOKE = WM_APP + 1;
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();
    private int _messagePending;

    public UiDispatcher()
    {
        CreateWindow("TildeDispatcher", 0, 0, 0, 0, 0, 0, HWND_MESSAGE);
        ThreadId = Environment.CurrentManagedThreadId;
    }

    public int ThreadId { get; }

    public void Post(SendOrPostCallback callback, object? state)
    {
        _queue.Enqueue((callback, state));
        // Un único mensaje pendiente basta para vaciar toda la cola.
        if (Interlocked.Exchange(ref _messagePending, 1) == 0)
        {
            PostMessageW(Handle, WM_APP_INVOKE, 0, 0);
        }
    }

    protected override nint WndProc(uint msg, nint wParam, nint lParam)
    {
        if (msg != WM_APP_INVOKE)
        {
            return base.WndProc(msg, wParam, lParam);
        }
        Volatile.Write(ref _messagePending, 0);
        while (_queue.TryDequeue(out var item))
        {
            try
            {
                item.Callback(item.State);
            }
            catch (Exception ex)
            {
                Log.Error("Error en una tarea del hilo de interfaz", ex);
            }
        }
        return 0;
    }
}

/// <summary>
/// Contexto de sincronización del hilo de interfaz: hace que el código tras un <c>await</c>
/// vuelva automáticamente a este hilo.
/// </summary>
public sealed class UiSynchronizationContext(UiDispatcher dispatcher) : SynchronizationContext
{
    public override void Post(SendOrPostCallback d, object? state) => dispatcher.Post(d, state);

    public override void Send(SendOrPostCallback d, object? state)
    {
        if (Environment.CurrentManagedThreadId == dispatcher.ThreadId)
        {
            d(state);
            return;
        }
        using var done = new ManualResetEventSlim();
        dispatcher.Post(_ =>
        {
            try { d(state); }
            finally { done.Set(); }
        }, null);
        done.Wait();
    }

    public override SynchronizationContext CreateCopy() => this;
}

/// <summary>
/// Temporizador de un solo disparo que ejecuta su acción en el hilo de interfaz. Sirve para
/// programar «el próximo momento en que algo cambia» (siguiente línea de la letra, siguiente
/// segundo del progreso) en lugar de comprobarlo periódicamente.
/// </summary>
public sealed class UiTimer : IDisposable
{
    private readonly SynchronizationContext _context;
    private readonly Action _action;
    private readonly Timer _timer;
    private int _generation;

    public UiTimer(Action action)
    {
        _context = SynchronizationContext.Current ?? throw new InvalidOperationException("Crear en el hilo de interfaz");
        _action = action;
        _timer = new Timer(OnElapsed);
    }

    /// <summary>Programa el disparo (sustituye a uno pendiente).</summary>
    public void Start(TimeSpan dueTime)
    {
        Interlocked.Increment(ref _generation);
        long ms = Math.Clamp((long)dueTime.TotalMilliseconds, 1, int.MaxValue);
        _timer.Change(ms, Timeout.Infinite);
    }

    public void Stop()
    {
        Interlocked.Increment(ref _generation);
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    public void Dispose()
    {
        Stop();
        _timer.Dispose();
    }

    private void OnElapsed(object? _)
    {
        int generation = Volatile.Read(ref _generation);
        _context.Post(_ =>
        {
            // Si entretanto se reprogramó o se paró, este disparo ya no vale.
            if (generation == Volatile.Read(ref _generation))
            {
                _action();
            }
        }, null);
    }
}
