namespace MIDIRift.Modules.Playback;

/// <summary>
/// Application-facing playback boundary. Stage D separates observation from
/// commands while keeping the current MainPage implementation behind the service.
/// </summary>
public interface IPlaybackService : IPlaybackState, IPlaybackCommands
{
}
