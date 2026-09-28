namespace MIDIRift;

/// <summary>
/// Eventos MIDI de canal que pueden consumir los motores de síntesis.
/// No contiene tipos de NAudio: esta es la frontera portable del proyecto.
/// </summary>
public enum CompiledMidiEventKind : byte
{
    NoteOn,
    NoteOff,
    PolyAfterTouch,
    ControlChange,
    PitchWheel,
    ChannelAfterTouch,
    PatchChange,
}
