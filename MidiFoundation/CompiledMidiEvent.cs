namespace MIDIRift;

/// <summary>
/// Evento MIDI de canal normalizado y ordenado. Sequence conserva el orden
/// estable original cuando varios eventos comparten el mismo tick.
/// </summary>
public readonly record struct CompiledMidiEvent(
    long Tick,
    int Sequence,
    int Channel,
    CompiledMidiEventKind Kind,
    int Data1,
    int Data2);
