namespace MIDIRift;

public enum ChiptuneEngineKind
{
    Classic = 0,
    Lyra = 1,
    DsnLike = 2,
}

/// <summary>
/// Punto único de selección del motor. Desde Foundation-4 la selección es
/// persistente y Lyra es el valor predeterminado para instalaciones nuevas.
/// </summary>
public static class ChiptuneEngineSelection
{
    public static ChiptuneEngineKind DefaultEngine
    {
        get => ChiptuneEngineSettings.Current;
        set => ChiptuneEngineSettings.Current = value;
    }
}
