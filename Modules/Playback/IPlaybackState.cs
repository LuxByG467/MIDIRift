namespace MIDIRift.Modules.Playback;

/// <summary>Observable, read-only application playback state.</summary>
public interface IPlaybackState
{
    PlaybackSnapshot Snapshot { get; }
    event Action? StateChanged;
}
