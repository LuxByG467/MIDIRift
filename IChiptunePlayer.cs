using System;
using System.Collections.Generic;

namespace MIDIRift;

public interface IChiptunePlayer : IDisposable
{
    bool IsFinished { get; }
    int CurrentRow { get; }
    float Speed { get; set; }
    long VirtualSample { get; }
    long TotalSamples { get; }

    /// <summary>
    /// El engine invoca este callback cada vez que avanza un step.
    /// TrackerPlayer se suscribe aquí en lugar de usar WaitOne,
    /// lo que es más natural en Android.Media / MAUI.
    /// </summary>
    event Action? OnStepAdvanced;

    List<WaveType> GetWaveTypes();
    void SetWaveType(int channel, WaveType wave);

    // Mezclador por canal. 1.0 = volumen original; 0.0 = silencio; 2.0 = +6 dB aprox.
    int ChannelCount { get; }
    void SetChannelGain(int channel, float gain);
    float GetChannelGain(int channel);
    float[] GetChannelGains();

    // ── Ecualizador gráfico (10 bandas, ver EqualizerBands) ────────────────
    // band = índice en EqualizerBands.All (0..EqualizerBands.Count-1).
    // gainDb se clampea internamente a [EqualizerBands.MinDb, EqualizerBands.MaxDb].
    void SetEqBand(int band, float gainDb);

    /// <summary>Ganancias actuales, una por banda — para restaurar el estado de EqualizerPage al reabrirla.</summary>
    float[] GetEqGains();

    /// <summary>Preamp global del EQ (dB, aplicado tras las 10 bandas).</summary>
    void SetPreamp(float gainDb);
    float GetPreamp();

    // ── Bass Restoration (DSP compartido, post-EQ) ───────────────────────
    void SetBassRestorationEnabled(bool enabled);
    void SetBassRestorationIntensity(float intensity);
    void SetBassRestorationFrequency(float frequencyHz);
    void SetBassRestorationMix(float mix);

    void Play();
    void Stop();
    void Reset();

    /// <summary>Salta a una posición absoluta (segundos desde el inicio). No reinicia el estado de reproducción (play/pause), solo la posición — ver ChiptuneAudioTrack.SeekTo para el detalle de cómo se reconstruye el estado interno.</summary>
    void SeekTo(float seconds);
}