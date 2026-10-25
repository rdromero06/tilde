using Tilde.App.Alerts;
using Tilde.App.Pages;
using Tilde.Platform;
using Tilde.Platform.Interop;
using Tilde.Render;
using static Tilde.Platform.Interop.Native;

namespace Tilde.App.Notch;

/// <summary>Avisos y campo de texto del notch.</summary>
internal sealed unsafe partial class NotchWindow : INotchHost
{
    private const nuint TimerAlert = 4;

    /// <summary>Tras quitar el ratón de un aviso, se cierra solo pasado este tiempo.</summary>
    private const uint AlertLeaveDismissMs = 3000;

    private Alert? _shownAlert;
    private TextInputBox? _textInput;

    private bool InputOpen => _textInput?.IsOpen == true;

    // ── Avisos ──

    /// <summary>Muestra, cambia u oculta el aviso según la cola y la visibilidad del notch.</summary>
    private void UpdateAlert()
    {
        var alert = _hidden ? null : _alerts.Current;
        if (ReferenceEquals(alert, _shownAlert))
        {
            return;
        }
        KillTimer(Handle, TimerAlert);
        if (alert is null)
        {
            _shownAlert = null;
            Render(() => _view?.HideAlert(expand: false, animate: !_hidden));
            SetTimer(Handle, TimerRegion, RegionDelayMs, 0);
            return;
        }

        if (_textInput?.IsOpen == true)
        {
            _textInput.Close(submit: false);
        }
        _expanded = false;
        _shownAlert = alert;
        Render(() => _view?.ShowAlert(alert));
        UpdateRegion();
        if (alert.Sound is { } sound && _settings.Current.SoundsEnabled)
        {
            Sounds.Play(sound);
        }
        if (alert.AutoDismiss is { } after)
        {
            SetTimer(Handle, TimerAlert, (uint)after.TotalMilliseconds, 0);
        }
    }

    private void AlertMouseMove(int x, int y)
    {
        if (!_trackingMouse)
        {
            TrackMouseLeave();
        }
        KillTimer(Handle, TimerAlert); // Mientras lo miras, no se cierra.
        var (wx, wy) = ToWindowDip(x, y);
        Render(() => _view?.SetAlertHover(_view.HitTestAlert(wx, wy)));
    }

    private void AlertMouseLeave()
    {
        Render(() => _view?.SetAlertHover(NotchView.AlertHitNone));
        if (_shownAlert?.AutoDismiss is not null)
        {
            SetTimer(Handle, TimerAlert, AlertLeaveDismissMs, 0);
        }
    }

    private void AlertClick(int x, int y)
    {
        if (_view is null || _shownAlert is not { } alert)
        {
            return;
        }
        var (wx, wy) = ToWindowDip(x, y);
        int hit = _view.HitTestAlert(wx, wy);
        if (hit == NotchView.AlertHitNone)
        {
            return;
        }
        // Primero se cierra el aviso y luego se ejecuta la acción (que puede crear otro).
        _alerts.Dismiss();
        if (hit >= 0 && hit < alert.Actions.Count)
        {
            alert.Actions[hit].Invoke();
        }
    }

    private void OnAlertTimer()
    {
        KillTimer(Handle, TimerAlert);
        if (_shownAlert is not null)
        {
            _alerts.Dismiss();
        }
    }

    private (float X, float Y) ToWindowDip(int x, int y) => (x / _geo.Scale, y / _geo.Scale);

    // ── Campo de texto ──

    /// <summary>
    /// Abre el campo de texto nativo encima de un rectángulo de la página activa. Mientras está
    /// abierto el notch no se cierra aunque el ratón salga.
    /// </summary>
    public void RequestTextInput(RectF rectInPage, string placeholder, string text, Action<string> onSubmit)
    {
        if (!_expanded || _view is null || !GetWindowRect(Handle, out var window))
        {
            return;
        }
        _textInput ??= new TextInputBox(Handle);
        float s = _geo.Scale;
        int left = window.Left + (int)MathF.Round((_geo.MarginDip + _geo.Content.X + rectInPage.X) * s);
        int top = window.Top + (int)MathF.Round((_geo.Content.Y + rectInPage.Y) * s);
        int height = (int)MathF.Round(MathF.Max(rectInPage.H, 26f * _geo.F) * s);
        var rect = new RECT { Left = left, Top = top, Right = left + (int)MathF.Round(rectInPage.W * s), Bottom = top + height };
        KillTimer(Handle, TimerCollapse);
        _textInput.Open(rect, (int)MathF.Round(13f * _geo.F * s), placeholder, text, onSubmit, OnTextInputClosed);
    }

    private void OnTextInputClosed()
    {
        // Si el ratón ya no está sobre el notch, se cierra como de costumbre.
        if (GetCursorPos(out var point) && GetWindowRect(Handle, out var window) &&
            (point.X < window.Left || point.X >= window.Right || point.Y < window.Top || point.Y >= window.Bottom))
        {
            SetTimer(Handle, TimerCollapse, CollapseDelayMs, 0);
        }
        else
        {
            TrackMouseLeave();
        }
    }
}
