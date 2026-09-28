namespace MIDIRift.Modules.Capabilities;

/// <summary>
/// Capability projection over the existing IChiptunePlayer contract.
/// No synthesis/render code is changed.
/// </summary>
public sealed class ChiptuneCapabilityAdapter : IWaveTypeControl, IChannelMixer, IEngineTelemetry
{
    private readonly IChiptunePlayer _player;
    private long _sequence;

    public ChiptuneCapabilityAdapter(IChiptunePlayer player)
        => _player = player ?? throw new ArgumentNullException(nameof(player));

    public IReadOnlyList<WaveType> WaveTypes => _player.GetWaveTypes();
    public void SetWaveType(int channel, WaveType waveType) => _player.SetWaveType(channel, waveType);

    public int ChannelCount => _player.ChannelCount;
    public float GetChannelGain(int channel) => _player.GetChannelGain(channel);
    public IReadOnlyList<float> GetChannelGains() => _player.GetChannelGains();
    public void SetChannelGain(int channel, float gain) => _player.SetChannelGain(channel, gain);

    public EngineTelemetrySnapshot Snapshot => new(
        Interlocked.Increment(ref _sequence),
        _player.VirtualSample / 44100.0,
        _player.TotalSamples / 44100.0,
        !_player.IsFinished,
        _player.IsFinished,
        _player.ChannelCount,
        0);
}
