using System.Net;
using Tilde.Core.Lyrics;

namespace Tilde.Core.Tests;

public sealed class LyricsServiceTests : IDisposable
{
    private const string Synced =
        """{"id":1,"trackName":"Hola","artistName":"Yo","duration":200,"instrumental":false,"plainLyrics":"a","syncedLyrics":"[00:01.00]a\n[00:02.00]b"}""";

    private readonly string _cache = Path.Combine(Path.GetTempPath(), "tilde-lyrics-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_cache))
        {
            Directory.Delete(_cache, recursive: true);
        }
    }

    [Fact]
    public async Task Encuentra_letra_sincronizada_y_solo_envia_titulo_artista_y_duracion()
    {
        var fake = new FakeLrclib(_ => (HttpStatusCode.OK, Synced));
        var service = new LyricsService(new LrclibClient("test", fake), _cache);

        var result = await service.FindAsync("Hola", "Yo", TimeSpan.FromSeconds(200.4), default);

        Assert.Equal(LyricsStatus.Synced, result.Status);
        Assert.Equal(2, result.Lines.Count);
        var request = Assert.Single(fake.Requests);
        Assert.Equal("/api/get?track_name=Hola&artist_name=Yo&duration=200", request);
    }

    [Fact]
    public async Task La_segunda_vez_sale_de_la_cache_sin_red()
    {
        var fake = new FakeLrclib(_ => (HttpStatusCode.OK, Synced));
        var service = new LyricsService(new LrclibClient("test", fake), _cache);
        await service.FindAsync("Hola", "Yo", TimeSpan.FromSeconds(200), default);
        await service.FindAsync("Hola", "Yo", TimeSpan.FromSeconds(200), default);
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task Prueba_el_titulo_limpio_y_luego_la_busqueda()
    {
        var fake = new FakeLrclib(path => path.StartsWith("/api/search")
            ? (HttpStatusCode.OK, "[" + Synced + "]")
            : (HttpStatusCode.NotFound, """{"code":404}"""));
        var service = new LyricsService(new LrclibClient("test", fake), _cache);

        var result = await service.FindAsync("Hola (Official Video)", "YoVEVO", TimeSpan.FromSeconds(201), default);

        Assert.Equal(LyricsStatus.Synced, result.Status);
        Assert.Equal(3, fake.Requests.Count);
        Assert.Contains("track_name=Hola&artist_name=Yo", fake.Requests[1]);
    }

    [Fact]
    public async Task No_encontrada_se_guarda_en_cache()
    {
        var fake = new FakeLrclib(path => path.StartsWith("/api/search") ? (HttpStatusCode.OK, "[]") : (HttpStatusCode.NotFound, "{}"));
        var service = new LyricsService(new LrclibClient("test", fake), _cache);

        Assert.Equal(LyricsStatus.NotFound, (await service.FindAsync("X", "Y", TimeSpan.FromSeconds(100), default)).Status);
        int requests = fake.Requests.Count;
        await service.FindAsync("X", "Y", TimeSpan.FromSeconds(100), default);
        Assert.Equal(requests, fake.Requests.Count);
    }

    [Fact]
    public async Task Un_error_de_red_no_se_guarda_en_cache()
    {
        var fake = new FakeLrclib(_ => (HttpStatusCode.ServiceUnavailable, "{}"));
        var service = new LyricsService(new LrclibClient("test", fake), _cache);

        await Assert.ThrowsAsync<LyricsUnavailableException>(() => service.FindAsync("Z", "W", TimeSpan.FromSeconds(90), default));
        Assert.False(Directory.Exists(_cache) && Directory.EnumerateFiles(_cache).Any());
    }

    [Fact]
    public void PickBest_prefiere_sincronizada_y_duracion_cercana()
    {
        var a = new LrclibTrack { Id = 1, Duration = 200, PlainLyrics = "x" };
        var b = new LrclibTrack { Id = 2, Duration = 210, SyncedLyrics = "[00:01.00]x" };
        var c = new LrclibTrack { Id = 3, Duration = 201, SyncedLyrics = "[00:01.00]x" };
        Assert.Equal(3, LyricsService.PickBest([a, b, c], TimeSpan.FromSeconds(200))!.Id);
        Assert.Equal(1, LyricsService.PickBest([a, b], TimeSpan.FromSeconds(200))!.Id);
        Assert.Null(LyricsService.PickBest([b], TimeSpan.FromSeconds(100)));
    }

    /// <summary>Simula LRCLIB: responde según la ruta pedida y anota las peticiones.</summary>
    private sealed class FakeLrclib(Func<string, (HttpStatusCode Code, string Body)> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.PathAndQuery;
            Requests.Add(path);
            var (code, body) = respond(path);
            return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) });
        }
    }
}
