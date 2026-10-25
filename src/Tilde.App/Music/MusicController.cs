using Tilde.Core;
using Tilde.Core.Lyrics;
using Tilde.Core.Settings;
using Tilde.Platform;
using Tilde.Platform.Audio;
using Tilde.Platform.Media;
using Tilde.Render;
using Vortice.Direct2D1;

namespace Tilde.App.Music;

/// <summary>
/// Estado de la música para toda la app: pista, reproducción, carátula, volumen y letra.
/// La página de música y la píldora lo observan mediante eventos (todos en el hilo de interfaz).
///
/// La línea actual de la letra se calcula sin sondeos: al cambiar de línea se programa un único
/// temporizador para el instante exacto de la siguiente, y se recalcula si el reproductor avisa
/// de pausa, salto o cambio de velocidad.
/// </summary>
internal sealed class MusicController : IDisposable
{
    /// <summary>Espera tras un cambio de pista antes de pedir la letra (los reproductores envían el título y luego el resto, y así evitamos peticiones si saltas canciones rápido).</summary>
    private static readonly TimeSpan LyricsDebounce = TimeSpan.FromMilliseconds(450);

    private readonly SettingsStore _settings;
    private readonly GraphicsDevice _graphics;
    private readonly LrclibClient _lrclib;
    private readonly LyricsService _lyrics;
    private readonly UiTimer _lineTimer;
    private readonly UiTimer _lyricsTimer;
    private CancellationTokenSource? _lyricsCts;
    private string? _lyricsKey;
    private ID2D1Bitmap1? _art;
    private bool _artDirty = true;

    public MusicController(SettingsStore settings, GraphicsDevice graphics, string version)
    {
        _settings = settings;
        _graphics = graphics;
        _lrclib = new LrclibClient($"Tilde/{version} (Windows notch app)");
        _lyrics = new LyricsService(_lrclib, Path.Combine(AppPaths.DataDirectory, "lyrics"));
        _lineTimer = new UiTimer(UpdateLine);
        _lyricsTimer = new UiTimer(() => _ = FetchLyricsAsync());

        Media = new MediaSessionWatcher();
        Media.MediaChanged += OnMediaChanged;
        Media.ThumbnailChanged += OnThumbnailChanged;
        Media.PlaybackChanged += OnPlaybackChanged;
        Volume = new SystemVolume();
        Volume.Changed += () => VolumeChanged?.Invoke();
    }

    public MediaSessionWatcher Media { get; }

    public SystemVolume Volume { get; }

    public MediaInfo? Track => Media.Media is { Title.Length: > 0 } media ? media : null;

    public PlaybackInfo? Playback => Track is null ? null : Media.Playback;

    public LyricsResult Lyrics { get; private set; } = LyricsResult.Of(LyricsStatus.None);

    /// <summary>Índice de la línea que suena (−1 si ninguna).</summary>
    public int CurrentLine { get; private set; } = -1;

    /// <summary>Texto de la línea actual si hay letra sincronizada y se está reproduciendo.</summary>
    public string? CurrentLineText =>
        Lyrics.IsSynced && CurrentLine >= 0 && Playback is { IsPlaying: true } ? Lyrics.Lines[CurrentLine].Text : null;

    public event Action? TrackChanged;

    public event Action? PlaybackChanged;

    public event Action? ArtChanged;

    public event Action? LyricsChanged;

    public event Action? LineChanged;

    public event Action? VolumeChanged;

    public Task StartAsync() => Media.StartAsync();

    /// <summary>Carátula decodificada (192 px), compartida por la página y la píldora.</summary>
    public ID2D1Bitmap1? GetArt()
    {
        if (_artDirty)
        {
            _artDirty = false;
            _art?.Dispose();
            _art = Media.Thumbnail is { } bytes ? Images.Decode(_graphics, bytes, 192) : null;
        }
        return _art;
    }

    /// <summary>Tras recrear el dispositivo gráfico hay que volver a decodificar la carátula.</summary>
    public void ResetDeviceResources()
    {
        _art?.Dispose();
        _art = null;
        _artDirty = true;
    }

