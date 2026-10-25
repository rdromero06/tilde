using Tilde.App.Music;
using Tilde.App.Notch;
using Tilde.Core;
using Tilde.Core.Animation;
using Tilde.Core.Lyrics;
using Tilde.Core.Settings;
using Tilde.Platform;
using Tilde.Platform.Interop;
using Tilde.Render;
using Vortice.Direct2D1;
using Vortice.DirectComposition;
using Vortice.DirectWrite;

namespace Tilde.App.Pages;

/// <summary>
/// Página de música con dos vistas:
/// <list type="bullet">
/// <item><b>Reproductor</b>: carátula, título, artista, controles, barra de progreso (con
/// búsqueda arrastrando) y volumen del sistema.</item>
/// <item><b>Letras</b>: el notch se divide en dos; a la izquierda un mini reproductor y a la
/// derecha la letra sincronizada, con la línea actual resaltada y centrada. Clic en una línea
/// para saltar a ese momento.</item>
/// </list>
/// El avance de la barra lo anima el compositor (una animación lineal hasta el final de la
/// canción), así que no hay que redibujar para que se mueva. Solo los números del tiempo se
/// redibujan, una vez por segundo y únicamente con el notch abierto en esta página.
/// </summary>
internal sealed class MusicPage : Page
{
    private enum Hit
    {
        None,
        Previous,
        Play,
        Next,
        LyricsToggle,
        Bar,
        Mute,
        Volume,
        LyricLine,
    }

    private readonly MusicController _music;
    private readonly SettingsStore _settings;
    private readonly UiTimer _secondTimer;
    private readonly List<IDisposable> _layers = [];
    private readonly List<IDisposable> _lyricsLayers = [];

    private Layout _l = null!;
    private DrawingSurface? _main;
    private DrawingSurface? _times;
    private DrawingSurface? _volume;
    private IDCompositionRectangleClip? _fillClip;
    private AnimatedValue? _fillRight;
    private float _fillWidthPx;

    private IDCompositionVisual3? _lyricsScroll;
    private DrawingSurface? _lyricsHighlight;
    private AnimatedValue? _scrollY;
    private (float Y, float H)[] _lineRects = [];

    private Hit _hover;
    private Hit _pressed;
    private bool _draggingBar;
    private bool _draggingVolume;
    private float _dragFraction;

    public MusicPage(MusicController music, SettingsStore settings)
    {
        _music = music;
        _settings = settings;
        _secondTimer = new UiTimer(OnSecondTick);
        music.TrackChanged += () => { RedrawMain(); RedrawTimes(); UpdateProgress(); };
        music.PlaybackChanged += () => { RedrawMain(); RedrawTimes(); UpdateProgress(); };
        music.ArtChanged += RedrawMain;
        music.VolumeChanged += RedrawVolume;
        music.LyricsChanged += BuildLyrics;
        music.LineChanged += UpdateHighlight;
    }

    public override string Title => Strings.PageMusic;

    public override IconKind Icon => IconKind.Music;

    private bool LyricsView => _settings.Current.LyricsView;

    // ── Ciclo de vida ──

    protected override void OnAttach(PageContext context) => BuildLayers();

    protected override void OnDetach()
    {
        _secondTimer.Stop();
        DisposeLayers();
    }

    protected override void OnVisibilityChanged(bool visible)
    {
        if (!visible)
        {
            _secondTimer.Stop();
            _hover = Hit.None;
        }
        RedrawTimes();
        UpdateProgress();
        Context?.Commit();
    }

    // ── Ratón y teclado ──

    public override void OnPointerMove(float x, float y)
    {
        if (_draggingBar)
        {
            _dragFraction = Fraction(x, _l.Bar);
            UpdateProgress();
            RedrawTimes();
            return;
        }
        if (_draggingVolume)
        {
            _music.Volume.SetLevel(Fraction(x, _l.Volume));
            return;
        }
        SetHover(HitTest(x, y));
    }

    public override void OnPointerLeave()
    {
        if (!_draggingBar && !_draggingVolume)
        {
            SetHover(Hit.None);
        }
    }

