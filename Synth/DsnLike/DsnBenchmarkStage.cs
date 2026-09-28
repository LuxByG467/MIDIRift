namespace MIDIRift.Synth.DsnLike;

public enum DsnBenchmarkStage
{
    LoopBaseline,
    Vco1,
    DualVco,
    EnvelopeVca,
    Filter,
    ControlMod,
    Fm,
    HardSync,
    Drive,
    OutputGain,
    MixAccumulation,
    Full
}

public sealed record DsnStageTiming(
    string Stage,
    double TotalMilliseconds,
    double IncrementalMilliseconds,
    double PercentOfFull,
    double NanosecondsPerVoiceSample);

public sealed record DsnStageBreakdown(
    int Voices,
    int FramesPerBlock,
    int Blocks,
    double DeadlineMilliseconds,
    IReadOnlyList<DsnStageTiming> Timings);
