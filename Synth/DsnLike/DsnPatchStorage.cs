using System.Text.Json;

namespace MIDIRift.Synth.DsnLike;

/// <summary>Versioned, engine-owned serialization for portable DSN patches and banks.</summary>
public static class DsnPatchStorage
{
    public const string PatchFormat = "midirift.dsn.patch";
    public const string BankFormat = "midirift.dsn.bank";
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static void ExportPatch(string path, string name, DsnLikePatch patch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Validate(patch);
        var doc = new PatchDocument { Name = NormalizeName(name), Patch = patch };
        File.WriteAllText(path, JsonSerializer.Serialize(doc, JsonOptions));
    }

    public static (string Name, DsnLikePatch Patch) ImportPatch(string path)
    {
        var doc = JsonSerializer.Deserialize<PatchDocument>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("El archivo DSN patch está vacío.");
        if (doc.Format != PatchFormat || doc.Version is < 1 or > CurrentVersion || doc.Patch is null)
            throw new InvalidDataException("Formato o versión de DSN patch no compatible.");
        Validate(doc.Patch);
        return (NormalizeName(doc.Name), doc.Patch);
    }

    public static void Validate(DsnLikePatch p)
    {
        ArgumentNullException.ThrowIfNull(p);
        static void R(float v, float min, float max, string n)
        { if (!float.IsFinite(v) || v < min || v > max) throw new InvalidDataException($"{n} fuera de rango."); }
        R(p.Osc1Level,0,1.5f,nameof(p.Osc1Level)); R(p.Osc2Level,0,1.5f,nameof(p.Osc2Level));
        R(p.Osc2Semitones,-48,48,nameof(p.Osc2Semitones)); R(p.PulseWidth1,.01f,.99f,nameof(p.PulseWidth1)); R(p.PulseWidth2,.01f,.99f,nameof(p.PulseWidth2));
        R(p.FmAmount,0,2,nameof(p.FmAmount)); R(p.AttackSeconds,0,10,nameof(p.AttackSeconds)); R(p.DecaySeconds,0,10,nameof(p.DecaySeconds));
        R(p.SustainLevel,0,1,nameof(p.SustainLevel)); R(p.ReleaseSeconds,0,20,nameof(p.ReleaseSeconds)); R(p.CutoffHz,20,22000,nameof(p.CutoffHz));
        R(p.Resonance,0,.99f,nameof(p.Resonance)); R(p.EnvelopeToCutoff,-1,1,nameof(p.EnvelopeToCutoff)); R(p.LfoHz,0,50,nameof(p.LfoHz));
        R(p.LfoToPitch,-1,1,nameof(p.LfoToPitch)); R(p.LfoToCutoff,-1,1,nameof(p.LfoToCutoff)); R(p.LfoToPulseWidth,-1,1,nameof(p.LfoToPulseWidth));
        R(p.Drive,0,2,nameof(p.Drive)); R(p.OutputGain,0,2,nameof(p.OutputGain));
    }

    internal static string NormalizeName(string? name) => string.IsNullOrWhiteSpace(name) ? "Untitled DSN Patch" : name.Trim();

    private sealed class PatchDocument
    {
        public string Format { get; set; } = PatchFormat;
        public int Version { get; set; } = CurrentVersion;
        public string Name { get; set; } = "Untitled DSN Patch";
        public DsnLikePatch? Patch { get; set; }
    }
}
