using Tilde.Core.Settings;

namespace Tilde.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tilde-tests-" + Guid.NewGuid().ToString("N"));

    public SettingsStoreTests() => Directory.CreateDirectory(_dir);

    private string SettingsPath => Path.Combine(_dir, "settings.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Sin_archivo_usa_valores_por_defecto()
    {
        using var store = new SettingsStore(SettingsPath);
        Assert.Equal(NotchSize.Auto, store.Current.NotchSize);
        Assert.True(store.Current.HideInFullscreen);
        Assert.False(store.Current.MinimalNotch);
    }

    [Fact]
    public void Guarda_y_recarga_con_enums_legibles()
    {
        using (var store = new SettingsStore(SettingsPath))
        {
            store.Update(s => { s.NotchSize = NotchSize.Large; s.MinimalNotch = true; s.LastPage = 3; });
        }
        var json = File.ReadAllText(SettingsPath);
        Assert.Contains("\"notchSize\": \"Large\"", json);

        using var reloaded = new SettingsStore(SettingsPath);
        Assert.Equal(NotchSize.Large, reloaded.Current.NotchSize);
        Assert.True(reloaded.Current.MinimalNotch);
        Assert.Equal(3, reloaded.Current.LastPage);
    }

    [Fact]
    public void Un_archivo_danado_no_rompe_la_app_y_se_guarda_copia()
    {
        File.WriteAllText(SettingsPath, "{ esto no es json");
        using var store = new SettingsStore(SettingsPath);
        Assert.Equal(NotchSize.Auto, store.Current.NotchSize);
        Assert.True(File.Exists(SettingsPath + ".bad"));
    }

    [Fact]
    public void TryReload_ignora_el_propio_guardado_y_detecta_cambios_externos()
    {
        using var store = new SettingsStore(SettingsPath);
        store.Save();
        Assert.False(store.TryReload());

        File.WriteAllText(SettingsPath, "{ \"minimalNotch\": true, /* comentario */ }");
        Assert.True(store.TryReload());
        Assert.True(store.Current.MinimalNotch);
    }

    [Fact]
    public void TryReload_ignora_json_invalido_y_conserva_lo_anterior()
    {
        using var store = new SettingsStore(SettingsPath);
        store.Update(s => s.MinimalNotch = true);
        File.WriteAllText(SettingsPath, "{ roto");
        Assert.False(store.TryReload());
        Assert.True(store.Current.MinimalNotch);
    }
}
