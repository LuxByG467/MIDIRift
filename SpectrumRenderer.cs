using NAudio.Dsp;
using System;
using System.Collections.Concurrent;

namespace MIDIRift;

/// <summary>
/// Utilidades estáticas de análisis espectral. Port 1:1 de la versión de
/// escritorio (SpectrumRenderer.cs) — esta clase ya era independiente de
/// Avalonia/UI, así que no cambia ninguna línea del pipeline DSP, solo se
/// agrega al proyecto Android.
/// Zero allocations por frame: todos los buffers son estáticos o
/// pre-alocados por el caller.
/// </summary>
public static class SpectrumRenderer
{
    // ── Constantes ────────────────────────────────────────────────────────
    public const int FftSize = 2048;
    public const int FftHalf = FftSize / 2;
    public const float SampleRate = 44100f;

    private const float FreqMin = 20f;
    private const float FreqMax = 16000f;
    private const float DbMin = -80f;
    private const float DbMax = 0f;

    // log2(FftSize) pre-calculado — NAudio lo exige como int explícito
    private static readonly int FftLog2 = (int)Math.Log2(FftSize);

    // Límites de frecuencia pre-calculados para evitar MathF.Pow en el hot path
    private static readonly float LogFreqMin = MathF.Log10(FreqMin);
    private static readonly float LogFreqRange = MathF.Log10(FreqMax) - MathF.Log10(FreqMin);

    // ── Buffers de trabajo estáticos ──────────────────────────────────────
    private static readonly Complex[] _fftBuffer = new Complex[FftSize];
    private static readonly float[] _hannWindow = BuildHannWindow(FftSize);
    private static readonly float[] _magnitudes = new float[FftHalf];
    private sealed class VisualBandMap
    {
        public readonly float[] CenterBins;
        public readonly float[] RadiusBins;

        public VisualBandMap(float[] centerBins, float[] radiusBins)
        {
            CenterBins = centerBins;
            RadiusBins = radiusBins;
        }
    }

    // Se construye una sola vez por resolución visual (241 para MIX, 24 para
    // canales). Antes estaba fijado accidentalmente a 56 bandas: cualquier
    // salida mayor terminaba leyendo el bin 0 y formaba enormes bloques.
    private static readonly ConcurrentDictionary<int, VisualBandMap> _bandMaps = new();

    // ── Ventana Hann ──────────────────────────────────────────────────────