    public override bool OnPointerDown(float x, float y)
    {
        _pressed = HitTest(x, y);
        if (_pressed == Hit.Bar && _music.Playback is { CanSeek: true, Duration.TotalSeconds: > 0 })
        {
            _draggingBar = true;
            _dragFraction = Fraction(x, _l.Bar);
            UpdateProgress();
            RedrawTimes();
            return true;
        }
        if (_pressed == Hit.Volume && _music.Volume.IsAvailable)
        {
            _draggingVolume = true;
            _music.Volume.SetLevel(Fraction(x, _l.Volume));
            return true;
        }
        return false;
    }

    public override void OnPointerUp(float x, float y)
    {
        if (_draggingBar)
        {
            _draggingBar = false;
            if (_music.Playback is { } playback)
            {
                _music.Seek(playback.Duration * _dragFraction);
            }
            UpdateProgress();
            RedrawTimes();
            return;
        }
        if (_draggingVolume)
        {
            _draggingVolume = false;
            RedrawVolume();
            return;
        }

        var hit = HitTest(x, y);
        if (hit == _pressed)
        {
            Activate(hit, y);
        }
        _pressed = Hit.None;
    }

    public override bool IsClickable(float x, float y) => HitTest(x, y) != Hit.None;

    public override bool OnKey(int key)
    {
        if (key == Native.VK_SPACE && _music.Track is not null)
        {
            _music.TogglePlayPause();
            return true;
        }
        return false;
    }

    private void Activate(Hit hit, float y)
    {
        switch (hit)
        {
            case Hit.Previous: _music.Previous(); break;
            case Hit.Play: _music.TogglePlayPause(); break;
            case Hit.Next: _music.Next(); break;
            case Hit.Mute: _music.Volume.SetMuted(!_music.Volume.IsMuted); break;
            case Hit.LyricsToggle:
                _settings.Update(s => s.LyricsView = !s.LyricsView);
                BuildLayers();
                break;
            case Hit.LyricLine:
                if (LineAt(y) is int line && _music.Lyrics.IsSynced)
                {
                    _music.Seek(_music.Lyrics.Lines[line].Time);
                }
                break;
        }
    }

    private Hit HitTest(float x, float y)
    {
        if (_music.Track is null)
        {
            return Hit.None;
        }
        var p = _music.Playback;
        if (_l.Play.Contains(x, y) && p?.CanPlayPause != false) return Hit.Play;
        if (_l.Previous.Contains(x, y) && p?.CanPrevious == true) return Hit.Previous;
        if (_l.Next.Contains(x, y) && p?.CanNext == true) return Hit.Next;
        if (_l.LyricsToggle.Contains(x, y)) return Hit.LyricsToggle;
        if (_l.Bar.Contains(x, y) && p?.CanSeek == true) return Hit.Bar;
        if (_l.Mute.Contains(x, y) && _music.Volume.IsAvailable) return Hit.Mute;
        if (_l.Volume.Contains(x, y) && _music.Volume.IsAvailable) return Hit.Volume;
        if (_l.LyricsPanel.Contains(x, y) && LineAt(y) is not null) return Hit.LyricLine;
        return Hit.None;
    }

    private void SetHover(Hit hit)
    {
        if (hit == _hover)
        {
            return;
        }
        var previous = _hover;
        _hover = hit;
        if (previous is Hit.Volume or Hit.Mute || hit is Hit.Volume or Hit.Mute)
        {
            RedrawVolume();
        }
        RedrawMain();
    }

    private static float Fraction(float x, RectF r) => r.W <= 0 ? 0 : Math.Clamp((x - r.X) / r.W, 0f, 1f);

    // ── Capas ──

