using Tilde.Core;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Tilde.Platform.Media;

/// <summary>Qué suena: datos de la pista actual.</summary>
public sealed record MediaInfo(string AppId, string Title, string Artist, string Album, TimeSpan Duration)
{
    /// <summary>Identifica la pista (para saber cuándo cambia de canción).</summary>
    public string Key => $"{AppId}\u001f{Title}\u001f{Artist}";
}

/// <summary>Estado de reproducción y posición.</summary>
public sealed record PlaybackInfo(
    bool IsPlaying,
    double Rate,
    TimeSpan Position,
    DateTimeOffset UpdatedAt,
    TimeSpan Duration,
    bool CanPlayPause,
    bool CanNext,
    bool CanPrevious,
    bool CanSeek)
{
    /// <summary>
    /// Posición estimada en un instante. Windows solo informa de la posición cuando cambia algo
    /// (play, pausa, salto); entre medias la calculamos con el reloj, sin preguntar a nadie.
    /// </summary>
    public TimeSpan PositionAt(DateTimeOffset now)
    {
        var position = IsPlaying ? Position + (now - UpdatedAt) * Rate : Position;
        if (position < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }
        return Duration > TimeSpan.Zero && position > Duration ? Duration : position;
    }
}

/// <summary>
/// Lee cualquier reproductor que aparezca en los controles multimedia de Windows (Spotify,
/// navegadores, Reproductor multimedia…) mediante GlobalSystemMediaTransportControls.
/// Todo funciona por eventos: Windows avisa cuando cambia la sesión, la pista, el estado o
/// la posición. Los avisos llegan en hilos del sistema y se pasan al hilo de interfaz.
/// </summary>
public sealed class MediaSessionWatcher : IDisposable
{
    private const long MaxThumbnailBytes = 4 * 1024 * 1024;
    private readonly SynchronizationContext _ui;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private int _mediaVersion;

    public MediaSessionWatcher()
    {
        _ui = SynchronizationContext.Current ?? throw new InvalidOperationException("Crear en el hilo de interfaz");
    }

    public MediaInfo? Media { get; private set; }

    public PlaybackInfo? Playback { get; private set; }

    /// <summary>Bytes de la carátula (JPEG/PNG) o null.</summary>
    public byte[]? Thumbnail { get; private set; }

    /// <summary>Cambió la pista (o ya no hay ninguna).</summary>
    public event Action? MediaChanged;

    /// <summary>Cambió la carátula.</summary>
    public event Action? ThumbnailChanged;

    /// <summary>Cambió play/pausa, la posición o los controles disponibles.</summary>
    public event Action? PlaybackChanged;