    /// <summary>Se llama cuando cambian los ajustes (p. ej. activar o desactivar la búsqueda de letras).</summary>
    public void ApplySettings()
    {
        bool online = _settings.Current.LyricsOnline;
        if (!online && Lyrics.Status != LyricsStatus.Disabled && Track is not null)
        {
            _lyricsCts?.Cancel();
            SetLyrics(LyricsResult.Of(LyricsStatus.Disabled));
        }
        else if (online && Lyrics.Status == LyricsStatus.Disabled)
        {
            SetLyrics(LyricsResult.Of(LyricsStatus.Loading));
            _lyricsTimer.Start(TimeSpan.Zero);
        }
    }

    // ── Órdenes ──

    public void TogglePlayPause() => _ = Media.TogglePlayPauseAsync();

    public void Next() => _ = Media.NextAsync();

    public void Previous() => _ = Media.PreviousAsync();

    public void Seek(TimeSpan position) => _ = Media.SeekAsync(position);

    public void Dispose()
    {
        _lyricsCts?.Cancel();
        _lineTimer.Dispose();
        _lyricsTimer.Dispose();
        Media.Dispose();
        Volume.Dispose();
        _lrclib.Dispose();
        _art?.Dispose();
    }

    // ── Eventos del reproductor ──

    private void OnMediaChanged()
    {
        var key = Track?.Key;
        if (key != _lyricsKey)
        {
            // Canción nueva: olvidamos la letra anterior al instante (nunca mostramos una ajena).
            _lyricsKey = key;
            _lyricsCts?.Cancel();
            _lyricsTimer.Stop();
            if (key is null)
            {
                SetLyrics(LyricsResult.Of(LyricsStatus.None));
            }
            else if (!_settings.Current.LyricsOnline)
            {
                SetLyrics(LyricsResult.Of(LyricsStatus.Disabled));
            }
            else
            {
                SetLyrics(LyricsResult.Of(LyricsStatus.Loading));
                _lyricsTimer.Start(LyricsDebounce);
            }
        }
        TrackChanged?.Invoke();
    }

    private void OnThumbnailChanged()
    {
        _artDirty = true;
        ArtChanged?.Invoke();
    }

    private void OnPlaybackChanged()
    {
        UpdateLine();
        PlaybackChanged?.Invoke();
    }

    private async Task FetchLyricsAsync()
    {
        if (Track is not { } track || _lyricsKey != track.Key)
        {
            return;
        }
        _lyricsCts?.Cancel();
        var cts = _lyricsCts = new CancellationTokenSource();
        var key = track.Key;
        var duration = Media.Playback?.Duration is { TotalSeconds: > 1 } d ? d : track.Duration;
        LyricsResult result;
        try
        {
            result = await _lyrics.FindAsync(track.Title, track.Artist, duration, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (LyricsUnavailableException ex)
        {
            Log.Info($"Letras no disponibles ahora mismo ({ex.Message})");
            result = LyricsResult.Of(LyricsStatus.Unavailable);
        }
        catch (Exception ex)
        {
            Log.Error("Error buscando la letra", ex);
            result = LyricsResult.Of(LyricsStatus.Unavailable);
        }

        // Si mientras tanto cambió la canción, este resultado ya no sirve.
        if (key == _lyricsKey && !cts.IsCancellationRequested)
        {
            SetLyrics(result);
        }
    }

    private void SetLyrics(LyricsResult lyrics)
    {
        Lyrics = lyrics;
        CurrentLine = -1;
        LyricsChanged?.Invoke();
        UpdateLine(force: true);
    }

    private void UpdateLine() => UpdateLine(force: false);

    private void UpdateLine(bool force)
    {
        _lineTimer.Stop();
        int index = -1;
        if (Lyrics.IsSynced && Playback is { } playback)
        {
            var now = DateTimeOffset.Now;
            var position = playback.PositionAt(now);
            var lines = Lyrics.Lines;
            index = LrcParser.IndexAt(lines, position);
            if (playback.IsPlaying && index + 1 < lines.Count)
            {
                // Un único despertar justo cuando empieza la siguiente línea.
                var wait = (lines[index + 1].Time - position) / Math.Max(0.1, playback.Rate);
                _lineTimer.Start(wait + TimeSpan.FromMilliseconds(15));
            }
        }
        if (index != CurrentLine || force)
        {
            CurrentLine = index;
            LineChanged?.Invoke();
        }
    }
}
