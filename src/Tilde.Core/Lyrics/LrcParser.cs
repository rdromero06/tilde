using System.Globalization;
using System.Text.RegularExpressions;

namespace Tilde.Core.Lyrics;

/// <summary>Una línea de letra sincronizada.</summary>
public readonly record struct LyricLine(TimeSpan Time, string Text);

/// <summary>
/// Lee el formato LRC: cada línea lleva una o varias marcas de tiempo, p. ej.
/// <c>[01:23.45]Texto</c> o <c>[00:12.00][00:45.10]Estribillo</c>. Admite la etiqueta
/// <c>[offset:+250]</c> y descarta metadatos como <c>[ar:Artista]</c>.
/// </summary>
public static partial class LrcParser
{
    public static IReadOnlyList<LyricLine> Parse(string? lrc)
    {
        if (string.IsNullOrWhiteSpace(lrc))
        {
            return [];
        }

        var lines = new List<LyricLine>();
        var offset = TimeSpan.Zero;
        foreach (var raw in lrc.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (OffsetTag().Match(line) is { Success: true } o &&
                int.TryParse(o.Groups[1].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int ms))
            {
                // Un offset positivo adelanta la letra.
                offset = TimeSpan.FromMilliseconds(ms);
                continue;
            }

            var times = new List<TimeSpan>();
            int pos = 0;
            Match m;
            while ((m = TimeTag().Match(line, pos)).Success && m.Index == pos)
            {
                times.Add(ToTime(m));
                pos = m.Index + m.Length;
            }
            if (times.Count == 0)
            {
                continue; // Metadatos u otra cosa sin marca de tiempo.
            }
            var text = line[pos..].Trim();
            foreach (var t in times)
            {
                lines.Add(new LyricLine(t, text));
            }
        }

        if (offset != TimeSpan.Zero)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                var t = lines[i].Time - offset;
                lines[i] = lines[i] with { Time = t < TimeSpan.Zero ? TimeSpan.Zero : t };
            }
        }
        lines.Sort((a, b) => a.Time.CompareTo(b.Time));
        return lines;
    }

    /// <summary>Índice de la línea que suena en <paramref name="position"/> (−1 si aún no empezó).</summary>
    public static int IndexAt(IReadOnlyList<LyricLine> lines, TimeSpan position)
    {
        int lo = 0, hi = lines.Count - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (lines[mid].Time <= position)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return found;
    }

    private static TimeSpan ToTime(Match m)
    {
        int minutes = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        int seconds = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        int ms = 0;
        if (m.Groups[3].Success)
        {
            // «5» = 500 ms, «45» = 450 ms (centésimas), «456» = 456 ms.
            var fraction = m.Groups[3].Value.PadRight(3, '0')[..3];
            ms = int.Parse(fraction, CultureInfo.InvariantCulture);
        }
        return new TimeSpan(0, 0, minutes, seconds, ms);
    }

    [GeneratedRegex(@"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]")]
    private static partial Regex TimeTag();

    [GeneratedRegex(@"^\s*\[offset:\s*([+-]?\d+)\s*\]", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetTag();
}
