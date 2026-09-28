using Microsoft.Maui.Storage;

namespace MIDIRift;

/// <summary>
/// Selección persistente del sintetizador chiptune.
/// Lyra es el motor predeterminado para instalaciones nuevas desde F-4.
/// </summary>
public static class ChiptuneEngineSettings
{
    private const string EngineKey = "chiptune_engine_kind";

    public static ChiptuneEngineKind Current
    {
        get
        {
            int raw = Preferences.Default.Get(
                EngineKey,
                (int)ChiptuneEngineKind.Lyra);

            return Enum.IsDefined(typeof(ChiptuneEngineKind), raw)
                ? (ChiptuneEngineKind)raw
                : ChiptuneEngineKind.Lyra;
        }
        set
        {
            if (!Enum.IsDefined(typeof(ChiptuneEngineKind), value))
                value = ChiptuneEngineKind.Lyra;

            Preferences.Default.Set(EngineKey, (int)value);
        }
    }
}
