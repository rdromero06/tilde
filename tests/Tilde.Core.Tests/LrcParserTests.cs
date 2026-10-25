using Tilde.Core.Lyrics;

namespace Tilde.Core.Tests;

public class LrcParserTests
{
    [Fact]
    public void Lee_marcas_de_tiempo_en_sus_variantes()
    {
        var lines = LrcParser.Parse("[ar:Alguien]\n[00:01.5]uno\n[00:02.45]dos\r\n[00:03.456]tres\n[01:04]cuatro");
        Assert.Equal(4, lines.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(1500), lines[0].Time);
        Assert.Equal(TimeSpan.FromMilliseconds(2450), lines[1].Time);
        Assert.Equal(TimeSpan.FromMilliseconds(3456), lines[2].Time);
        Assert.Equal(TimeSpan.FromSeconds(64), lines[3].Time);
        Assert.Equal("dos", lines[1].Text);
    }

    [Fact]
    public void Varias_marcas_en_una_linea_y_orden_cronologico()
    {
        var lines = LrcParser.Parse("[00:10.00][00:30.00]Estribillo\n[00:20.00]Estrofa");
        Assert.Equal(["Estribillo", "Estrofa", "Estribillo"], lines.Select(l => l.Text));
        Assert.Equal(TimeSpan.FromSeconds(30), lines[2].Time);
    }

    [Fact]
    public void Aplica_offset_y_conserva_lineas_vacias()
    {
        var lines = LrcParser.Parse("[offset:+500]\n[00:02.00]hola\n[00:04.00]");
        Assert.Equal(TimeSpan.FromSeconds(1.5), lines[0].Time);
        Assert.Equal(string.Empty, lines[1].Text);
    }

    [Theory]
    [InlineData(0.5, -1)]
    [InlineData(1.0, 0)]
    [InlineData(2.9, 1)]
    [InlineData(99, 2)]
    public void IndexAt_devuelve_la_linea_que_suena(double seconds, int expected)
    {
        var lines = LrcParser.Parse("[00:01.00]a\n[00:02.00]b\n[00:03.00]c");
        Assert.Equal(expected, LrcParser.IndexAt(lines, TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Texto_vacio_o_sin_marcas_no_da_lineas()
    {
        Assert.Empty(LrcParser.Parse(null));
        Assert.Empty(LrcParser.Parse("solo texto\nsin tiempos"));
    }
}
