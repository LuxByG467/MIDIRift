using MIDIRift.Modules;
using MIDIRift.Modules.Capabilities;

namespace MIDIRift;

public sealed class DsnLikeChiptuneEngineFactory : IChiptuneEngineFactory
{
    public const string ModuleId = "midirift.engine.dsnlike";
    public ModuleDescriptor Descriptor { get; } =
        new(ModuleId, "DSN-like", "0.2.23.1", ModuleType.AudioEngine);
    public string EngineId => "dsnlike";
    public void Initialize(IModuleContext context) => ArgumentNullException.ThrowIfNull(context);

    public ChiptuneEngineBuild Create(ChiptuneEngineInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Song.MusicalChannelCount <= 0)
            throw new InvalidDataException("El MIDI no contiene canales musicales reproducibles.");

        var player = new DsnLikeAudioTrackPlayer(input);
        var trackerChannels = LegacyMidiAdapter.BuildChannels(input);
        var trackerModel = TrackerModel.FromChannels(trackerChannels);
        return new ChiptuneEngineBuild(player, trackerModel, new StaticEngineCapabilityProvider(
            EngineToolCapability.PatchEditor | EngineToolCapability.PatchBank |
            EngineToolCapability.DualVco | EngineToolCapability.Fm | EngineToolCapability.HardSync |
            EngineToolCapability.DedicatedDrums));
    }

    public void Dispose() { }
}
