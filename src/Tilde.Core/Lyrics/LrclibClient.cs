using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tilde.Core.Lyrics;

/// <summary>Una pista tal como la devuelve LRCLIB.</summary>
public sealed class LrclibTrack
{
    public long Id { get; set; }

    public string? TrackName { get; set; }

    public string? ArtistName { get; set; }

    public double Duration { get; set; }

    public bool Instrumental { get; set; }

    public string? PlainLyrics { get; set; }

    public string? SyncedLyrics { get; set; }
}

/// <summary>Error temporal (sin conexión, servidor ocupado…): no se guarda en caché.</summary>
public sealed class LyricsUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Cliente de LRCLIB (https://lrclib.net), una base de datos abierta de letras.
/// Privacidad: solo se envían título, artista y duración. Nunca el álbum, el reproductor ni
/// ningún dato del equipo, y sin cookies.
/// </summary>
public sealed class LrclibClient : IDisposable
{
    private readonly HttpClient _http;

    public LrclibClient(string userAgent, HttpMessageHandler? handler = null)
    {
        handler ??= new SocketsHttpHandler
        {
            // Cerramos conexiones ociosas pronto: no queremos sockets abiertos sin motivo.
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(20),
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Brotli,
            UseCookies = false,
        };
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://lrclib.net/"),
            Timeout = TimeSpan.FromSeconds(12),
        };
        // LRCLIB pide identificar la aplicación.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
    }

    /// <summary>Busca la pista exacta. Devuelve null si no existe (404).</summary>
    public async Task<LrclibTrack?> GetAsync(string title, string artist, TimeSpan? duration, CancellationToken ct)
    {
        var query = $"api/get?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
        if (duration is { TotalSeconds: > 0 } d)
        {
            query += $"&duration={(int)Math.Round(d.TotalSeconds)}";
        }
        var json = await SendAsync(query, ct);
        return json is null ? null : JsonSerializer.Deserialize(json, LrclibJsonContext.Default.LrclibTrack);
    }

    /// <summary>Búsqueda aproximada por título y artista.</summary>
    public async Task<IReadOnlyList<LrclibTrack>> SearchAsync(string title, string artist, CancellationToken ct)
    {
        var query = $"api/search?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
        var json = await SendAsync(query, ct);
        return json is null ? [] : JsonSerializer.Deserialize(json, LrclibJsonContext.Default.ListLrclibTrack) ?? [];
    }

    public void Dispose() => _http.Dispose();

    private async Task<string?> SendAsync(string query, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(query, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            if (!response.IsSuccessStatusCode)
            {
                throw new LyricsUnavailableException($"LRCLIB respondió {(int)response.StatusCode}");
            }
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new LyricsUnavailableException("No se pudo contactar con LRCLIB", ex);
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LrclibTrack))]
[JsonSerializable(typeof(List<LrclibTrack>))]
internal sealed partial class LrclibJsonContext : JsonSerializerContext;
