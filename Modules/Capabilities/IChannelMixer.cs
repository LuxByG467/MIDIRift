namespace MIDIRift.Modules.Capabilities;

/// <summary>Per-channel gain capability independent from a concrete synth engine.</summary>
public interface IChannelMixer
{
    int ChannelCount { get; }
    float GetChannelGain(int channel);
    IReadOnlyList<float> GetChannelGains();
    void SetChannelGain(int channel, float gain);
}
