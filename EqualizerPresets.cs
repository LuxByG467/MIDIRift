namespace MIDIRift;

/// <summary>
/// Curvas de fábrica para las 10 bandas de <see cref="EqualizerBands"/>
/// (31, 62, 125, 250, 500, 1k, 2k, 4k, 8k, 16k Hz), en dB. Son valores
/// fijos en memoria — a diferencia de los presets personalizados
/// (<see cref="EqPresetStore"/>) no se guardan ni se pueden borrar desde
/// la UI.
/// </summary>
public static class EqualizerPresets
{
    public static readonly EqPreset[] Factory =
    {
        new("Flat",         new float[] {  0,  0,  0,  0,  0,  0,  0,  0,  0,  0 }),
        new("Bass Boost",   new float[] {  7,  6,  4,  2,  0, -1, -1,  0,  0,  0 }),
        new("Bass Reducer", new float[] { -7, -6, -4, -2,  0,  0,  0,  0,  0,  0 }),
        new("Treble Boost", new float[] {  0,  0,  0,  0,  0,  0,  2,  4,  6,  7 }),
        new("Rock",         new float[] {  5,  3, -2, -4, -2,  1,  3,  5,  5,  5 }),
        new("Pop",          new float[] { -2, -1,  0,  2,  4,  4,  2,  0, -1, -2 }),
        new("Jazz",         new float[] {  3,  2,  1,  2, -1, -1,  0,  1,  2,  3 }),
        new("Classical",    new float[] {  4,  3,  2,  0,  0,  0, -2, -2, -2,  3 }),
        new("Vocal",        new float[] { -3, -2, -1,  1,  4,  4,  3,  1, -1, -2 }),
        new("Electronic",   new float[] {  5,  4,  1,  0, -2,  2,  0,  1,  4,  6 }),
    };
}
