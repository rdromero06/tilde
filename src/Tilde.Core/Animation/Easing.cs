namespace Tilde.Core.Animation;

/// <summary>Curvas de animación disponibles.</summary>
public enum Ease
{
    Linear,

    /// <summary>Arranca rápido y frena suave.</summary>
    OutCubic,

    /// <summary>Como OutCubic pero se pasa un poco (~4 %) y vuelve: da sensación de «vida».</summary>
    OutBack,
}

/// <summary>
/// Matemática de las curvas. Todas son polinomios cúbicos f(u) = A·u + B·u² + C·u³ con
/// f(0) = 0 y f(1) = 1. Esto no es casualidad: DirectComposition anima con segmentos
/// cúbicos, así que podemos entregarle la curva exacta y que la ejecute el compositor de
/// Windows a la frecuencia del monitor (60–240 Hz) sin gastar CPU de nuestro proceso.
/// </summary>
public static class Easing
{
    /// <summary>Intensidad del rebote de OutBack (1,70158 es el clásico; 1 es más sutil).</summary>
    public const float BackStrength = 1.0f;

    public static (float A, float B, float C) Polynomial(Ease ease) => ease switch
    {
        Ease.OutCubic => (3f, -3f, 1f),                     // 1 − (1 − u)³
        Ease.OutBack => (BackStrength + 3f,                  // 1 + c3(u − 1)³ + c1(u − 1)²
                         -2f * BackStrength - 3f,
                         BackStrength + 1f),
        _ => (1f, 0f, 0f),
    };

    /// <summary>Valor de la curva en u ∈ [0, 1].</summary>
    public static float Evaluate(Ease ease, float u)
    {
        u = Math.Clamp(u, 0f, 1f);
        var (a, b, c) = Polynomial(ease);
        return u * (a + u * (b + u * c));
    }
}
