namespace MIDIRift.CleanRoom.Features.Midi;

[Flags]
public enum SongFeatureFlags
{
    None = 0,
    TempoChanges = 1 << 0,
    ProgramChanges = 1 << 1,
    Volume = 1 << 2,
    Pan = 1 << 3,
    Expression = 1 << 4,
    Sustain = 1 << 5,
    PitchBend = 1 << 6,
    AllNotesOff = 1 << 7,
    Percussion = 1 << 8,
    MultiplePorts = 1 << 9,
    Modulation = 1 << 10,
    Portamento = 1 << 11,
    Legato = 1 << 12,
    Brightness = 1 << 13,
    Resonance = 1 << 14,
    ReleaseTime = 1 << 15,
    AttackTime = 1 << 16,
    DecayTime = 1 << 17,
    VibratoRate = 1 << 18,
    VibratoDepth = 1 << 19,
    VibratoDelay = 1 << 20,
    ResetAllControllers = 1 << 21,
    AllSoundOff = 1 << 22,
    PitchBendRange = 1 << 23,
    FineTuning = 1 << 24,
    CoarseTuning = 1 << 25,
    Sostenuto = 1 << 26,
    SoftPedal = 1 << 27,
    PortamentoControl = 1 << 28
}

public enum CompiledMidiEventKind
{
    NoteOn,
    NoteOff,
    Program,
    Volume,
    Pan,
    Expression,
    Sustain,
    PitchBend,
    Modulation,
    PortamentoTime,
    PortamentoSwitch,
    LegatoSwitch,
    Brightness,
    Resonance,
    ReleaseTime,
    AttackTime,
    DecayTime,
    VibratoRate,
    VibratoDepth,
    VibratoDelay,
    ResetAllControllers,
    AllSoundOff,
    PitchBendRange,
    FineTuning,
    CoarseTuning,
    Sostenuto,
    SoftPedal,
    PortamentoControl,
    AllNotesOff
}

public readonly record struct MidiChannelId(int Port, int Channel)
{
    public int DisplayChannel => Channel + 1;
    public override string ToString() => $"P{Port} CH{DisplayChannel:00}";
}

public readonly record struct CompiledMidiEvent(
    long Tick,
    long SamplePosition,
    double TimeSeconds,
    CompiledMidiEventKind Kind,
    int ChannelIndex,
    byte Data1,
    float Value,
    int SourceOrder);

public readonly record struct CompiledEventBatch(
    long SamplePosition,
    int StartIndex,
    int Count);

public readonly record struct TempoPoint(
    long Tick,
    long SamplePosition,
    double TimeSeconds,
    int MicrosecondsPerQuarterNote)
{
    public double BeatsPerMinute => 60_000_000.0 / MicrosecondsPerQuarterNote;
}

public sealed record CompiledChannel(
    int RuntimeIndex,
    MidiChannelId Id,
    int InitialProgram,
    ChiptuneWaveType InitialWaveType,
    string GeneralMidiFamily,
    bool IsPercussion,
    bool HasNotes,
    string? TrackName,
    string? DeviceName);

public sealed class CompiledSong
{
    public const int MidiChannelsPerPort = 16;
    public const int DefaultSampleRate = 44_100;

    public required string SourcePath { get; init; }
    public required int TicksPerQuarterNote { get; init; }
    public required int SampleRate { get; init; }
    public required IReadOnlyList<CompiledMidiEvent> Events { get; init; }
    public required IReadOnlyList<CompiledEventBatch> EventBatches { get; init; }
    public required IReadOnlyList<TempoPoint> TempoMap { get; init; }
    public required IReadOnlyList<CompiledChannel> Channels { get; init; }
    public required SongFeatureFlags Features { get; init; }
    public required long DurationSamples { get; init; }
    public required double DurationSeconds { get; init; }
    public required int SourceEventCount { get; init; }

    public bool Uses(SongFeatureFlags feature) => (Features & feature) != 0;
}
