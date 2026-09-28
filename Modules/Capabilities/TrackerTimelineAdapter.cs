namespace MIDIRift.Modules.Capabilities;

/// <summary>Read-only projection over TrackerPlayer for modular visualizers.</summary>
public sealed class TrackerTimelineAdapter : ITrackerTimelineSource, IDisposable
{
    private readonly TrackerPlayer _player;

    public TrackerTimelineAdapter(TrackerPlayer player)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _player.OnFrameTick += ForwardFrame;
    }

    public GridRow[][] Grid => _player.Grid;
    public int CurrentRow => _player.CurrentRow;
    public bool IsFinished => _player.IsFinished;
    public event Action<float>? FrameAdvanced;

    private void ForwardFrame(float row) => FrameAdvanced?.Invoke(row);

    public void Dispose() => _player.OnFrameTick -= ForwardFrame;
}
