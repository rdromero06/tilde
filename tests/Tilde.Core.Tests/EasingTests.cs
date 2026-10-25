using Tilde.Core.Animation;

namespace Tilde.Core.Tests;

public class EasingTests
{
    [Theory]
    [InlineData(Ease.Linear)]
    [InlineData(Ease.OutCubic)]
    [InlineData(Ease.OutBack)]
    public void Todas_las_curvas_van_de_0_a_1(Ease ease)
    {
        Assert.Equal(0f, Easing.Evaluate(ease, 0f), 5);
        Assert.Equal(1f, Easing.Evaluate(ease, 1f), 5);
    }

    [Fact]
    public void OutCubic_nunca_se_pasa_ni_retrocede()
    {
        float previous = 0;
        for (int i = 1; i <= 100; i++)
        {
            float v = Easing.Evaluate(Ease.OutCubic, i / 100f);
            Assert.True(v >= previous && v <= 1.00001f);
            previous = v;
        }
    }

    [Fact]
    public void OutBack_rebota_un_poco_por_encima_del_destino()
    {
        float max = Enumerable.Range(0, 101).Max(i => Easing.Evaluate(Ease.OutBack, i / 100f));
        Assert.InRange(max, 1.02f, 1.06f); // Rebote sutil (~4 %), dentro del margen de la ventana.
    }

    [Fact]
    public void Fuera_de_rango_se_recorta()
    {
        Assert.Equal(0f, Easing.Evaluate(Ease.OutCubic, -1f));
        Assert.Equal(1f, Easing.Evaluate(Ease.OutCubic, 2f));
    }
}
