namespace MIDIRift.Modules.Playback;

/// <summary>
/// Read-only timeline of one playback session. Finished must originate from
/// playback, never from a visualizer.
/// </summary>
public interface IPlaybackTimeline
{
    float PositionSeconds { get; }
    float DurationSeconds { get; }
    bool IsFinished { get; }

    event Action? Finished;
}
