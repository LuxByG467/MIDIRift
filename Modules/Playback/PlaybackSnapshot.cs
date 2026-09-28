namespace MIDIRift.Modules.Playback;

/// <summary>
/// Immutable public view of the current playback state.
/// </summary>
public sealed record PlaybackSnapshot(
    bool HasTrack,
    bool IsPlaying,
    string Title,
    string? CurrentTrackId,
    float ElapsedSeconds,
    float TotalSeconds,
    string Format,
    string Engine,
    int? SampleRate,
    int? Kbps,
    PlaybackStatus Status);
