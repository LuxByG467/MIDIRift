namespace MIDIRift.Modules.Playback;

/// <summary>Thin adapter over the existing MP3 player contract.</summary>
public sealed class Mp3PlaybackSessionAdapter : IPlaybackTransport, IPlaybackTimeline
{
    private readonly IMp3Player _player;

    public Mp3PlaybackSessionAdapter(IMp3Player player)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _player.OnCompleted += OnCompleted;
    }

    public bool IsPlaying => _player.IsPlaying;
    public float Speed { get => _player.Speed; set => _player.Speed = value; }
    public float PositionSeconds => _player.PositionSeconds;
    public float DurationSeconds => _player.DurationSeconds;
    public bool IsFinished => _player.IsFinished;

    public event Action? Finished;

    public void Play() => _player.Play();
    public void Pause() => _player.Pause();
    public void Stop() => _player.Stop();
    public void SeekTo(float seconds) => _player.SeekTo(seconds);

    private void OnCompleted() => Finished?.Invoke();
}
