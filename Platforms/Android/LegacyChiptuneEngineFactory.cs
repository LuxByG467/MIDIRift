using MIDIRift.Modules;

namespace MIDIRift;

public sealed class LegacyChiptuneEngineFactory : IChiptuneEngineFactory
{
    public const string ModuleId = "midirift.engine.legacy";

    public ModuleDescriptor Descriptor { get; } =
        new(ModuleId, "Legacy", "1.0.0", ModuleType.AudioEngine);

    public string EngineId => "classic";

    public void Initialize(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
    }

    public ChiptuneEngineBuild Create(ChiptuneEngineInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        List<Channel> channels = LegacyMidiAdapter.BuildChannels(input);
        if (channels.Count == 0)
            throw new InvalidDataException("El MIDI no contiene canales musicales reproducibles.");

        var player = new ChiptuneAudioTrack(channels);
        var trackerModel = TrackerModel.FromChannels(channels);
        return new ChiptuneEngineBuild(player, trackerModel);
    }

    public void Dispose()
    {
    }
}
