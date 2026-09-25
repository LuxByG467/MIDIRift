using System.Text.Json;

namespace MIDIRift;

public sealed class EngineSettings
{
    public int RenderBlockFrames { get; set; } = 1024;
    public int RingBufferBlocks { get; set; } = 12;
    public int AudioTrackBufferMultiplier { get; set; } = 12;
    public string AudioBackend { get; set; } = "AAudio";

    public EngineSettings Normalize()
    {
        int[] allowed = [256, 512, 1024, 2048];
        if (!allowed.Contains(RenderBlockFrames)) RenderBlockFrames = 1024;
        RingBufferBlocks = Math.Clamp(RingBufferBlocks, 4, 32);
        AudioTrackBufferMultiplier = Math.Clamp(AudioTrackBufferMultiplier, 4, 24);
        AudioBackend = string.Equals(AudioBackend, "AudioTrack", StringComparison.OrdinalIgnoreCase)
            ? "AudioTrack"
            : "AAudio";
        return this;
    }

    public EngineSettings Clone() => new()
    {
        RenderBlockFrames = RenderBlockFrames,
        RingBufferBlocks = RingBufferBlocks,
        AudioTrackBufferMultiplier = AudioTrackBufferMultiplier,
        AudioBackend = AudioBackend,
    };
}

public static class EngineSettingsStore
{
    public const string DefaultJson = "{\n  \"RenderBlockFrames\": 1024,\n  \"RingBufferBlocks\": 12,\n  \"AudioTrackBufferMultiplier\": 12\n}";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static EngineSettings Load()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<EngineSettings>(File.ReadAllText(AppDataPaths.EngineSettings));
            return (settings ?? new EngineSettings()).Normalize();
        }
        catch
        {
            FirstRunInitializer.Quarantine(AppDataPaths.EngineSettings);
            var settings = new EngineSettings();
            Save(settings);
            return settings;
        }
    }

    public static void Save(EngineSettings settings)
    {
        settings.Normalize();
        FirstRunInitializer.AtomicWriteAllText(
            AppDataPaths.EngineSettings,
            JsonSerializer.Serialize(settings, JsonOptions));
    }
}

/// <summary>
/// Snapshot inmutable durante toda la ejecución. Guardar ajustes no altera
/// motores ya creados ni canciones cargadas; se leen de nuevo al reiniciar.
/// </summary>
public static class EngineSettingsRuntime
{
    private static EngineSettings _current = new();
    public static EngineSettings Current => _current;
    public static void Initialize() => _current = EngineSettingsStore.Load().Clone();
}
