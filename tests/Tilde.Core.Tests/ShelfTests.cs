using Tilde.Core.Productivity;

namespace Tilde.Core.Tests;

public class ShelfTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private static ShelfList WithFiles(params string[] names)
    {
        var shelf = new ShelfList(new ShelfData());
        shelf.AddPaths(names.Select(n => $@"C:\docs\{n}"), p => !p.Contains('.'), Now);
        return shelf;
    }

    private static Guid Id(ShelfList s, int i) => s.Items[i].Id;

    [Fact]
    public void Guarda_rutas_sin_duplicar_y_distingue_carpetas()
    {
        var shelf = WithFiles("a.txt", "Fotos", "b.pdf");
        shelf.AddPaths([@"C:\DOCS\A.TXT"], _ => false, Now);
        Assert.Equal(3, shelf.Items.Count);
        Assert.Equal(@"C:\DOCS\A.TXT", shelf.Items[^1].Path); // Repetido: pasa al final.
        Assert.Equal(ShelfKind.Folder, shelf.Items.Single(i => i.DisplayName == "Fotos").Kind);
    }

    [Fact]
    public void Texto_se_guarda_como_tarjeta_y_vacio_se_ignora()
    {
        var shelf = new ShelfList(new ShelfData());
        Assert.Null(shelf.AddText("   ", Now));
        var item = shelf.AddText("Hola\r\nmundo", Now)!;
        Assert.Equal(ShelfKind.Text, item.Kind);
        Assert.Equal("Hola mundo", item.DisplayName);
    }

    [Fact]
    public void Clic_ctrl_y_mayus_seleccionan_como_en_windows()
    {
        var s = WithFiles("1.txt", "2.txt", "3.txt", "4.txt", "5.txt");
        s.Click(Id(s, 1), ctrl: false, shift: false);
        Assert.Equal([Id(s, 1)], s.Selected);

        s.Click(Id(s, 3), ctrl: false, shift: true);           // Rango 2..4
        Assert.Equal(3, s.Selected.Count);

        s.Click(Id(s, 2), ctrl: true, shift: false);           // Ctrl alterna: quita el 3
        Assert.Equal(2, s.Selected.Count);
        Assert.False(s.IsSelected(Id(s, 2)));

        s.Click(Id(s, 0), ctrl: false, shift: false);          // Clic simple: solo uno
        Assert.Equal([Id(s, 0)], s.Selected);
    }

    [Fact]
    public void Arrastrar_un_seleccionado_lleva_toda_la_seleccion()
    {
        var s = WithFiles("1.txt", "2.txt", "3.txt");
        s.Click(Id(s, 0), false, false);
        s.Click(Id(s, 2), true, false);
        Assert.Equal(2, s.DragSet(Id(s, 2)).Count);
        Assert.Single(s.DragSet(Id(s, 1)));                     // No seleccionado: solo él.
        Assert.Equal([Id(s, 1)], s.Selected);
    }

    [Fact]
    public void Quitar_y_vaciar_limpian_la_seleccion()
    {
        var s = WithFiles("1.txt", "2.txt");
        s.Click(Id(s, 0), false, false);
        s.Remove([Id(s, 0)]);
        Assert.Single(s.Items);
        Assert.Empty(s.Selected);
        s.Clear();
        Assert.Empty(s.Items);
    }

    [Fact]
    public void Tiene_un_maximo_y_salen_los_mas_antiguos()
    {
        var s = new ShelfList(new ShelfData());
        s.AddPaths(Enumerable.Range(0, ShelfList.MaxItems + 5).Select(i => $@"C:\f{i}.txt"), _ => false, Now);
        Assert.Equal(ShelfList.MaxItems, s.Items.Count);
        Assert.Equal(@"C:\f5.txt", s.Items[0].Path);
    }
}
