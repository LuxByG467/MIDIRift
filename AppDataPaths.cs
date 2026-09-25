using Microsoft.Maui.Storage;

namespace MIDIRift;

/// <summary>
/// Rutas persistentes únicas de la aplicación. En Android no se debe mezclar
/// Environment.SpecialFolder.ApplicationData con FileSystem.AppDataDirectory:
/// dependiendo del runtime o del estado de instalación pueden resolver a
/// ubicaciones distintas.
/// </summary>
public static class AppDataPaths
{
    public static string Root => Path.Combine(FileSystem.AppDataDirectory, "MIDIRift");
    public static string Config => Path.Combine(Root, "config");

    public static string Library => Path.Combine(Root, "library.json");
    public static string Playlists => Path.Combine(Root, "playlists.json");
    public static string EqPresets => Path.Combine(Config, "eq_presets.json");
    public static string WaveTypes => Path.Combine(Config, "wavetypes.json");
    public static string EngineSettings => Path.Combine(Config, "engine_settings.json");
    public static string ChannelVolumes => Path.Combine(Config, "channel_volumes.json");
    public static string FirstRunMarker => Path.Combine(Config, ".initialized-v1");
    public static string StartupLog => Path.Combine(Root, "startup-error.txt");
}