    private void BuildLayers()
    {
        if (Context is not { } c)
        {
            return;
        }
        DisposeLayers();
        _l = Layout.Create(c.Width, c.Height, c.F, LyricsView);

        _main = AddSurface(c, new RectF(0, 0, c.Width, c.Height), c.Root, _layers);

        // Relleno de la barra: un rectángulo cuyo recorte derecho anima el compositor.
        var track = _l.Track;
        var fill = AddSurface(c, track, c.Root, _layers);
        fill.Draw(dc => dc.FillRoundedRectangle(
            new RoundedRectangle(new Vortice.RawRectF(0, 0, track.W, track.H), track.H / 2, track.H / 2), c.Theme.Primary));
        _fillWidthPx = c.Px(track.W);
        _fillClip = c.Device.CreateRectangleClip();
        _layers.Add(_fillClip);
        _fillClip.SetLeft(-1);
        _fillClip.SetTop(-1);
        _fillClip.SetBottom(c.Px(track.H) + 1);
        fill.Visual.SetClip(_fillClip);
        _fillRight = new AnimatedValue(v => _fillClip.SetRight(v), a => _fillClip.SetRight(a), 0);

        _times = _l.Times.W > 0 ? AddSurface(c, _l.Times, c.Root, _layers) : null;
        _volume = _l.VolumeArea.W > 0 ? AddSurface(c, _l.VolumeArea, c.Root, _layers) : null;

        RedrawMain();
        RedrawTimes();
        RedrawVolume();
        BuildLyrics();
        UpdateProgress();
        c.Commit();
    }

    private void DisposeLayers()
    {
        DisposeLyricsLayers();
        for (int i = _layers.Count - 1; i >= 0; i--)
        {
            _layers[i].Dispose();
        }
        _layers.Clear();
        _main = _times = _volume = null;
        _fillClip = null;
        _fillRight = null;
    }

    private DrawingSurface AddSurface(PageContext c, RectF rect, IDCompositionVisual parent, List<IDisposable> owner)
    {
        var surface = new DrawingSurface(c.Graphics);
        surface.Resize(rect.W, rect.H, c.Scale);
        surface.SetOffset(rect.X, rect.Y);
        parent.AddVisual(surface.Visual, false, null);
        owner.Add(new Removal(parent, surface));
        return surface;
    }

    /// <summary>Al liberar una capa, primero se quita de su padre.</summary>
    private sealed class Removal(IDCompositionVisual parent, DrawingSurface surface) : IDisposable
    {
        public void Dispose()
        {
            parent.RemoveVisual(surface.Visual);
            surface.Dispose();
        }
    }

    // ── Dibujo del reproductor ──

    private void RedrawMain()
    {
        if (_main is not { } main || Context is not { } c)
        {
            return;
        }
        var theme = c.Theme;
        float f = c.F;
        main.Draw(dc =>
        {
            var track = _music.Track;
            if (track is null)
            {
                DrawEmpty(dc, theme, c.Width, c.Height);
                return;
            }
            var p = _music.Playback;

            if (_music.GetArt() is { } art)
            {
                Images.DrawRounded(dc, art, _l.Art, 10f * f);
            }
            else
            {
                dc.FillRoundedRectangle(Rounded(_l.Art, 10f * f), theme.Subtle);
                float icon = _l.Art.W * 0.42f;
                theme.Icons.Draw(dc, IconKind.Music, _l.Art.X + (_l.Art.W - icon) / 2, _l.Art.Y + (_l.Art.H - icon) / 2, icon, theme.Tertiary);
            }

            Text.Draw(dc, theme.DWrite, track.Title, theme.TrackTitle, _l.Title, theme.Primary);
            Text.Draw(dc, theme.DWrite, track.Artist.Length > 0 ? track.Artist : Strings.UnknownArtist, theme.TrackArtist, _l.Artist, theme.Secondary);

            DrawButton(dc, theme, _l.Previous, IconKind.Previous, 0.6f, p?.CanPrevious == true, Hit.Previous);
            DrawButton(dc, theme, _l.Play, p?.IsPlaying == true ? IconKind.Pause : IconKind.Play, 0.66f, p?.CanPlayPause != false, Hit.Play);
            DrawButton(dc, theme, _l.Next, IconKind.Next, 0.6f, p?.CanNext == true, Hit.Next);

            // Botón de letras: en ámbar cuando la vista de letras está activa.
            var lyricsBrush = LyricsView ? theme.Accent : _hover == Hit.LyricsToggle ? theme.Primary : theme.Secondary;
            DrawIconCentered(dc, theme, _l.LyricsToggle, IconKind.Lyrics, 0.8f, lyricsBrush);

            if (_l.Source.W > 0)
            {
                Text.Draw(dc, theme.DWrite, TrackNames.FriendlyAppName(track.AppId), theme.Small, _l.Source, theme.Tertiary);
            }

            // Pista de la barra de progreso (el relleno es otra capa).
            dc.FillRoundedRectangle(Rounded(_l.Track, _l.Track.H / 2), theme.Tertiary);
        });
        c.Commit();
    }