    public async Task StartAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += (_, _) => _ui.Post(_ => BindCurrentSession(), null);
            BindCurrentSession();
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo acceder a los controles multimedia de Windows", ex);
        }
    }

    // ── Órdenes al reproductor ──

    public Task TogglePlayPauseAsync() => Run(s => s.TryTogglePlayPauseAsync().AsTask());

    public Task NextAsync() => Run(s => s.TrySkipNextAsync().AsTask());

    public Task PreviousAsync() => Run(s => s.TrySkipPreviousAsync().AsTask());

    /// <summary>Salta a una posición (relativa al inicio de la pista).</summary>
    public Task SeekAsync(TimeSpan position)
    {
        return Run(s =>
        {
            var timeline = s.GetTimelineProperties();
            return s.TryChangePlaybackPositionAsync((timeline.StartTime + position).Ticks).AsTask();
        });
    }

    public void Dispose() => Unbind();

    private async Task Run(Func<GlobalSystemMediaTransportControlsSession, Task<bool>> command)
    {
        if (_session is not { } session)
        {
            return;
        }
        try
        {
            await command(session);
        }
        catch (Exception ex)
        {
            Log.Warn($"El reproductor rechazó la orden ({ex.Message})");
        }
    }

    private void BindCurrentSession()
    {
        var session = _manager?.GetCurrentSession();
        if (ReferenceEquals(session, _session) && session is not null)
        {
            return;
        }
        Unbind();
        _session = session;
        if (session is null)
        {
            Media = null;
            Playback = null;
            Thumbnail = null;
            MediaChanged?.Invoke();
            ThumbnailChanged?.Invoke();
            PlaybackChanged?.Invoke();
            return;
        }
        session.MediaPropertiesChanged += OnMediaPropertiesChanged;
        session.PlaybackInfoChanged += OnPlaybackChanged;
        session.TimelinePropertiesChanged += OnTimelineChanged;
        RefreshPlayback();
        _ = RefreshMediaAsync();
    }

    private void Unbind()
    {
        if (_session is { } old)
        {
            old.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            old.PlaybackInfoChanged -= OnPlaybackChanged;
            old.TimelinePropertiesChanged -= OnTimelineChanged;
        }
        _session = null;
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) =>
        _ui.Post(_ => { if (ReferenceEquals(sender, _session)) _ = RefreshMediaAsync(); }, null);

    private void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) =>
        _ui.Post(_ => { if (ReferenceEquals(sender, _session)) RefreshPlayback(); }, null);

    private void OnTimelineChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) =>
        _ui.Post(_ => { if (ReferenceEquals(sender, _session)) RefreshPlayback(); }, null);

    private async Task RefreshMediaAsync()
    {
        var session = _session;
        if (session is null)
        {
            return;
        }
        int version = ++_mediaVersion;
        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            if (version != _mediaVersion || !ReferenceEquals(session, _session))
            {
                return; // Llegó otra actualización mientras esperábamos: esta ya no vale.
            }
            var timeline = session.GetTimelineProperties();
            var media = new MediaInfo(
                session.SourceAppUserModelId ?? string.Empty,
                props?.Title ?? string.Empty,
                props?.Artist is { Length: > 0 } artist ? artist : props?.AlbumArtist ?? string.Empty,
                props?.AlbumTitle ?? string.Empty,
                timeline.EndTime - timeline.StartTime);

            bool trackChanged = Media?.Key != media.Key;
            Media = media;
            MediaChanged?.Invoke();

            var thumbnail = props?.Thumbnail is { } reference ? await ReadThumbnailAsync(reference) : null;
            if (version != _mediaVersion)
            {
                return;
            }
            if (trackChanged || thumbnail is not null || Thumbnail is null)
            {
                Thumbnail = thumbnail;
                ThumbnailChanged?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"No se pudieron leer los datos de la pista ({ex.Message})");
        }
    }

    private void RefreshPlayback()
    {
        if (_session is not { } session)
        {
            return;
        }
        try
        {
            var info = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();
            var controls = info.Controls;
            Playback = new PlaybackInfo(
                info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                info.PlaybackRate is > 0 and var rate ? rate : 1.0,
                timeline.Position - timeline.StartTime,
                timeline.LastUpdatedTime,
                timeline.EndTime - timeline.StartTime,
                controls.IsPlayPauseToggleEnabled || controls.IsPlayEnabled || controls.IsPauseEnabled,
                controls.IsNextEnabled,
                controls.IsPreviousEnabled,
                controls.IsPlaybackPositionEnabled);
            PlaybackChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Warn($"No se pudo leer el estado de reproducción ({ex.Message})");
        }
    }

    private static async Task<byte[]?> ReadThumbnailAsync(IRandomAccessStreamReference reference)
    {
        try
        {
            using var stream = await reference.OpenReadAsync();
            if (stream.Size == 0 || stream.Size > MaxThumbnailBytes)
            {
                return null;
            }
            using var reader = new DataReader(stream);
            uint size = (uint)stream.Size;
            await reader.LoadAsync(size);
            var bytes = new byte[size];
            reader.ReadBytes(bytes);
            return bytes;
        }
        catch (Exception ex)
        {
            Log.Warn($"No se pudo leer la carátula ({ex.Message})");
            return null;
        }
    }
}
