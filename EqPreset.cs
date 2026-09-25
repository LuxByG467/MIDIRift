namespace MIDIRift;

/// <summary>
/// Un preset de ecualización: nombre + 10 ganancias en dB (mismo orden
/// que <see cref="EqualizerBands.All"/>). Usado tanto para los presets
/// de fábrica (<see cref="EqualizerPresets"/>, en memoria, no editables)
/// como para los personalizados (<see cref="EqPresetStore"/>, persistidos
/// en JSON). Es el tipo que serializa EqPresetStore, así que sus
/// propiedades deben tener setters públicos para System.Text.Json.
/// </summary>
public class EqPreset
{
    public string Name { get; set; } = "";
    public float[] GainsDb { get; set; } = new float[EqualizerBands.Count];

    /// <summary>Preamp guardado junto con las 10 bandas (dB). Default 0 — los presets de fábrica no lo usan.</summary>
    public float PreampDb { get; set; } = 0f;

    public EqPreset() { }

    public EqPreset(string name, float[] gainsDb)
    {
        Name = name;
        GainsDb = gainsDb;
    }

    public EqPreset(string name, float[] gainsDb, float preampDb)
    {
        Name = name;
        GainsDb = gainsDb;
        PreampDb = preampDb;
    }
}
