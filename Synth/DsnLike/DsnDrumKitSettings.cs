using System.Text.Json;

namespace MIDIRift.Synth.DsnLike;

public sealed class DsnDrumPieceSettings
{
    public float TuneSemitones { get; set; } = 0f;
    public float DecayScale { get; set; } = 1f;
    public float GainScale { get; set; } = 1f;
    public float Tone { get; set; } = .5f;
}

public sealed class DsnDrumKitSettings
{
    public const int SchemaVersion = 1;
    public int Version { get; set; } = SchemaVersion;
    public Dictionary<int, DsnDrumPieceSettings> Pieces { get; set; } = new();

    public DsnDrumPieceSettings Get(int note)
    {
        note = Math.Clamp(note, 35, 81);
        if (!Pieces.TryGetValue(note, out var p))
            Pieces[note] = p = new DsnDrumPieceSettings();
        return p;
    }

    public DsnDrumKitSettings Clone()
    {
        var copy = new DsnDrumKitSettings();
        foreach (var (note, p) in Pieces)
            copy.Pieces[note] = new DsnDrumPieceSettings
            {
                TuneSemitones = p.TuneSemitones,
                DecayScale = p.DecayScale,
                GainScale = p.GainScale,
                Tone = p.Tone
            };
        return copy;
    }
}

public static class DsnDrumKitSettingsStore
{
    private const string FileName = "dsnlike-drumkit-v1.json";
    public static string StoragePath => Path.Combine(AppDataPaths.Root, FileName);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static DsnDrumKitSettings Load()
    {
        try
        {
            if (!File.Exists(StoragePath)) return new DsnDrumKitSettings();
            var value = JsonSerializer.Deserialize<DsnDrumKitSettings>(File.ReadAllText(StoragePath), Json);
            if (value is null || value.Version != DsnDrumKitSettings.SchemaVersion)
                return new DsnDrumKitSettings();
            return value;
        }
        catch
        {
            FirstRunInitializer.Quarantine(StoragePath);
            return new DsnDrumKitSettings();
        }
    }

    public static void Save(DsnDrumKitSettings value)
    {
        Directory.CreateDirectory(AppDataPaths.Root);
        FirstRunInitializer.AtomicWriteAllText(
            StoragePath,
            JsonSerializer.Serialize(value, Json));
    }
}
