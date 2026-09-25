namespace MIDIRift;

/// <summary>
/// Versión visible de producto. Es deliberadamente independiente de
/// ApplicationVersion/VersionCode/PackageVersion: cambiar esta etiqueta NO
/// modifica el versionado interno que Android usa para instalar el paquete.
/// </summary>
public static class MidiRiftVersion
{
    public const string Version = "0.14.0-DSN0.2.23.1-alpha-FirstRunUiStateHotfix";
    public const string Build = "2026.09.20";

    public static string Display => $"MIDIRift {Version}";
    public static string FullDisplay => $"{Display} • Build {Build}";
}
