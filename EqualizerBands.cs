namespace MIDIRift;

/// <summary>
/// Definición de las 10 bandas del ecualizador gráfico (frecuencia central
/// ISO + rango de ganancia en dB) y nada más.
///
/// Deliberadamente en su propio archivo, sin usings de Avalonia ni de
/// NAudio: es el único punto de verdad que <see cref="EqualizerPanel"/>
/// (capa de UI) y los motores de audio (<see cref="ChiptuneNAudio"/>,
/// <see cref="Mp3AudioSource"/>, vía <see cref="IAudioSource"/>) comparten.
/// Si el índice de banda fuera solo una constante duplicada en cada lado
/// (UI con 10, motor con 10 "porque sí"), un día se desincroniza. Con esto,
/// agregar/quitar una banda es un cambio en un solo lugar y ambos lados lo
/// heredan automáticamente.
///
/// Ninguno de los dos lados referencia al otro: la UI no conoce
/// ChiptuneNAudio/Mp3AudioSource, y los motores no conocen EqualizerPanel.
/// El único acoplamiento pasa por <see cref="IAudioSource.SetEqBand"/>,
/// que MainWindow conecta al evento OnBandGainChanged del panel.
/// </summary>
public static class EqualizerBands
{
    public static readonly (string Label, float FreqHz)[] All =
    {
        ("31",  31f),  ("62",  62f),  ("125", 125f), ("250", 250f), ("500", 500f),
        ("1k",  1000f),("2k",  2000f),("4k",  4000f),("8k",  8000f),("16k", 16000f),
    };

    public const int Count = 10;
    public const float MinDb = -12f;
    public const float MaxDb = 12f;
}
