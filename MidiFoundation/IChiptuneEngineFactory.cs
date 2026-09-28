using MIDIRift.Modules.Capabilities;
using MIDIRift.Modules.Audio;

namespace MIDIRift;

/// <summary>
/// Compatibility bridge for the pre-modular engine factory contract.
/// Stage C engine implementations are real IAudioEngineModule instances.
/// </summary>
public interface IChiptuneEngineFactory : IAudioEngineModule
{
    string EngineId { get; }
}

public sealed class ChiptuneEngineBuild
{
    public IChiptunePlayer Player { get; }
    public TrackerModel TrackerModel { get; }
    public IWaveTypeControl? WaveTypes { get; }
    public IEngineCapabilityProvider Capabilities { get; }
    public IChannelMixer Mixer { get; }
    public IEngineTelemetry Telemetry { get; }

    public ChiptuneEngineBuild(IChiptunePlayer player, TrackerModel trackerModel, IEngineCapabilityProvider? capabilities = null)
    {
        Player = player ?? throw new ArgumentNullException(nameof(player));
        TrackerModel = trackerModel ?? throw new ArgumentNullException(nameof(trackerModel));

        var adapter = new ChiptuneCapabilityAdapter(Player);
        Capabilities = capabilities ?? new StaticEngineCapabilityProvider(EngineToolCapability.WaveTypeSelector);
        WaveTypes = Capabilities.Supports(EngineToolCapability.WaveTypeSelector) ? adapter : null;
        Mixer = adapter;
        Telemetry = adapter;
    }
}
