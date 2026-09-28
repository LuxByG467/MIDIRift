namespace MIDIRift.Synth.DsnLike;

/// <summary>Cheap lock-free snapshot for the in-app DSN performance monitor.</summary>
public sealed record DsnRealtimeTelemetrySnapshot(
    long Blocks, long LateBlocks, double MeanRenderMs, double WorstRenderMs,
    double DeadlineMs, int ActiveVoices, int PeakVoices, int DrumVoices,
    int DualVcoVoices, int FmVoices, int SyncVoices, int FilterVoices, int DriveVoices,
    int ManagedRingReadyBlocks, int ManagedRingCapacity, int BackendBufferedFrames,
    int BackendBufferFrames, int BackendBurstFrames, int BackendAdaptiveIncreases,
    int Underruns, long AllocatedBytes, int Gen0, int Gen1, int Gen2);
