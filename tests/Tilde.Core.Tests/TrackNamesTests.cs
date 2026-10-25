using Tilde.Core.Lyrics;

namespace Tilde.Core.Tests;

public class TrackNamesTests
{
    [Theory]
    [InlineData("Bohemian Rhapsody (Official Video Remastered)", "Queen Official", "Bohemian Rhapsody", "Queen")]
    [InlineData("Queen - Bohemian Rhapsody (Official Video)", "QueenVEVO", "Bohemian Rhapsody", "Queen")]
    [InlineData("Despacito", "Luis Fonsi - Topic", "Despacito", "Luis Fonsi")]
    [InlineData("Let It Be - Remastered 2009", "The Beatles", "Let It Be", "The Beatles")]
    [InlineData("Song (feat. Someone)", "Artist", "Song", "Artist")]
    [InlineData("Canción [Letra]", "Artista", "Canción", "Artista")]
    [InlineData("Hysteria", "Muse", "Hysteria", "Muse")]
    public void Limpia_titulos_tipicos(string title, string artist, string expectedTitle, string expectedArtist)
    {
        var (t, a) = TrackNames.Clean(title, artist);
        Assert.Equal(expectedTitle, t);
        Assert.Equal(expectedArtist, a);
    }

    [Fact]
    public void No_separa_guiones_si_el_artista_es_otro()
    {
        var (t, a) = TrackNames.Clean("Seven Nation Army - Live", "The White Stripes");
        Assert.Equal("Seven Nation Army - Live", t);
        Assert.Equal("The White Stripes", a);
    }

    [Theory]
    [InlineData("Spotify.exe", "Spotify")]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "Spotify")]
    [InlineData("chrome", "Chrome")]
    [InlineData("MSEdge", "Edge")]
    [InlineData("", "")]
    public void Nombre_amigable_del_reproductor(string appId, string expected) =>
        Assert.Equal(expected, TrackNames.FriendlyAppName(appId));
}
