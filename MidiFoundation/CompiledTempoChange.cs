namespace MIDIRift;

/// <summary>Cambio global de tempo expresado en microsegundos por negra.</summary>
public readonly record struct CompiledTempoChange(
    long Tick,
    int MicrosecondsPerQuarterNote);
