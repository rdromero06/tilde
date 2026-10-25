using System.Runtime.InteropServices;
using static Tilde.Platform.Interop.Native;

namespace Tilde.Platform;

public enum SoundKind
{
    /// <summary>Dos notas suaves ascendentes (Pomodoro).</summary>
    Chime,

    /// <summary>Una «gota» corta (agua).</summary>
    Drop,

    /// <summary>Tres notas (recordatorio de tarea).</summary>
    Bell,
}

/// <summary>
/// Sonidos propios de Tilde, sintetizados al vuelo (no hay archivos de audio): ondas senoidales
/// con caída exponencial, a volumen moderado. Se reproducen de forma asíncrona con PlaySound,
/// que respeta el volumen y el dispositivo de salida de Windows.
/// </summary>
public static unsafe class Sounds
{
    private const int SampleRate = 22050;
    private static readonly Dictionary<SoundKind, nint> Cache = [];

    public static void Play(SoundKind kind)
    {
        if (!Cache.TryGetValue(kind, out var wav))
        {
            Cache[kind] = wav = Build(kind);
        }
        PlaySoundW(wav, 0, SND_MEMORY | SND_ASYNC | SND_NODEFAULT);
    }

    private static nint Build(SoundKind kind)
    {
        // (frecuencia en Hz, inicio en s, caída) de cada nota.
        (double Freq, double Start, double Decay)[] notes = kind switch
        {
            SoundKind.Chime => [(784, 0, 5), (1047, 0.16, 4.5)],
            SoundKind.Bell => [(880, 0, 6), (1109, 0.15, 6), (1319, 0.30, 4.5)],
            _ => [],
        };
        double length = kind switch { SoundKind.Drop => 0.32, SoundKind.Bell => 1.1, _ => 0.9 };
        int samples = (int)(length * SampleRate);
        var pcm = new short[samples];

        for (int i = 0; i < samples; i++)
        {
            double t = (double)i / SampleRate;
            double v = 0;
            if (kind == SoundKind.Drop)
            {
                // Barrido descendente 900 → 420 Hz: suena a gota.
                double phase = 2 * Math.PI * (420 * t + 480 * (1 - Math.Exp(-t * 18)) / 18);
                v = Math.Sin(phase) * Math.Exp(-t * 11) * 0.3;
            }
            else
            {
                foreach (var (freq, start, decay) in notes)
                {
                    double nt = t - start;
                    if (nt < 0) continue;
                    double env = Math.Exp(-nt * decay) * Math.Min(1, nt / 0.006); // ataque de 6 ms sin «clic»
                    v += (Math.Sin(2 * Math.PI * freq * nt) + 0.18 * Math.Sin(4 * Math.PI * freq * nt)) * env * 0.2;
                }
            }
            double fadeOut = Math.Min(1, (length - t) / 0.03);
            pcm[i] = (short)(Math.Clamp(v * fadeOut, -1, 1) * short.MaxValue);
        }

        // Archivo WAV en memoria (PCM 16 bits mono). Se queda reservado: PlaySound lo lee en segundo plano.
        int dataBytes = samples * 2;
        int total = 44 + dataBytes;
        var mem = (byte*)NativeMemory.Alloc((nuint)total);
        var span = new Span<byte>(mem, total);
        "RIFF"u8.CopyTo(span);
        BitConverter.TryWriteBytes(span[4..], total - 8);
        "WAVEfmt "u8.CopyTo(span[8..]);
        BitConverter.TryWriteBytes(span[16..], 16);
        BitConverter.TryWriteBytes(span[20..], (short)1);           // PCM
        BitConverter.TryWriteBytes(span[22..], (short)1);           // mono
        BitConverter.TryWriteBytes(span[24..], SampleRate);
        BitConverter.TryWriteBytes(span[28..], SampleRate * 2);
        BitConverter.TryWriteBytes(span[32..], (short)2);
        BitConverter.TryWriteBytes(span[34..], (short)16);
        "data"u8.CopyTo(span[36..]);
        BitConverter.TryWriteBytes(span[40..], dataBytes);
        MemoryMarshal.AsBytes(pcm.AsSpan()).CopyTo(span[44..]);
        return (nint)mem;
    }
}
