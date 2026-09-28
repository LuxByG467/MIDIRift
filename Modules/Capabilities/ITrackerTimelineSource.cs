namespace MIDIRift.Modules.Capabilities;

/// <summary>
/// Read-only tracker timeline capability. Visualizers consume this instead of
/// depending on TrackerPlayer ownership.
/// </summary>
public interface ITrackerTimelineSource
{
    GridRow[][] Grid { get; }
    int CurrentRow { get; }
    bool IsFinished { get; }
    event Action<float>? FrameAdvanced;
}
