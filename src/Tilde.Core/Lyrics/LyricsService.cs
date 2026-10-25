using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tilde.Core.Lyrics;

public enum LyricsStatus
{
    /// <summary>No suena nada.</summary>
    None,
    Loading,
    Synced,
    /// <summary>Solo hay letra sin tiempos.</summary>
    Plain,
    Instrumental,
    NotFound,
    /// <summary>Sin conexión o LRCLIB no responde (se reintentará con la siguiente canción).</summary>
    Unavailable,
    /// <summary>El usuario desactivó la búsqueda de letras en Ajustes.</summary>
    Disabled,
}

/// <summary>Resultado de buscar una letra.</summary>
public sealed record LyricsResult(LyricsStatus Status, IReadOnlyList<LyricLine> Lines, string? Plain)
{
    public static LyricsResult Of(LyricsStatus status) => new(status, [], null);

    public bool IsSynced => Status == LyricsStatus.Synced && Lines.Count > 0;
}

/// <summary>
/// Encuentra la letra de una canción: primero en la caché local y, si no está, en LRCLIB.
/// Prueba el título tal cual y luego «limpio», y como último recurso una búsqueda aproximada
/// eligiendo la versión cuya duración más se parece. Las respuestas (también «no encontrada»)
/// se guardan en %LOCALAPPDATA%\Tilde\lyrics para no repetir peticiones.
/// </summary>
public sealed class LyricsService(LrclibClient client, string cacheDirectory)
{
    /// <summary>Una canción sin letra se vuelve a buscar pasados unos días (quizá alguien la subió).</summary>
    private static readonly TimeSpan NotFoundExpiry = TimeSpan.FromDays(3);

    /// <summary>Máxima diferencia de duración aceptada en la búsqueda aproximada.</summary>
    private const double DurationToleranceSeconds = 3;

    public async Task<LyricsResult> FindAsync(string title, string artist, TimeSpan duration, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return LyricsResult.Of(LyricsStatus.NotFound);
        }

        var cacheFile = Path.Combine(cacheDirectory, CacheKey(title, artist, duration) + ".json");
        if (ReadCache(cacheFile) is { } cached)
        {
            return cached;
        }

        TimeSpan? knownDuration = duration.TotalSeconds > 1 ? duration : null;
        var (cleanTitle, cleanArtist) = TrackNames.Clean(title, artist);

        LrclibTrack? track = await client.GetAsync(title, artist, knownDuration, ct);
        if (track is null && (cleanTitle != title || cleanArtist != artist) && cleanTitle.Length > 0)
        {
            track = await client.GetAsync(cleanTitle, cleanArtist, knownDuration, ct);
        }
        if (track is null || (track.SyncedLyrics is null && !track.Instrumental))
        {
            var candidates = await client.SearchAsync(cleanTitle.Length > 0 ? cleanTitle : title, cleanArtist, ct);
            track = PickBest(candidates, knownDuration) ?? track;
        }

        var result = ToResult(track);
        WriteCache(cacheFile, track, result.Status);
        return result;
    }

    /// <summary>De varios candidatos, el que tiene letra sincronizada y la duración más parecida.</summary>
    internal static LrclibTrack? PickBest(IReadOnlyList<LrclibTrack> candidates, TimeSpan? duration)
    {
        var usable = candidates.Where(c => c.SyncedLyrics is not null || c.PlainLyrics is not null || c.Instrumental);
        if (duration is { } d)
        {
            usable = usable.Where(c => Math.Abs(c.Duration - d.TotalSeconds) <= DurationToleranceSeconds);
        }
        return usable
            .OrderByDescending(c => c.SyncedLyrics is not null)
            .ThenBy(c => duration is { } known ? Math.Abs(c.Duration - known.TotalSeconds) : 0)
            .FirstOrDefault();
    }

    private static LyricsResult ToResult(LrclibTrack? track)
    {
        if (track is null)
        {
            return LyricsResult.Of(LyricsStatus.NotFound);
        }
        if (track.Instrumental)
        {
            return LyricsResult.Of(LyricsStatus.Instrumental);
        }
        var lines = LrcParser.Parse(track.SyncedLyrics);
        if (lines.Count > 0)
        {
            return new LyricsResult(LyricsStatus.Synced, lines, track.PlainLyrics);
        }
        return string.IsNullOrWhiteSpace(track.PlainLyrics)
            ? LyricsResult.Of(LyricsStatus.NotFound)
            : new LyricsResult(LyricsStatus.Plain, [], track.PlainLyrics);
    }

    internal static string CacheKey(string title, string artist, TimeSpan duration)
    {
        var normalized = $"{title.Trim().ToLowerInvariant()}\u001f{artist.Trim().ToLowerInvariant()}\u001f{(int)Math.Round(duration.TotalSeconds / 2)}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..24].ToLowerInvariant();
    }

    private static LyricsResult? ReadCache(string file)
    {
        try
        {
            if (!File.Exists(file))
            {
                return null;
            }
            var entry = JsonSerializer.Deserialize(File.ReadAllText(file), LyricsCacheJsonContext.Default.LyricsCacheEntry);
            if (entry is null)
            {
                return null;
            }
            if (entry.Status == LyricsStatus.NotFound && DateTimeOffset.UtcNow - entry.SavedAt > NotFoundExpiry)
            {
                return null;
            }
            return ToResult(entry.Status == LyricsStatus.NotFound ? null : new LrclibTrack
            {
                Instrumental = entry.Status == LyricsStatus.Instrumental,
                SyncedLyrics = entry.Synced,
                PlainLyrics = entry.Plain,
            });
        }
        catch (Exception ex)
        {
            Log.Warn($"Caché de letras ilegible, se ignora ({ex.Message})");
            return null;
        }
    }

    private static void WriteCache(string file, LrclibTrack? track, LyricsStatus status)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var entry = new LyricsCacheEntry
            {
                Status = status,
                Synced = track?.SyncedLyrics,
                Plain = track?.PlainLyrics,
                SavedAt = DateTimeOffset.UtcNow,
            };
            File.WriteAllText(file, JsonSerializer.Serialize(entry, LyricsCacheJsonContext.Default.LyricsCacheEntry));
        }
        catch (Exception ex)
        {
            Log.Warn($"No se pudo guardar la letra en caché ({ex.Message})");
        }
    }
}

/// <summary>Lo que se guarda en disco por cada canción (sin título ni artista: el nombre es un hash).</summary>
internal sealed class LyricsCacheEntry
{
    public LyricsStatus Status { get; set; }

    public string? Synced { get; set; }

    public string? Plain { get; set; }

    public DateTimeOffset SavedAt { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(LyricsCacheEntry))]
internal sealed partial class LyricsCacheJsonContext : JsonSerializerContext;
