using MIDIRift.CleanRoom.Features.Midi.Synthesis;
namespace MIDIRift.CleanRoom.Features.Midi;

public readonly record struct EngineChannelSnapshot(
    int RuntimeIndex,
    MidiChannelId Id,
    bool IsPercussion,
    int Program,
    ChiptuneWaveType WaveType,
    float Volume,
    float Expression,
    float Pan,
    float PitchBendRatio,
    bool Sustain,
    int ActiveVoiceCount,
    int HeldNoteCount,
    float PeakEnvelope,
    float UserGain,
    bool Muted,
    bool Solo);

public readonly record struct EngineVoiceSnapshot(
    int ChannelIndex,
    int Note,
    float Velocity,
    bool KeyHeld,
    bool SustainHeld,
    VoiceEnvelopeStage EnvelopeStage,
    float EnvelopeLevel);

public sealed class EngineSnapshot
{
    private readonly EngineChannelSnapshot[] _channels;
    private readonly EngineVoiceSnapshot[] _voices;

    internal EngineSnapshot(int channelCapacity, int voiceCapacity)
    {
        if (channelCapacity < 0) throw new ArgumentOutOfRangeException(nameof(channelCapacity));
        if (voiceCapacity < 0) throw new ArgumentOutOfRangeException(nameof(voiceCapacity));
        _channels = new EngineChannelSnapshot[channelCapacity];
        _voices = new EngineVoiceSnapshot[voiceCapacity];
    }

    public long Sequence { get; internal set; }
    public double PositionSeconds { get; internal set; }
    public double DurationSeconds { get; internal set; }
    public bool IsPlaying { get; internal set; }
    public bool IsFinished { get; internal set; }
    public int ChannelCount { get; internal set; }
    public int VoiceCount { get; internal set; }

    public ReadOnlySpan<EngineChannelSnapshot> Channels => _channels.AsSpan(0, ChannelCount);
    public ReadOnlySpan<EngineVoiceSnapshot> Voices => _voices.AsSpan(0, VoiceCount);

    internal EngineChannelSnapshot[] WritableChannels => _channels;
    internal EngineVoiceSnapshot[] WritableVoices => _voices;
}

internal sealed class EngineSnapshotPublisher
{
    private readonly EngineSnapshot[] _buffers;
    private int _writeIndex;
    private long _sequence;
    private EngineSnapshot _current;

    public EngineSnapshotPublisher(int channelCapacity, int voiceCapacity)
    {
        _buffers =
        [
            new EngineSnapshot(channelCapacity, voiceCapacity),
            new EngineSnapshot(channelCapacity, voiceCapacity)
        ];
        _current = _buffers[0];
        _writeIndex = 1;
    }

    public EngineSnapshot Current => Volatile.Read(ref _current);

    public EngineSnapshot BeginWrite()
    {
        EngineSnapshot target = _buffers[_writeIndex];
        target.Sequence = Interlocked.Increment(ref _sequence);
        target.ChannelCount = 0;
        target.VoiceCount = 0;
        target.IsFinished = false;
        return target;
    }

    public EngineSnapshot Publish(EngineSnapshot target)
    {
        Volatile.Write(ref _current, target);
        _writeIndex ^= 1;
        return target;
    }
}
