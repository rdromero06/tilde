using System.Diagnostics;
using Tilde.Core.Animation;
using Vortice.DirectComposition;

namespace Tilde.Render;

/// <summary>Crea animaciones de DirectComposition a partir de nuestras curvas.</summary>
public static class Anim
{
    /// <summary>
    /// Animación de <paramref name="from"/> a <paramref name="to"/>. Al ser la curva un
    /// polinomio cúbico, cabe en un único segmento AddCubic: el compositor la reproduce exacta.
    /// Los coeficientes se expresan en segundos desde el inicio del segmento.
    /// </summary>
    public static IDCompositionAnimation Create(IDCompositionDevice2 device, float from, float to,
        double duration, Ease ease, double delay = 0)
    {
        var animation = device.CreateAnimation();
        if (delay > 0)
        {
            animation.AddCubic(0, from, 0, 0, 0); // Espera quieto en el valor inicial.
        }
        var (a, b, c) = Easing.Polynomial(ease);
        double d = duration;
        float delta = to - from;
        animation.AddCubic(delay, from,
            (float)(delta * a / d),
            (float)(delta * b / (d * d)),
            (float)(delta * c / (d * d * d)));
        animation.End(delay + duration, to);
        return animation;
    }
}

/// <summary>
/// Una propiedad animable de una capa (opacidad, posición, recorte…). Recuerda su animación
/// en curso para poder calcular su valor actual: si una animación nueva interrumpe a otra
/// (p. ej. el ratón sale mientras el notch aún se abre), parte del punto exacto donde iba
/// y no da saltos.
/// </summary>
public sealed class AnimatedValue
{
    private readonly Action<float> _setValue;
    private readonly Action<IDCompositionAnimation> _setAnimation;
    private float _from;
    private float _to;
    private long _start;
    private double _delay;
    private double _duration;
    private Ease _ease;

    public AnimatedValue(Action<float> setValue, Action<IDCompositionAnimation> setAnimation, float initial)
    {
        _setValue = setValue;
        _setAnimation = setAnimation;
        Set(initial);
    }

    public float Target => _to;

    /// <summary>Valor en este instante (interpolado si hay animación en curso).</summary>
    public float Current
    {
        get
        {
            if (_duration <= 0)
            {
                return _to;
            }
            double t = Stopwatch.GetElapsedTime(_start).TotalSeconds - _delay;
            if (t <= 0)
            {
                return _from;
            }
            float u = (float)Math.Min(1, t / _duration);
            return _from + (_to - _from) * Easing.Evaluate(_ease, u);
        }
    }

    /// <summary>Fija un valor sin animar.</summary>
    public void Set(float value)
    {
        _from = _to = value;
        _duration = 0;
        _setValue(value);
    }

    /// <summary>Anima desde el valor actual hasta <paramref name="to"/>.</summary>
    public void AnimateTo(IDCompositionDevice2 device, float to, double duration, Ease ease, double delay = 0)
    {
        float from = Current;
        if (from == to && _duration <= 0)
        {
            return;
        }
        _from = from;
        _to = to;
        _start = Stopwatch.GetTimestamp();
        _delay = delay;
        _duration = duration;
        _ease = ease;
        // DComp copia la función al asignarla, así que podemos liberar el objeto enseguida.
        using var animation = Anim.Create(device, from, to, duration, ease, delay);
        _setAnimation(animation);
    }
}
