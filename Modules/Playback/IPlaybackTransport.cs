namespace MIDIRift.Modules.Playback;

/// <summary>
/// Low-level transport capability of one active playback session.
/// Queue/library selection does not belong here.
/// </summary>
public interface IPlaybackTransport
{
    bool IsPlaying { get; }
    float Speed { get; set; }

    void Play();
    void Pause();
    void Stop();
    void SeekTo(float seconds);
}
