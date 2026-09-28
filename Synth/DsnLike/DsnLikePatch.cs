namespace MIDIRift.Synth.DsnLike;

/// <summary>
/// First MIDIRift DSN-like patch model. Inspired by classic dual-VCO subtractive
/// synthesis; it is not a bit-exact emulation of KORG DSN-12.
/// </summary>
public sealed record DsnLikePatch
{
    public DsnOscillatorWave Osc1Wave { get; init; } = DsnOscillatorWave.Saw;
    public DsnOscillatorWave Osc2Wave { get; init; } = DsnOscillatorWave.Pulse;

    public float Osc1Level { get; init; } = 0.75f;
    public float Osc2Level { get; init; } = 0.25f;
    public float Osc2Semitones { get; init; } = 0f;
    public float PulseWidth1 { get; init; } = 0.5f;
    public float PulseWidth2 { get; init; } = 0.5f;

    public bool HardSync { get; init; }
    public float FmAmount { get; init; } = 0f;

    public float AttackSeconds { get; init; } = 0.005f;
    public float DecaySeconds { get; init; } = 0.08f;
    public float SustainLevel { get; init; } = 0.75f;
    public float ReleaseSeconds { get; init; } = 0.12f;

    public DsnFilterMode FilterMode { get; init; } = DsnFilterMode.LowPass;
    public float CutoffHz { get; init; } = 12000f;
    public float Resonance { get; init; } = 0.1f;
    public float EnvelopeToCutoff { get; init; } = 0f;

    public DsnLfoWave LfoWave { get; init; } = DsnLfoWave.Triangle;
    public float LfoHz { get; init; } = 5f;
    public float LfoToPitch { get; init; } = 0f;
    public float LfoToCutoff { get; init; } = 0f;
    public float LfoToPulseWidth { get; init; } = 0f;

    public float Drive { get; init; } = 0f;
    public float OutputGain { get; init; } = 0.8f;

    public static DsnLikePatch Default { get; } = new();
}
