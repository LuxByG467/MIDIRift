namespace MIDIRift.Modules.Playback;

/// <summary>
/// Thin adapter over IChiptunePlayer. It introduces no work in the render path.
/// Pause maps to the existing non-disposing Stop semantics.
/// </summary>
public sealed class MidiPlaybackSessionAdapter : IPlaybackTransport, IPlaybackTimeline
{
    private readonly IChiptunePlayer _player;
    private bool _playing;
    private int _finishedRaised;

    public MidiPlaybackSessionAdapter(IChiptunePlayer player)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _player.OnStepAdvanced += OnStepAdvanced;
    }

    public bool IsPlaying => _playing && !_player.IsFinished;
    public float Speed { get => _player.Speed; set => _player.Speed = value; }
    public float PositionSeconds => _player.VirtualSample / 44100f;
    public float DurationSeconds => _player.TotalSamples / 44100f;
    public bool IsFinished => _player.IsFinished;

    public event Action? Finished;

    public void Play()
    {
        _player.Play();
        _playing = true;
    }

    public void Pause()
    {
        _player.Stop();
        _playing = false;
    }

    public void Stop()
    {
        _player.Stop();
        _playing = false;
    }

    public void SeekTo(float seconds) => _player.SeekTo(seconds);

    private void OnStepAdvanced()
    {
        if (!_player.IsFinished || Interlocked.Exchange(ref _finishedRaised, 1) != 0)
            return;

        _playing = false;
        Finished?.Invoke();
    }
}
