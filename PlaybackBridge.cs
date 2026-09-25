using MIDIRift.Modules.Playback;

namespace MIDIRift;

/// <summary>
/// Compatibility facade for the pre-modular LibraryPage/MainPage bridge.
/// New code should depend on IPlaybackService / IPlaybackState / IPlaybackCommands.
/// </summary>
public sealed class PlaybackBridge : PlaybackService
{
    private bool _hasTrack;
    private bool _isPlaying;
    private string _title = "";
    private string? _currentTrackId;
    private float _elapsedSeconds;
    private float _totalSeconds;
    private string _format = "";
    private string _engine = "";
    private int? _sampleRate;
    private int? _kbps;

    // Legacy command spelling.
    public void RequestPlay(string path) => PlayTrack(path);

    // Legacy mutable state surface. MainPage is still the publisher in Stage D.
    public bool HasTrack { get => _hasTrack; set => _hasTrack = value; }
    public bool IsPlaying { get => _isPlaying; set => _isPlaying = value; }
    public string Title { get => _title; set => _title = value ?? ""; }
    public string? CurrentTrackId { get => _currentTrackId; set => _currentTrackId = value; }
    public float ElapsedSeconds { get => _elapsedSeconds; set => _elapsedSeconds = value; }
    public float TotalSeconds { get => _totalSeconds; set => _totalSeconds = value; }
    public string Format { get => _format; set => _format = value ?? ""; }
    public string Engine { get => _engine; set => _engine = value ?? ""; }
    public int? SampleRate { get => _sampleRate; set => _sampleRate = value; }
    public int? Kbps { get => _kbps; set => _kbps = value; }

    public void NotifyChanged()
    {
        var status = !_hasTrack
            ? PlaybackStatus.Stopped
            : _isPlaying
                ? PlaybackStatus.Playing
                : PlaybackStatus.Paused;

        Publish(new PlaybackSnapshot(
            _hasTrack,
            _isPlaying,
            _title,
            _currentTrackId,
            _elapsedSeconds,
            _totalSeconds,
            _format,
            _engine,
            _sampleRate,
            _kbps,
            status));
    }
}
