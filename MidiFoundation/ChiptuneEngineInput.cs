namespace MIDIRift;

/// <summary>
/// Contrato de entrada común para cualquier motor chiptune de MIDIRift.
/// No contiene Channel ni MidiStep; esos tipos pertenecen a Legacy.
/// </summary>
public sealed class ChiptuneEngineInput
{
    public CompiledMidiSong Song { get; }
    public ChiptuneChannelSettings[] Channels { get; }

    public ChiptuneEngineInput(CompiledMidiSong song)
    {
        Song = song ?? throw new ArgumentNullException(nameof(song));

        var settings = new List<ChiptuneChannelSettings>(song.MusicalChannelCount);
        for (int i = 0; i < song.Channels.Length; i++)
        {
            CompiledMidiChannel channel = song.Channels[i];
            if (!channel.IsMusical) continue;

            settings.Add(new ChiptuneChannelSettings(
                channel.MidiChannel,
                ChiptuneWaveTypeMapper.FromGeneralMidi(
                    channel.InitialPatch,
                    channel.MidiChannel)));
        }
        Channels = settings.ToArray();
    }
}