    private void DrawEmpty(ID2D1DeviceContext dc, Theme theme, float width, float height)
    {
        float f = theme.F;
        float icon = 26f * f;
        float y = (height - (icon + 8f * f + 21f * f + 17f * f)) / 2;
        theme.Icons.Draw(dc, IconKind.Music, (width - icon) / 2, y, icon, theme.Tertiary);
        y += icon + 8f * f;
        Text.Draw(dc, theme.DWrite, Strings.NothingPlaying, theme.Title, new RectF(0, y, width, 21f * f), theme.Primary, TextAlignment.Center);
        Text.Draw(dc, theme.DWrite, Strings.NothingPlayingHint, theme.Caption, new RectF(0, y + 21f * f, width, 17f * f), theme.Secondary, TextAlignment.Center);
    }

    private void DrawButton(ID2D1DeviceContext dc, Theme theme, RectF r, IconKind icon, float iconScale, bool enabled, Hit hit)
    {
        if (enabled && _hover == hit)
        {
            float d = MathF.Min(r.W, r.H);
            dc.FillEllipse(new Ellipse(new System.Numerics.Vector2(r.X + r.W / 2, r.Y + r.H / 2), d / 2, d / 2), theme.Subtle);
        }
        DrawIconCentered(dc, theme, r, icon, iconScale, enabled ? theme.Primary : theme.Tertiary);
    }

    private static void DrawIconCentered(ID2D1DeviceContext dc, Theme theme, RectF r, IconKind icon, float scale, ID2D1Brush brush)
    {
        float size = MathF.Min(r.W, r.H) * scale;
        theme.Icons.Draw(dc, icon, r.X + (r.W - size) / 2, r.Y + (r.H - size) / 2, size, brush);
    }

    private static RoundedRectangle Rounded(RectF r, float radius) =>
        new(new Vortice.RawRectF(r.X, r.Y, r.Right, r.Bottom), radius, radius);

    private void RedrawTimes()
    {
        if (_times is not { } times || Context is not { } c)
        {
            return;
        }
        var p = _music.Playback;
        var position = _draggingBar && p is not null ? p.Duration * _dragFraction : p?.PositionAt(DateTimeOffset.Now) ?? TimeSpan.Zero;
        times.Draw(dc =>
        {
            if (p is null || p.Duration <= TimeSpan.Zero)
            {
                return;
            }
            var rect = new RectF(0, 0, times.WidthDip, times.HeightDip);
            Text.Draw(dc, c.Theme.DWrite, FormatTime(position), c.Theme.Small, rect, c.Theme.Secondary);
            Text.Draw(dc, c.Theme.DWrite, FormatTime(p.Duration), c.Theme.Small, rect, c.Theme.Secondary, TextAlignment.Trailing);
        });
        c.Commit();

        // Próximo redibujado justo al cambiar el segundo, solo si se ve y suena.
        _secondTimer.Stop();
        if (IsVisible && p is { IsPlaying: true } && !_draggingBar)
        {
            double fraction = position.TotalSeconds % 1;
            _secondTimer.Start(TimeSpan.FromSeconds((1 - fraction) / Math.Max(0.1, p.Rate)) + TimeSpan.FromMilliseconds(10));
        }
    }

    private void OnSecondTick() => RedrawTimes();

