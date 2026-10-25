using System.Text.Json;

namespace Tilde.Core.Settings;

/// <summary>
/// Carga, guarda y vigila settings.json. Los cambios hechos desde fuera (a mano o desde la
/// ventana de Ajustes, que es otro proceso) se detectan con FileSystemWatcher, que se apoya
/// en notificaciones del sistema de archivos: no hay sondeo.
/// </summary>
public sealed class SettingsStore : IDisposable
{
    private readonly string _path;
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;
    private string? _lastText;

    public SettingsStore(string path)
    {
        _path = path;
        Current = Load(out _lastText);
    }

    public AppSettings Current { get; private set; }

    /// <summary>
    /// El archivo ha cambiado en disco. Se lanza en un hilo del pool: el receptor debe
    /// pasar al hilo de UI y llamar a <see cref="TryReload"/>.
    /// </summary>
    public event Action? FileChanged;

    /// <summary>Aplica un cambio y lo guarda inmediatamente.</summary>
    public void Update(Action<AppSettings> change)
    {
        change(Current);
        Save();
    }

    public void Save()
    {
        var text = JsonSerializer.Serialize(Current, SettingsJsonContext.Default.AppSettings);
        _lastText = text;
        try
        {
            // Escritura atómica: si algo falla a mitad, el archivo anterior queda intacto.
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, text);
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("No se pudieron guardar los ajustes", ex);
        }
    }

    /// <summary>Relee el archivo. Devuelve true si el contenido cambió y es válido.</summary>
    public bool TryReload()
    {
        string text;
        try
        {
            text = File.ReadAllText(_path);
        }
        catch (IOException)
        {
            return false; // Otro proceso lo está escribiendo; llegará otra notificación.
        }

        if (text == _lastText)
        {
            return false; // Es nuestro propio guardado.
        }

        try
        {
            var parsed = JsonSerializer.Deserialize(text, SettingsJsonContext.Default.AppSettings);
            if (parsed is null)
            {
                return false;
            }
            Current = parsed;
            _lastText = text;
            return true;
        }
        catch (JsonException ex)
        {
            Log.Warn($"settings.json no es válido, se ignora el cambio ({ex.Message})");
            return false;
        }
    }

    /// <summary>Empieza a vigilar el archivo de ajustes.</summary>
    public void StartWatching()
    {
        var dir = Path.GetDirectoryName(_path)!;
        _debounce = new Timer(_ => FileChanged?.Invoke());
        _watcher = new FileSystemWatcher(dir, Path.GetFileName(_path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        // Los editores disparan varios eventos seguidos: agrupamos en 150 ms.
        FileSystemEventHandler onChange = (_, _) => _debounce.Change(150, Timeout.Infinite);
        _watcher.Changed += onChange;
        _watcher.Created += onChange;
        _watcher.Renamed += (_, _) => _debounce.Change(150, Timeout.Infinite);
        _watcher.EnableRaisingEvents = true;
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce?.Dispose();
    }

    private AppSettings Load(out string? text)
    {
        text = null;
        if (!File.Exists(_path))
        {
            return new AppSettings();
        }
        try
        {
            text = File.ReadAllText(_path);
            return JsonSerializer.Deserialize(text, SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            // Guardamos una copia del archivo dañado para no perder lo que hubiera.
            Log.Warn($"No se pudo leer settings.json; se usan valores por defecto ({ex.Message})");
            try { File.Copy(_path, _path + ".bad", overwrite: true); } catch { }
            text = null;
            return new AppSettings();
        }
    }
}
