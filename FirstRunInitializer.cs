using MIDIRift.Synth.DsnLike;
using System.Text;
using System.Text.Json;

namespace MIDIRift;

/// <summary>
/// Prepara un almacenamiento válido antes de construir cualquier página.
/// Es idempotente: puede ejecutarse en cada arranque sin borrar datos válidos.
/// </summary>
public static class FirstRunInitializer
{
    public static bool IsFirstRun { get; private set; }

    private static readonly (string Path, string DefaultJson)[] JsonFiles =
    [
        (AppDataPaths.Library, "[]"),
        (AppDataPaths.Playlists, "{\n  \"Playlists\": []\n}"),
        (AppDataPaths.EqPresets,
            "{\n  \"Custom\": [],\n  \"CurrentGains\": [0,0,0,0,0,0,0,0,0,0],\n  \"CurrentPreampDb\": 0\n}"),
        (AppDataPaths.WaveTypes, "{}"),
        (AppDataPaths.EngineSettings, EngineSettingsStore.DefaultJson),
        (AppDataPaths.ChannelVolumes, "{}"),
    ];

    public static void EnsureInitialized()
    {
        Directory.CreateDirectory(AppDataPaths.Root);
        Directory.CreateDirectory(AppDataPaths.Config);

        IsFirstRun = !File.Exists(AppDataPaths.FirstRunMarker);

        foreach (var file in JsonFiles)
            EnsureJsonFile(file.Path, file.DefaultJson);

        EngineSettingsRuntime.Initialize();

        // Engine-owned mutable stores are optional on first run. Loading them
        // here exercises their missing/corrupt recovery before any page or
        // audio engine depends on them. Factory DSN patches live in code, so
        // no User Bank file is required.
        _ = DsnProgramBank.Load();
        _ = DsnDrumKitSettingsStore.Load();

        if (IsFirstRun)
            AtomicWriteAllText(AppDataPaths.FirstRunMarker, "initialized-v2");
    }

    /// <summary>
    /// Reconstruye únicamente los archivos de configuración generados por la
    /// app. Se usa como recuperación automática si el primer arranque limpio
    /// falla antes de construir la interfaz.
    /// </summary>
    public static void RebuildGeneratedFiles()
    {
        Directory.CreateDirectory(AppDataPaths.Root);
        Directory.CreateDirectory(AppDataPaths.Config);

        foreach (var file in JsonFiles)
        {
            Quarantine(file.Path);
            AtomicWriteAllText(file.Path, file.DefaultJson);
        }

        try { File.Delete(AppDataPaths.FirstRunMarker); } catch { }
        EngineSettingsRuntime.Initialize();
    }

    private static void EnsureJsonFile(string path, string defaultJson)
    {
        if (!File.Exists(path))
        {
            AtomicWriteAllText(path, defaultJson);
            return;
        }

        try
        {
            var info = new FileInfo(path);
            if (info.Length == 0)
                throw new InvalidDataException("Archivo JSON vacío.");

            using var _ = JsonDocument.Parse(File.ReadAllText(path));
        }
        catch
        {
            Quarantine(path);
            AtomicWriteAllText(path, defaultJson);
        }
    }

    public static void Quarantine(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            string backup = path + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff");
            File.Move(path, backup, overwrite: true);
        }
        catch
        {
            try { File.Delete(path); } catch { }
        }
    }

    public static void AtomicWriteAllText(string path, string content)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string temp = path + ".tmp";
        File.WriteAllText(temp, content, new UTF8Encoding(false));

        try
        {
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            // Algunos proveedores/sistemas de archivos Android se portan mal
            // con Move(..., overwrite:true). El fallback conserva el arranque
            // en frío sin depender de esa sobrecarga concreta.
            try { File.Delete(path); } catch { }
            try
            {
                File.Move(temp, path);
            }
            catch
            {
                File.WriteAllText(path, content, new UTF8Encoding(false));
                try { File.Delete(temp); } catch { }
            }
        }
    }

    /// <summary>
    /// Carga JSON persistente con la misma política de recuperación usada por
    /// el bootstrap: missing/empty/corrupt => cuarentena (si aplica) + default.
    /// El loader nunca convierte un archivo mutable en requisito de arranque.
    /// </summary>
    public static T LoadJsonOrDefault<T>(string path, Func<T?> deserialize, Func<T> createDefault,
        Func<T, bool>? validate = null, Action<T>? persistDefault = null)
    {
        if (!File.Exists(path))
        {
            var missingDefault = createDefault();
            try { persistDefault?.Invoke(missingDefault); } catch { }
            return missingDefault;
        }

        try
        {
            if (new FileInfo(path).Length == 0)
                throw new InvalidDataException("Archivo persistente vacío.");

            var value = deserialize();
            if (value is null || (validate is not null && !validate(value)))
                throw new InvalidDataException("Estado persistente inválido.");

            return value;
        }
        catch
        {
            Quarantine(path);
            var fallback = createDefault();
            try { persistDefault?.Invoke(fallback); } catch { }
            return fallback;
        }
    }

}
