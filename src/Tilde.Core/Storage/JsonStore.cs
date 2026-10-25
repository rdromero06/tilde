using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Tilde.Core.Productivity;

namespace Tilde.Core.Storage;

/// <summary>
/// Guarda y carga un objeto en un archivo JSON de la carpeta de datos. La escritura es
/// atómica (archivo temporal + renombrado) y un archivo dañado no rompe la app: se aparta
/// como «.bad» y se empieza de cero.
/// </summary>
public sealed class JsonStore<T>(string path, JsonTypeInfo<T> typeInfo) where T : class, new()
{
    public string Path { get; } = path;

    public T Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                return JsonSerializer.Deserialize(File.ReadAllText(Path), typeInfo) ?? new T();
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"{System.IO.Path.GetFileName(Path)} no se pudo leer; se empieza de cero ({ex.Message})");
            try { File.Copy(Path, Path + ".bad", overwrite: true); } catch { }
        }
        return new T();
    }

    public void Save(T value)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var tmp = Path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, typeInfo));
            File.Move(tmp, Path, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error($"No se pudo guardar {System.IO.Path.GetFileName(Path)}", ex);
        }
    }
}

/// <summary>Serializadores generados en compilación para los datos de productividad.</summary>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TasksData))]
[JsonSerializable(typeof(WaterData))]
[JsonSerializable(typeof(PomodoroData))]
public sealed partial class DataJsonContext : JsonSerializerContext;
