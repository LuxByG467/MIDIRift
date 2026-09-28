namespace MIDIRift.Modules.Capabilities;

/// <summary>
/// Engine-neutral telemetry snapshot. Engines may expose richer private telemetry,
/// but consumers can always rely on these common fields.
/// </summary>
public sealed record EngineTelemetrySnapshot(
    long Sequence,
    double PositionSeconds,
    double DurationSeconds,
    bool IsPlaying,
    bool IsFinished,
    int ChannelCount,
    int ActiveVoiceCount);
