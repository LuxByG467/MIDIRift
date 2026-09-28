namespace MIDIRift;

/// <summary>
/// Describe un canal MIDI lógico. IsMusical sólo es true si el archivo contiene
/// al menos un NoteOn con velocidad mayor que cero para ese canal.
/// </summary>
public sealed class CompiledMidiChannel
{
    public int MidiChannel { get; }
    public int InitialPatch { get; }
    public bool IsMusical { get; }
    public CompiledMidiEvent[] Events { get; }

    public CompiledMidiChannel(
        int midiChannel,
        int initialPatch,
        bool isMusical,
        CompiledMidiEvent[] events)
    {
        MidiChannel = midiChannel;
        InitialPatch = initialPatch;
        IsMusical = isMusical;
        Events = events ?? throw new ArgumentNullException(nameof(events));
    }
}
