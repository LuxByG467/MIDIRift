namespace MIDIRift.Modules.Capabilities;

/// <summary>Engine capability for inspecting and hot-swapping channel wave types.</summary>
public interface IWaveTypeControl
{
    IReadOnlyList<WaveType> WaveTypes { get; }
    void SetWaveType(int channel, WaveType waveType);
}
