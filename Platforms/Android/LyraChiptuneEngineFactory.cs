using MIDIRift.Modules;

namespace MIDIRift;

public sealed class LyraChiptuneEngineFactory : IChiptuneEngineFactory
{
    public const string ModuleId = "midirift.engine.lyra";

    public ModuleDescriptor Descriptor { get; } =
        new(ModuleId, "Lyra", "1.0.0", ModuleType.AudioEngine);

    public string EngineId => "lyra";

    public void Initialize(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
    }

    public ChiptuneEngineBuild Create(ChiptuneEngineInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Song.MusicalChannelCount <= 0)
            throw new InvalidDataException(
                "El MIDI no contiene canales musicales reproducibles.");

        var player = new LyraAudioTrackPlayer(input);
        List<Channel> trackerChannels = LegacyMidiAdapter.BuildChannels(input);
        var trackerModel = TrackerModel.FromChannels(trackerChannels);

        return new ChiptuneEngineBuild(player, trackerModel);
    }

    public void Dispose()
    {
    }
}
