namespace MIDIRift;

/// <summary>
/// Metadato preservado para UI/diagnóstico futuro. Los motores no tienen que
/// mostrarlo ni procesarlo como audio.
/// </summary>
public readonly record struct CompiledMidiMetaEvent(
    long Tick,
    string Type,
    string Text);
