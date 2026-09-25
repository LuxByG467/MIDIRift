using System;

namespace MIDIRift;

/// <summary>
/// Abstracción de reproducción de MP3, análoga a IChiptunePlayer pero para
/// audio pregrabado en lugar de síntesis.
///
/// La implementación Android (Mp3AudioPlayer, en Platforms/Android) decodifica
/// el archivo a mano con MediaExtractor + MediaCodec y empuja el PCM
/// resultante a un AudioTrack — el mismo patrón "decodificar y empujar" que
/// ya usa ChiptuneAudioTrack para la síntesis. Esto (a diferencia de delegar
/// todo en Android.Media.MediaPlayer) también nos deja copiar el PCM a un
/// ring buffer antes de reproducirlo, así que Mp3AudioPlayer implementa
/// además IPanelAudioSource y SpectrumPanel/OscilloscopePanel funcionan en
/// modo MP3 sin pedir el permiso RECORD_AUDIO (que sí exige la alternativa
/// típica, android.media.audiofx.Visualizer, incluso para la propia sesión
/// de audio de la app).
///
/// Igual que con IChiptunePlayer/ChiptunePlayerFactory, la implementación
/// concreta vive en Platforms/Android y se obtiene a través de una factory
/// para que el código compartido (MainPage) no referencie tipos de Android.Media
/// directamente.
/// </summary>
public interface IMp3Player : IDisposable
{
    bool IsFinished { get; }
    bool IsPlaying { get; }

    /// <summary>Posición actual, en segundos.</summary>
    float PositionSeconds { get; }

    /// <summary>Duración total, en segundos.</summary>
    float DurationSeconds { get; }

    /// <summary>Sample rate del stream decodificado, en Hz (p. ej. 44100).</summary>
    int SampleRate { get; }

    /// <summary>
    /// Velocidad de reproducción. Requiere API 23+ (Android 6.0); en
    /// versiones anteriores el setter es un no-op silencioso.
    /// </summary>
    float Speed { get; set; }

    /// <summary>Se dispara en el hilo principal cuando termina la pista.</summary>
    event Action? OnCompleted;

    // ── Ecualizador gráfico (10 bandas, ver EqualizerBands) ────────────────
    // Mismo contrato que IChiptunePlayer — ver ese archivo.
    void SetEqBand(int band, float gainDb);
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
    void Pause();
    void Stop();

    /// <summary>Vuelve al inicio de la pista (posición 0), sin reproducir.</summary>
    void Reset();

    void SeekTo(float seconds);
}
