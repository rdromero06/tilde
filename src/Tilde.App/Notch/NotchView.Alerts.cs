using Tilde.App.Alerts;
using Tilde.App.Pages;
using Tilde.Core.Animation;
using Tilde.Platform.Interop;
using Tilde.Render;
using Vortice.DirectWrite;

namespace Tilde.App.Notch;

/// <summary>
/// Avisos: la píldora se transforma en una tarjeta con icono, título, mensaje y botones. Se
/// reutiliza la misma forma animada del notch, así el aviso «sale» de la píldora.
/// </summary>
internal sealed partial class NotchView
{
    public const int AlertHitNone = -1;
    public const int AlertHitClose = -2;

    private DrawingSurface _alertSurface = null!;
    private AnimatedValue _alertOpacity = null!;
    private Alert? _alert;
    private RectF[] _alertButtons = [];
    private int _alertHover = AlertHitNone;

    public bool IsAlertVisible => _alert is not null;

    /// <summary>Rectángulo del aviso en DIP de la ventana.</summary>
    private RectF AlertRect
    {
        get
        {
            float f = _geo.F;
            float w = MathF.Min(_geo.ExpandedWidthDip, 410f * f);
            return new RectF(_geo.CenterDip - w / 2, 0, w, 90f * f);
        }
    }

    public RECT AlertRegion
    {
        get
        {
            var r = AlertRect;
            return new RECT { Left = (int)_geo.Px(r.X), Top = 0, Right = (int)_geo.Px(r.Right), Bottom = (int)_geo.Px(r.Bottom) };
        }
    }

    public void ShowAlert(Alert alert)
    {
        _alert = alert;
        _alertHover = AlertHitNone;
        _isExpanded = false;
        SetBarHover(false, commit: false);
        UpdatePageVisibility();
        DrawAlert();

        var r = AlertRect;
        AnimateShape(new Shape(_geo.Px(r.X), _geo.Px(r.Right), _geo.Px(r.Bottom), 20f * _geo.F * _geo.Scale), ExpandDuration, Ease.OutBack);
        _clockOpacity.AnimateTo(_dev, 0, 0.10, Ease.OutCubic);
        _expandedOpacity.AnimateTo(_dev, 0, 0.12, Ease.OutCubic);
        _alertOpacity.AnimateTo(_dev, 1, 0.22, Ease.OutCubic, delay: 0.08);
        _dev.Commit();
    }

    /// <summary>Quita el aviso y vuelve al notch abierto o cerrado.</summary>
    public void HideAlert(bool expand, bool animate = true)
    {
        if (_alert is null)
        {
            return;
        }
        _alert = null;
        if (animate)
        {
            _alertOpacity.AnimateTo(_dev, 0, 0.12, Ease.OutCubic);
        }
        else
        {
            _alertOpacity.Set(0);
        }
        SetExpanded(expand, animate);
    }

    /// <summary>Índice del botón bajo el punto (DIP de ventana), <see cref="AlertHitClose"/> o <see cref="AlertHitNone"/>.</summary>
    public int HitTestAlert(float x, float y)
    {
        if (_alert is null)
        {
            return AlertHitNone;
        }
        var r = AlertRect;
        float lx = x - r.X, ly = y - r.Y;
        if (AlertClose.Inflate(4f * _geo.F, 4f * _geo.F).Contains(lx, ly))
        {
            return AlertHitClose;
        }
        for (int i = 0; i < _alertButtons.Length; i++)
        {
            if (_alertButtons[i].Contains(lx, ly))
            {
                return i;
            }
        }
        return AlertHitNone;
    }

    public void SetAlertHover(int hit)
    {
        if (hit != _alertHover && _alert is not null)
        {
            _alertHover = hit;
            DrawAlert();
            _dev.Commit();
        }
    }

    private RectF AlertClose => new(AlertRect.W - 34f * _geo.F, 10f * _geo.F, 22f * _geo.F, 22f * _geo.F);

    private void CreateAlertLayer()
    {
        var r = AlertRect;
        _alertSurface = new DrawingSurface(_g);
        _alertSurface.Resize(r.W, r.H, _geo.Scale);
        _alertSurface.SetOffset(r.X, r.Y);
        _alertOpacity = new AnimatedValue(v => _alertSurface.Visual.SetOpacity(v), a => _alertSurface.Visual.SetOpacity(a), 0);
        _fill.Visual.AddVisual(_alertSurface.Visual, false, null);
    }

    private void DrawAlert()
    {
        if (_alert is not { } alert)
        {
            return;
        }
        var theme = _theme;
        float f = _geo.F;
        float w = _alertSurface.WidthDip;
        float h = _alertSurface.HeightDip;

        // Botones alineados a la derecha, en el orden dado.
        float buttonH = 26f * f;
        float gap = 8f * f;
        var widths = alert.Actions.Select(a => Ui.ButtonWidth(theme, a.Label, icon: false)).ToArray();
        float x = w - 16f * f - widths.Sum() - gap * Math.Max(0, widths.Length - 1);
        _alertButtons = new RectF[widths.Length];
        for (int i = 0; i < widths.Length; i++)
        {
            _alertButtons[i] = new RectF(x, h - buttonH - 12f * f, widths[i], buttonH);
            x += widths[i] + gap;
        }

        _alertSurface.Draw(dc =>
        {
            float cx = 36f * f, cy = 32f * f, radius = 18f * f;
            dc.FillEllipse(new Vortice.Direct2D1.Ellipse(new System.Numerics.Vector2(cx, cy), radius, radius), theme.Subtle);
            float icon = 18f * f;
            theme.Icons.Draw(dc, alert.Icon, cx - icon / 2, cy - icon / 2, icon, theme.Accent);

            float tx = 66f * f;
            Text.Draw(dc, theme.DWrite, alert.Title, theme.TrackTitle, new RectF(tx, 12f * f, w - tx - 44f * f, 21f * f), theme.Primary);
            Text.Draw(dc, theme.DWrite, alert.Message, theme.Caption, new RectF(tx, 33f * f, w - tx - 18f * f, 18f * f), theme.Secondary);

            Ui.IconButton(dc, theme, AlertClose, IconKind.Close, _alertHover == AlertHitClose, 0.6f,
                _alertHover == AlertHitClose ? theme.Primary : theme.Tertiary);
            for (int i = 0; i < _alertButtons.Length; i++)
            {
                var action = alert.Actions[i];
                Ui.Button(dc, theme, _alertButtons[i], action.Label, action.Primary, _alertHover == i);
            }
        });
    }
}
