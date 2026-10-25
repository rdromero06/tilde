using Tilde.Core.Layout;
using Tilde.Core.Settings;

namespace Tilde.Core.Tests;

public class NotchMetricsTests
{
    [Theory]
    [InlineData(1920, 1080, 1.0f)]   // Full HD al 100 %
    [InlineData(2048, 1152, 1.0f)]   // 2560×1440 al 125 %
    [InlineData(2560, 1440, 1.2f)]   // 4K al 150 % o QHD al 100 %
    [InlineData(3440, 1440, 1.2f)]   // Ultrapanorámico 21:9
    [InlineData(2560, 1080, 1.2f)]   // Ultrapanorámico 21:9 pequeño
    public void Auto_es_mayor_en_monitores_anchos(float width, float height, float expected) =>
        Assert.Equal(expected, NotchMetrics.SizeFactor(NotchSize.Auto, width, height));

    [Theory]
    [InlineData(NotchSize.Small, 0.85f)]
    [InlineData(NotchSize.Medium, 1.0f)]
    [InlineData(NotchSize.Large, 1.2f)]
    [InlineData(NotchSize.ExtraLarge, 1.4f)]
    public void Los_tamanos_fijos_no_dependen_del_monitor(NotchSize size, float expected)
    {
        Assert.Equal(expected, NotchMetrics.SizeFactor(size, 1920, 1080));
        Assert.Equal(expected, NotchMetrics.SizeFactor(size, 3440, 1440));
    }
}