    private static float[] BuildHannWindow(int size)
    {
        var w = new float[size];
        for (int i = 0; i < size; i++)
            w[i] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / (size - 1)));
        return w;
    }


    private static VisualBandMap BuildVisualBandMap(int bandCount)
    {
        var centers = new float[bandCount];
        var radii = new float[bandCount];
        float freqScale = SampleRate / FftSize;

        for (int i = 0; i < bandCount; i++)
        {
            // Bordes geométricos de cada banda. Se conservan como posiciones
            // fraccionales de FFT para que las bandas graves no colapsen todas
            // en el mismo índice entero.
            float t0 = (float)i / bandCount;
            float t1 = (float)(i + 1) / bandCount;
            float f0 = MathF.Pow(10f, LogFreqMin + t0 * LogFreqRange);
            float f1 = MathF.Pow(10f, LogFreqMin + t1 * LogFreqRange);

            float lo = Math.Clamp(f0 / freqScale, 0f, FftHalf - 1.001f);
            float hi = Math.Clamp(f1 / freqScale, lo, FftHalf - 1.001f);
            centers[i] = 0.5f * (lo + hi);

            // En graves, donde una banda puede ser más estrecha que un bin,
            // usamos interpolación fraccional. En medios/agudos ampliamos el
            // radio y promediamos triangularmente para mantener estabilidad.
            radii[i] = MathF.Max(0.5f, 0.5f * (hi - lo));
        }

        return new VisualBandMap(centers, radii);
    }

    private static float SampleBand(float center, float radius)
    {
        int first = Math.Max(0, (int)MathF.Floor(center - radius));
        int last = Math.Min(FftHalf - 1, (int)MathF.Ceiling(center + radius));

        float weighted = 0f;
        float weightSum = 0f;
        float invRadius = 1f / MathF.Max(radius, 0.5f);

        for (int bin = first; bin <= last; bin++)
        {
            float distance = MathF.Abs(bin - center);
            float weight = MathF.Max(0f, 1f - distance * invRadius);
            if (weight <= 0f)
                continue;

            weighted += _magnitudes[bin] * weight;
            weightSum += weight;
        }

        if (weightSum > 1e-6f)
            return weighted / weightSum;

        // Respaldo lineal para bandas extremadamente estrechas.
        int lo = Math.Clamp((int)MathF.Floor(center), 0, FftHalf - 1);
        int hi = Math.Min(lo + 1, FftHalf - 1);
        float frac = center - lo;
        return _magnitudes[lo] + (_magnitudes[hi] - _magnitudes[lo]) * frac;
    }

    // ── Pipeline principal ────────────────────────────────────────────────

    /// <summary>
    /// Transforma <paramref name="samples"/> en magnitudes espectrales normalizadas [0,1]
    /// en escala logarítmica y las escribe en <paramref name="output"/>.
    /// Aplica suavizado temporal asimétrico contra <paramref name="smoothed"/> (in/out).
    /// Zero allocations — todos los buffers deben ser pre-alocados por el caller.
    /// </summary>
    public static void ProcessRaw(float[] samples, float[] output)
    {
        int binCount = output.Length;

        // 1. Ventana Hann + carga en buffer complejo
        for (int i = 0; i < FftSize; i++)
        {
            _fftBuffer[i].X = samples[i] * _hannWindow[i];
            _fftBuffer[i].Y = 0f;
        }

        // 2. FFT in-place
        FastFourierTransform.FFT(true, FftLog2, _fftBuffer);

        // 3. Magnitudes → dB → [0,1]
        float dbRange = DbMax - DbMin;
        for (int i = 0; i < FftHalf; i++)
        {
            float re = _fftBuffer[i].X;
            float im = _fftBuffer[i].Y;
            float power = re * re + im * im;
            float db = power > 1e-20f ? 10f * MathF.Log10(power) : DbMin;
            _magnitudes[i] = Math.Clamp((db - DbMin) / dbRange, 0f, 1f);
        }

        // 4. Banco logarítmico específico para la resolución solicitada.
        //    Usa centros fraccionales y filtros triangulares para evitar que
        //    decenas de barras graves terminen leyendo exactamente el mismo bin.
        VisualBandMap map = _bandMaps.GetOrAdd(binCount, BuildVisualBandMap);
        for (int vi = 0; vi < binCount; vi++)
            output[vi] = SampleBand(map.CenterBins[vi], map.RadiusBins[vi]);

        // El suavizado temporal ya no vive en el hilo de análisis. El render
        // interpola continuamente hacia este objetivo usando deltaTime real,
        // de modo que 30 Hz de FFT pueden verse fluidos a 60/90/120 Hz.
    }

    /// <summary>Compatibilidad con callers antiguos. El suavizado se conserva
    /// aquí sólo para código que todavía no use el pipeline desacoplado.</summary>
    public static void Process(float[] samples, float[] output, float[] smoothed)
    {
        ProcessRaw(samples, output);
        const float riseAlpha = 0.90f;
        const float fallDecay = 0.78f;
        const float fallAlpha = 1f - fallDecay;
        for (int i = 0; i < output.Length; i++)
        {
            float prev = smoothed[i];
            float next = output[i];
            smoothed[i] = next >= prev
                ? prev * (1f - riseAlpha) + next * riseAlpha
                : prev * fallDecay + next * fallAlpha;
            output[i] = smoothed[i];
        }
}
}
