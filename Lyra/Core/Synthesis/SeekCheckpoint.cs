using MIDIRift.CleanRoom.Features.Midi;

namespace MIDIRift.CleanRoom.Features.Midi.Synthesis;

public readonly record struct ChannelStateSnapshot(
    int Program,
    ChiptuneWaveType WaveType,
    float Volume,
    float Expression,
    float Pan,
    float PitchBendRatio,
    float PitchBendRangeSemitones,
    float PitchBendNormalized,
    float FineTuningSemitones,
    float CoarseTuningSemitones,
    bool Sustain,
    bool Sostenuto,
    bool SoftPedal,
    float Modulation,
    bool PortamentoEnabled,
    bool LegatoEnabled,
    float PortamentoSeconds,
    int? PortamentoSourceNote,
    float Brightness,
    float Resonance,
    float AttackTime,
    float DecayTime,
    float ReleaseTime,
    float VibratoRate,
    float VibratoDepth,
    float VibratoDelay);

public sealed class SeekCheckpoint
{
    public SeekCheckpoint(
        long samplePosition,
        int nextBatchIndex,
        ChannelStateSnapshot[] channels,
        VoiceState[] voices,
        long voiceStartSequence)
    {
        SamplePosition = samplePosition;
        NextBatchIndex = nextBatchIndex;
        Channels = channels ?? throw new ArgumentNullException(nameof(channels));
        Voices = voices ?? throw new ArgumentNullException(nameof(voices));
        VoiceStartSequence = voiceStartSequence;
    }

    public long SamplePosition { get; }
    public int NextBatchIndex { get; }
    public ChannelStateSnapshot[] Channels { get; }
    public VoiceState[] Voices { get; }
    public long VoiceStartSequence { get; }
}

public sealed class SeekCheckpointCache
{
    private readonly List<SeekCheckpoint> _checkpoints = new();
    private readonly long _intervalSamples;
    private long _nextCaptureSample;

    public SeekCheckpointCache(int sampleRate, double intervalSeconds = 5.0)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (intervalSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(intervalSeconds));

        _intervalSamples = Math.Max(1, (long)Math.Round(sampleRate * intervalSeconds));
        _nextCaptureSample = 0;
    }

    public long NextCaptureSample => _nextCaptureSample;
    public int Count => _checkpoints.Count;

    public void Reset(SeekCheckpoint initial)
    {
        ArgumentNullException.ThrowIfNull(initial);
        _checkpoints.Clear();
        _checkpoints.Add(initial);
        _nextCaptureSample = initial.SamplePosition + _intervalSamples;
    }

    public bool ShouldCapture(long samplePosition) => samplePosition >= _nextCaptureSample;

    public void Add(SeekCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        if (_checkpoints.Count > 0 && checkpoint.SamplePosition <= _checkpoints[^1].SamplePosition)
            return;

        _checkpoints.Add(checkpoint);
        while (_nextCaptureSample <= checkpoint.SamplePosition)
            _nextCaptureSample += _intervalSamples;
    }

    public SeekCheckpoint FindAtOrBefore(long samplePosition)
    {
        int low = 0;
        int high = _checkpoints.Count - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            if (_checkpoints[middle].SamplePosition <= samplePosition)
                low = middle + 1;
            else
                high = middle - 1;
        }

        return _checkpoints[Math.Clamp(high, 0, _checkpoints.Count - 1)];
    }
}
