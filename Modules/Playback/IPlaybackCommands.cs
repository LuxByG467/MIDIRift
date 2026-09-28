namespace MIDIRift.Modules.Playback;

/// <summary>
/// High-level commands accepted by the application playback coordinator.
/// Queue semantics for Next/Previous are delegated to Stage E.
/// </summary>
public interface IPlaybackCommands
{
    void PlayTrack(string path);
    void TogglePause();
    void Next();
    void Previous();
    void SeekTo(float seconds);
}