    private static string FormatTime(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    /// <summary>Coloca el relleno de la barra y, si suena, lo anima hasta el final de la canción.</summary>
    private void UpdateProgress()
    {
        if (_fillRight is null || Context is not { } c)
        {
            return;
        }
        var p = _music.Playback;
        if (p is null || p.Duration <= TimeSpan.Zero)
        {
            _fillRight.Set(0);
        }
        else if (_draggingBar)
        {
            _fillRight.Set(_fillWidthPx * _dragFraction);
        }
        else
        {
            var position = p.PositionAt(DateTimeOffset.Now);
            float start = (float)(position / p.Duration) * _fillWidthPx;
            _fillRight.Set(start);
            if (p.IsPlaying && IsVisible)
            {
                double remaining = (p.Duration - position).TotalSeconds / Math.Max(0.1, p.Rate);
                if (remaining > 0.05)
                {
                    _fillRight.AnimateTo(c.Device, _fillWidthPx, remaining, Ease.Linear);
                }
            }
        }
        c.Commit();
    }

    private void RedrawVolume()
    {
        if (_volume is not { } volume || Context is not { } c)
        {
            return;
        }
        var theme = c.Theme;
        float f = c.F;
        var v = _music.Volume;
        volume.Draw(dc =>
        {
            if (_music.Track is null || !v.IsAvailable)
            {
                return;
            }
            var mute = Offset(_l.Mute, _l.VolumeArea);
            bool active = _hover is Hit.Volume or Hit.Mute || _draggingVolume;
            DrawIconCentered(dc, theme, mute, v.IsMuted ? IconKind.VolumeMuted : IconKind.Volume, 0.8f,
                _hover == Hit.Mute ? theme.Primary : theme.Secondary);

            var slider = Offset(_l.Volume, _l.VolumeArea);
            float cy = slider.Y + slider.H / 2;
            float h = 4f * f;
            dc.FillRoundedRectangle(Rounded(new RectF(slider.X, cy - h / 2, slider.W, h), h / 2), theme.Tertiary);
            float level = v.IsMuted ? 0 : v.Level;
            if (level > 0)
            {
                dc.FillRoundedRectangle(Rounded(new RectF(slider.X, cy - h / 2, MathF.Max(h, slider.W * level), h), h / 2),
                    active ? theme.Accent : theme.Primary);
            }
            if (active)
            {
                dc.FillEllipse(new Ellipse(new System.Numerics.Vector2(slider.X + slider.W * level, cy), 5f * f, 5f * f), theme.Primary);
            }
        });
        c.Commit();
    }

    private static RectF Offset(RectF r, RectF origin) => r with { X = r.X - origin.X, Y = r.Y - origin.Y };

    // ── Letras ──

    private void BuildLyrics()
    {
        if (Context is not { } c || _l is null)
        {
            return;
        }
        DisposeLyricsLayers();
        var panel = _l.LyricsPanel;
        if (panel.W <= 0 || _music.Track is null)
        {
            c.Commit();
            return;
        }

        var theme = c.Theme;
        var container = c.Graphics.CreateVisual();
        container.SetOffsetX(c.Px(panel.X));
        container.SetOffsetY(c.Px(panel.Y));
        container.SetClip(new Vortice.RawRectF(0, 0, c.Px(panel.W), c.Px(panel.H)));
        c.Root.AddVisual(container, false, null);
        _lyricsLayers.Add(new VisualRemoval(c.Root, container));

        var lyrics = _music.Lyrics;
        if (!lyrics.IsSynced)
        {
            string message = lyrics.Status switch
            {
                LyricsStatus.Loading => Strings.LyricsLoading,
                LyricsStatus.Plain => Strings.LyricsPlainOnly,
                LyricsStatus.Instrumental => Strings.LyricsInstrumental,
                LyricsStatus.Unavailable => Strings.LyricsUnavailable,
                LyricsStatus.Disabled => Strings.LyricsDisabled,
                _ => Strings.LyricsNotFound,
            };
            var note = AddSurface(c, new RectF(0, 0, panel.W, panel.H), container, _lyricsLayers);
            note.Draw(dc => Text.Draw(dc, theme.DWrite, message, theme.Caption, new RectF(0, 0, panel.W, panel.H), theme.Secondary, TextAlignment.Center));
            _lineRects = [];
            c.Commit();
            return;
        }

        // Medimos todas las líneas (con ajuste de línea) para saber dónde cae cada una.
        float gap = 6f * c.F;
        var layouts = new IDWriteTextLayout[lyrics.Lines.Count];
        var rects = new (float Y, float H)[lyrics.Lines.Count];
        float y = 0;
        float maxHeight = 15_000f / c.Scale; // Límite de tamaño de una superficie.
        for (int i = 0; i < layouts.Length; i++)
        {
            var text = lyrics.Lines[i].Text.Length > 0 ? lyrics.Lines[i].Text : "♪";
            layouts[i] = theme.DWrite.CreateTextLayout(text, theme.Lyric, panel.W, 1000f);
            float h = layouts[i].Metrics.Height;
            rects[i] = (y, h);
            y = MathF.Min(y + h + gap, maxHeight);
        }
        _lineRects = rects;

        _lyricsScroll = c.Graphics.CreateVisual();
        container.AddVisual(_lyricsScroll, false, null);
        _lyricsLayers.Add(new VisualRemoval(container, _lyricsScroll));
        var list = AddSurface(c, new RectF(0, 0, panel.W, MathF.Max(1, y)), _lyricsScroll, _lyricsLayers);
        list.Draw(dc =>
        {
            for (int i = 0; i < layouts.Length; i++)
            {
                if (rects[i].Y + rects[i].H <= list.HeightDip)
                {
                    dc.DrawTextLayout(new System.Numerics.Vector2(0, rects[i].Y), layouts[i], theme.Tertiary, DrawTextOptions.None);
                }
            }
        });
        foreach (var layout in layouts)
        {
            layout.Dispose();
        }

        // La línea actual se pinta encima, en blanco, en su propia capa pequeña.
        _lyricsHighlight = AddSurface(c, new RectF(0, 0, panel.W, 1), _lyricsScroll, _lyricsLayers);
        _lyricsHighlight.Visual.SetOpacity(0);
        var scroll = _lyricsScroll;
        _scrollY = new AnimatedValue(v => scroll.SetOffsetY(v), a => scroll.SetOffsetY(a), 0);
        UpdateHighlight(animate: false);
        c.Commit();
    }

    private void UpdateHighlight() => UpdateHighlight(animate: true);

    private void UpdateHighlight(bool animate)
    {
        if (Context is not { } c || _lyricsHighlight is not { } highlight || _scrollY is null || !_music.Lyrics.IsSynced)
        {
            return;
        }
        int index = _music.CurrentLine;
        var panel = _l.LyricsPanel;
        (float Y, float H) target = index >= 0 && index < _lineRects.Length ? _lineRects[index]
            : _lineRects.Length > 0 ? _lineRects[0] : (0f, 0f);

        if (index >= 0 && index < _lineRects.Length)
        {
            var text = _music.Lyrics.Lines[index].Text is { Length: > 0 } t ? t : "♪";
            highlight.Resize(panel.W, MathF.Max(1, target.H), c.Scale);
            highlight.SetOffset(0, target.Y);
            highlight.Draw(dc =>
            {
                using var layout = c.Theme.DWrite.CreateTextLayout(text, c.Theme.Lyric, panel.W, 1000f);
                dc.DrawTextLayout(default, layout, c.Theme.Primary, DrawTextOptions.None);
            });
            highlight.Visual.SetOpacity(1);
        }
        else
        {
            highlight.Visual.SetOpacity(0);
        }

        // Centramos la línea actual en el panel.
        float offset = c.Px(panel.H / 2 - (target.Y + target.H / 2));
        if (animate && IsVisible)
        {
            _scrollY.AnimateTo(c.Device, offset, 0.45, Ease.OutCubic);
        }
        else
        {
            _scrollY.Set(offset);
        }
        c.Commit();
    }

    /// <summary>Línea de la letra bajo una coordenada Y de la página (o null).</summary>
    private int? LineAt(float y)
    {
        if (_scrollY is null || Context is not { } c || _lineRects.Length == 0)
        {
            return null;
        }
        float listY = y - _l.LyricsPanel.Y - _scrollY.Current / c.Scale;
        for (int i = 0; i < _lineRects.Length; i++)
        {
            if (listY >= _lineRects[i].Y - 3f * c.F && listY < _lineRects[i].Y + _lineRects[i].H + 3f * c.F)
            {
                return i;
            }
        }
        return null;
    }

    private void DisposeLyricsLayers()
    {
        for (int i = _lyricsLayers.Count - 1; i >= 0; i--)
        {
            _lyricsLayers[i].Dispose();
        }
        _lyricsLayers.Clear();
        _lyricsScroll = null;
        _lyricsHighlight = null;
        _scrollY = null;
        _lineRects = [];
    }

    private sealed class VisualRemoval(IDCompositionVisual parent, IDCompositionVisual3 visual) : IDisposable
    {
        public void Dispose()
        {
            parent.RemoveVisual(visual);
            visual.Dispose();
        }
    }

    // ── Distribución ──

    /// <summary>Rectángulos (DIP, relativos a la página) de cada elemento en la vista actual.</summary>
    private sealed record Layout(
        RectF Art, RectF Title, RectF Artist,
        RectF Previous, RectF Play, RectF Next, RectF LyricsToggle, RectF Source,
        RectF Bar, RectF Track, RectF Times,
        RectF VolumeArea, RectF Mute, RectF Volume,
        RectF LyricsPanel)
    {
        public static Layout Create(float w, float h, float f, bool lyricsView) =>
            lyricsView ? CreateLyrics(w, h, f) : CreatePlayer(w, h, f);

        private static Layout CreatePlayer(float w, float h, float f)
        {
            float art = MathF.Min(74f * f, h - 8f * f);
            float x0 = art + 14f * f;
            float textW = w - x0 - 112f * f;
            var bar = new RectF(x0 + 40f * f, 50f * f, w - x0 - 80f * f, 18f * f);
            float trackH = 4f * f;
            return new Layout(
                Art: new RectF(0, (h - art) / 2, art, art),
                Title: new RectF(x0, 7f * f, textW, 21f * f),
                Artist: new RectF(x0, 28f * f, textW, 17f * f),
                Previous: new RectF(w - 102f * f, 5f * f, 32f * f, 32f * f),
                Play: new RectF(w - 68f * f, 3f * f, 36f * f, 36f * f),
                Next: new RectF(w - 32f * f, 5f * f, 32f * f, 32f * f),
                LyricsToggle: new RectF(x0 - 3f * f, 71f * f, 22f * f, 22f * f),
                Source: new RectF(x0 + 26f * f, 72f * f, 150f * f, 20f * f),
                Bar: bar,
                Track: new RectF(bar.X, bar.Y + (bar.H - trackH) / 2, bar.W, trackH),
                Times: new RectF(x0, bar.Y, w - x0, bar.H),
                VolumeArea: new RectF(w - 150f * f, 71f * f, 150f * f, 22f * f),
                Mute: new RectF(w - 150f * f, 71f * f, 22f * f, 22f * f),
                Volume: new RectF(w - 120f * f, 71f * f, 120f * f, 22f * f),
                LyricsPanel: default);
        }

        private static Layout CreateLyrics(float w, float h, float f)
        {
            float left = 186f * f;
            float art = 50f * f;
            float tx = art + 10f * f;
            float mid = left / 2;
            var bar = new RectF(0, 54f * f, left, 14f * f);
            float trackH = 4f * f;
            return new Layout(
                Art: new RectF(0, 2f * f, art, art),
                Title: new RectF(tx, 5f * f, left - tx, 20f * f),
                Artist: new RectF(tx, 25f * f, left - tx, 17f * f),
                Previous: new RectF(mid - 52f * f, 68f * f, 28f * f, 26f * f),
                Play: new RectF(mid - 16f * f, 67f * f, 32f * f, 28f * f),
                Next: new RectF(mid + 24f * f, 68f * f, 28f * f, 26f * f),
                LyricsToggle: new RectF(left - 22f * f, 70f * f, 22f * f, 22f * f),
                Source: default,
                Bar: bar,
                Track: new RectF(bar.X, bar.Y + (bar.H - trackH) / 2, bar.W, trackH),
                Times: default,
                VolumeArea: default,
                Mute: default,
                Volume: default,
                LyricsPanel: new RectF(left + 20f * f, 0, w - left - 20f * f, h));
        }
    }
}
