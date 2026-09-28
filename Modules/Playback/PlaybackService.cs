namespace MIDIRift.Modules.Playback;

/// <summary>
/// Stage-D playback boundary. It owns the public state snapshot and command bus.
/// MainPage remains the compatibility host that executes commands until the queue
/// and session ownership move out in Stages E+.
/// </summary>
public class PlaybackService : IPlaybackService
{
    private PlaybackSnapshot _snapshot = new(
        false, false, "", null, 0, 0, "", "", null, null, PlaybackStatus.Stopped);

    public event Action? StateChanged;

    public event Action<string>? PlayRequested;
    public event Action? TogglePauseRequested;
    public event Action? NextRequested;
    public event Action? PreviousRequested;
    public event Action<float>? SeekRequested;

    public PlaybackSnapshot Snapshot => _snapshot;

    public void PlayTrack(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        PlayRequested?.Invoke(path);
    }

    public void TogglePause() => TogglePauseRequested?.Invoke();
    public void Next() => NextRequested?.Invoke();
    public void Previous() => PreviousRequested?.Invoke();

    public void SeekTo(float seconds)
    {
        if (!float.IsFinite(seconds))
            throw new ArgumentOutOfRangeException(nameof(seconds));

        SeekRequested?.Invoke(Math.Max(0f, seconds));
    }

    /// <summary>
    /// Compatibility update path used by MainPage in Stage D. Later the
    /// playback coordinator itself will own these transitions.
    /// </summary>
    public void Publish(PlaybackSnapshot snapshot, bool notify = true)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshot = snapshot;
        if (notify)
            StateChanged?.Invoke();
    }
}
