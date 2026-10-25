using System.Text.RegularExpressions;

namespace Tilde.Core.Lyrics;

/// <summary>
/// Limpieza de títulos y artistas para encontrar la letra. Los reproductores web suelen enviar
/// cosas como «Artista - Canción (Official Video)» con el canal de YouTube como artista
/// («ArtistaVEVO», «Artista - Topic»).
/// </summary>
public static partial class TrackNames
{
    /// <summary>Devuelve título y artista sin «ruido». Si no hay nada que limpiar, los devuelve igual.</summary>
    public static (string Title, string Artist) Clean(string title, string artist)
    {
        title = title.Trim();
        artist = artist.Trim();
        bool channelLike = ChannelSuffix().IsMatch(artist);
        artist = ChannelSuffix().Replace(artist, string.Empty).Trim();

        // «Artista - Canción»: típico de vídeos. Lo separamos si el «artista» es un canal,
        // está vacío o coincide con la primera parte del título.
        int dash = title.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0)
        {
            var left = title[..dash].Trim();
            var right = title[(dash + 3)..].Trim();
            if (artist.Length == 0 || channelLike || left.Equals(artist, StringComparison.OrdinalIgnoreCase))
            {
                artist = left;
                title = right;
            }
        }

        title = Noise().Replace(title, string.Empty);
        title = Featuring().Replace(title, string.Empty);
        title = RemasterSuffix().Replace(title, string.Empty);
        int bar = title.IndexOf(" | ", StringComparison.Ordinal);
        if (bar > 0)
        {
            title = title[..bar];
        }
        return (Spaces().Replace(title, " ").Trim(), Spaces().Replace(artist, " ").Trim());
    }

    /// <summary>Nombre legible del reproductor a partir de su identificador de app.</summary>
    public static string FriendlyAppName(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId))
        {
            return string.Empty;
        }
        var id = appId;
        int bang = id.LastIndexOf('!');           // Apps de la Tienda: «Paquete_xyz!App»
        if (bang >= 0)
        {
            var package = id[..bang];
            int underscore = package.IndexOf('_');
            id = underscore > 0 ? package[..underscore] : id[(bang + 1)..];
            int dot = id.LastIndexOf('.');
            if (dot >= 0 && dot < id.Length - 1)
            {
                id = id[(dot + 1)..];
            }
        }
        id = Path.GetFileNameWithoutExtension(id.Replace('\\', '/').Split('/')[^1]);
        return id.ToLowerInvariant() switch
        {
            "chrome" => "Chrome",
            "msedge" => "Edge",
            "firefox" or "308046b0af4a39cb" => "Firefox",
            "spotifymusic" or "spotify" => "Spotify",
            "zunemusic" or "microsoft.zunemusic" => "Reproductor multimedia",
            _ => id.Length > 0 ? char.ToUpperInvariant(id[0]) + id[1..] : id,
        };
    }

    [GeneratedRegex(@"\s*(-\s*Topic|VEVO|\s+Official)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ChannelSuffix();

    // (Official Video), [Lyrics], (Audio), (Visualizer), (HD), (Letra)… en paréntesis o corchetes.
    [GeneratedRegex(@"\s*[\(\[][^\)\]]*\b(official|video|audio|lyrics?|letra|visualizer|visualiser|hd|4k|hq|mv|m/v|clip)\b[^\)\]]*[\)\]]", RegexOptions.IgnoreCase)]
    private static partial Regex Noise();

    [GeneratedRegex(@"\s*[\(\[]?\s*\b(feat\.?|ft\.?|featuring)\s+[^\)\]]*[\)\]]?", RegexOptions.IgnoreCase)]
    private static partial Regex Featuring();

    [GeneratedRegex(@"\s*-\s*(\d{4}\s+)?remaster(ed)?(\s+\d{4})?(\s+version)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex RemasterSuffix();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();
}
