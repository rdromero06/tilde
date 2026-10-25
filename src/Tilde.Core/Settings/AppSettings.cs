using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tilde.Core.Settings;

/// <summary>Tamaño del notch. «Auto» elige uno mayor en monitores anchos.</summary>
public enum NotchSize
{
    Auto,
    Small,
    Medium,
    Large,
    ExtraLarge,
}

/// <summary>
/// Ajustes persistentes de la app (settings.json). Solo valores simples: el estado de
/// «Iniciar con Windows» vive en el registro, que es la fuente de verdad.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Versión del esquema, para migrar en el futuro sin perder datos.</summary>
    public int Version { get; set; } = 1;

    public NotchSize NotchSize { get; set; } = NotchSize.Auto;

    /// <summary>Cerrado, el notch se reduce a una línea fina.</summary>
    public bool MinimalNotch { get; set; }

    /// <summary>Ocultar el notch cuando hay una app a pantalla completa.</summary>
    public bool HideInFullscreen { get; set; } = true;

    /// <summary>Mostrar la mascota tras un rato de inactividad (fase 5).</summary>
    public bool MascotEnabled { get; set; } = true;

    /// <summary>Buscar letras en LRCLIB (envía solo título, artista y duración).</summary>
    public bool LyricsOnline { get; set; } = true;

    /// <summary>
    /// Con el notch cerrado, mostrar la línea actual de la letra en la píldora. Desactivado por
    /// defecto: la píldora cerrada muestra la hora y la letra se ve al abrir el notch.
    /// </summary>
    public bool LyricsInPill { get; set; }

    /// <summary>La página de música muestra la vista de letras (true) o el reproductor (false).</summary>
    public bool LyricsView { get; set; }

    /// <summary>Última página visitada, para reabrir donde lo dejaste.</summary>
    public int LastPage { get; set; }
}

/// <summary>
/// Serializador generado en compilación: Native AOT no permite reflexión, así que
/// System.Text.Json usa este contexto en lugar de inspeccionar tipos en ejecución.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    UseStringEnumConverter = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
