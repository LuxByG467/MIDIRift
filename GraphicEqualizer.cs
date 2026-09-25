using MIDIRift.CleanRoom.Features.Midi.Synthesis;
using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace MIDIRift;

/// <summary>
/// Ecualizador gráfico de 10 bandas optimizado para el hot path de audio.
///
/// Las preferencias se escriben desde UI, pero los coeficientes y estados DSP
/// sólo se modifican desde el hilo que llama Process*. Esto evita carreras entre
/// sliders y render. Únicamente se recorren las bandas con ganancia distinta de
/// 0 dB; el preamp se aplica en una pasada SIMD independiente.
/// </summary>
public sealed class GraphicEqualizer
{
    private const int BandCount = EqualizerBands.Count;
    private const float DefaultSampleRate = 44100f;
    private const float MinimumSampleRate = 8000f;
    private const float Q = 1.2f;
    private const float ActiveEpsilonDb = 0.001f;

    private struct BiquadCoeffs
    {
        public float B0, B1, B2, A1, A2;
    }

    private float _sampleRate = DefaultSampleRate;

    // Valores solicitados por UI. Un float se escribe atómicamente; la versión
    // publicada con Interlocked actúa como barrera antes de que el hilo DSP lea.
    private readonly float[] _requestedGainsDb = new float[BandCount];
    private float _requestedPreampDb;
    private int _requestedVersion;

    // Estado propiedad exclusiva del hilo de audio.
    private readonly float[] _appliedGainsDb = new float[BandCount];
    private float _appliedPreampDb;
    private float _preampLinear = 1f;
    private int _appliedVersion = -1;

    private readonly BiquadCoeffs[] _coeffs = new BiquadCoeffs[BandCount];
    private readonly int[] _activeBands = new int[BandCount];
    private int _activeBandCount;

    // Forma directa II transpuesta: dos estados por canal/banda, menos accesos
    // de memoria y multiplicaciones que la forma directa I anterior.
    private readonly float[] _z1L = new float[BandCount];
    private readonly float[] _z2L = new float[BandCount];
    private readonly float[] _z1R = new float[BandCount];
    private readonly float[] _z2R = new float[BandCount];

    public GraphicEqualizer()
    {
        RebuildAppliedConfiguration(forceReset: true);
    }

    public int SampleRate => (int)_sampleRate;

    public void ConfigureSampleRate(int sampleRate)
    {
        float validated = sampleRate >= MinimumSampleRate && sampleRate <= 384000
            ? sampleRate
            : DefaultSampleRate;

        if (MathF.Abs(_sampleRate - validated) < 0.5f) return;
        _sampleRate = validated;
        // Normalmente se llama antes de arrancar el stream. Marcar pendiente
        // conserva la regla de que el estado DSP cambia en frontera de bloque.
        Interlocked.Increment(ref _requestedVersion);
    }

    public float GetGain(int band) => (uint)band < BandCount
        ? Volatile.Read(ref _requestedGainsDb[band])
        : 0f;

    public float[] GetGains()
    {
        var result = new float[BandCount];
        for (int i = 0; i < BandCount; i++)
            result[i] = Volatile.Read(ref _requestedGainsDb[i]);
        return result;
    }

    public float GetPreamp() => Volatile.Read(ref _requestedPreampDb);

    public void SetPreampDb(float gainDb)
    {
        gainDb = Math.Clamp(gainDb, EqualizerBands.MinDb, EqualizerBands.MaxDb);
        if (MathF.Abs(Volatile.Read(ref _requestedPreampDb) - gainDb) < ActiveEpsilonDb)
            return;

        Volatile.Write(ref _requestedPreampDb, gainDb);
        Interlocked.Increment(ref _requestedVersion);
    }

    public void SetBandGain(int band, float gainDb)
    {
        if ((uint)band >= BandCount) return;
        gainDb = Math.Clamp(gainDb, EqualizerBands.MinDb, EqualizerBands.MaxDb);
        if (MathF.Abs(Volatile.Read(ref _requestedGainsDb[band]) - gainDb) < ActiveEpsilonDb)
            return;

        Volatile.Write(ref _requestedGainsDb[band], gainDb);
        Interlocked.Increment(ref _requestedVersion);
    }

    public void SetAllGains(ReadOnlySpan<float> gainsDb)
    {
        int n = Math.Min(BandCount, gainsDb.Length);
        bool changed = false;
        for (int i = 0; i < n; i++)
        {
            float value = Math.Clamp(gainsDb[i], EqualizerBands.MinDb, EqualizerBands.MaxDb);
            if (MathF.Abs(Volatile.Read(ref _requestedGainsDb[i]) - value) < ActiveEpsilonDb)
                continue;
            Volatile.Write(ref _requestedGainsDb[i], value);
            changed = true;
        }

        if (changed) Interlocked.Increment(ref _requestedVersion);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ApplyPendingConfiguration()
    {
        int requested = Volatile.Read(ref _requestedVersion);
        if (requested == _appliedVersion) return;

        RebuildAppliedConfiguration(forceReset: false);
        _appliedVersion = requested;
    }

    private void RebuildAppliedConfiguration(bool forceReset)
    {
        _activeBandCount = 0;

        for (int band = 0; band < BandCount; band++)
        {
            float gain = Volatile.Read(ref _requestedGainsDb[band]);
            bool coefficientChanged = forceReset || MathF.Abs(gain - _appliedGainsDb[band]) >= ActiveEpsilonDb;
            _appliedGainsDb[band] = gain;

            if (coefficientChanged)
            {
                RecomputeCoeffs(band, gain);
                // Un historial perteneciente a otros coeficientes puede causar
                // picos enormes. Reiniciar sólo esta banda es barato y estable.
                _z1L[band] = _z2L[band] = 0f;
                _z1R[band] = _z2R[band] = 0f;
            }

            if (MathF.Abs(gain) >= ActiveEpsilonDb)
                _activeBands[_activeBandCount++] = band;
        }

        float preamp = Volatile.Read(ref _requestedPreampDb);
        _appliedPreampDb = preamp;
        _preampLinear = MathF.Abs(preamp) < ActiveEpsilonDb
            ? 1f
            : FastAudioMath.Exp2((preamp / 20f) * 3.32192809489f);
    }

    private void RecomputeCoeffs(int band, float gainDb)
    {
        float freq = EqualizerBands.All[band].FreqHz;
        float nyquist = _sampleRate * 0.5f;
        if (freq >= nyquist * 0.98f || MathF.Abs(gainDb) < ActiveEpsilonDb)
        {
            _coeffs[band] = new BiquadCoeffs { B0 = 1f };
            return;
        }

        float a = FastAudioMath.Exp2((gainDb / 40f) * 3.32192809489f);
        float w0 = 2f * MathF.PI * freq / _sampleRate;
        float phase01 = w0 * 0.15915494309f;
        float sinW0 = FastAudioMath.Sin01(phase01);
        float cosW0 = FastAudioMath.Sin01(phase01 + 0.25f);
        float alpha = sinW0 / (2f * Q);

        float b0 = 1f + alpha * a;
        float b1 = -2f * cosW0;
        float b2 = 1f - alpha * a;
        float a0 = 1f + alpha / a;
        float a1 = -2f * cosW0;
        float a2 = 1f - alpha / a;
        float invA0 = 1f / a0;

        _coeffs[band] = new BiquadCoeffs
        {
            B0 = b0 * invA0,
            B1 = b1 * invA0,
            B2 = b2 * invA0,
            A1 = a1 * invA0,
            A2 = a2 * invA0,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void ProcessBlock(float[] left, float[] right, int count)
    {
        ApplyPendingConfiguration();
        count = Math.Min(count, Math.Min(left.Length, right.Length));
        if (count <= 0) return;

        for (int active = 0; active < _activeBandCount; active++)
        {
            int band = _activeBands[active];
            BiquadCoeffs c = _coeffs[band];
            float z1L = _z1L[band], z2L = _z2L[band];
            float z1R = _z1R[band], z2R = _z2R[band];

            for (int n = 0; n < count; n++)
            {
                float xL = left[n];
                float yL = c.B0 * xL + z1L;
                z1L = c.B1 * xL - c.A1 * yL + z2L;
                z2L = c.B2 * xL - c.A2 * yL;
                left[n] = yL;

                float xR = right[n];
                float yR = c.B0 * xR + z1R;
                z1R = c.B1 * xR - c.A1 * yR + z2R;
                z2R = c.B2 * xR - c.A2 * yR;
                right[n] = yR;
            }

            _z1L[band] = z1L; _z2L[band] = z2L;
            _z1R[band] = z1R; _z2R[band] = z2R;
        }

        ApplyPreampPlanar(left, right, count);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void ProcessInterleaved(float[] buffer, int offset, int frameCount)
    {
        ApplyPendingConfiguration();
        if (frameCount <= 0 || offset < 0 || offset >= buffer.Length) return;
        frameCount = Math.Min(frameCount, (buffer.Length - offset) / 2);

        for (int active = 0; active < _activeBandCount; active++)
        {
            int band = _activeBands[active];
            BiquadCoeffs c = _coeffs[band];
            float z1L = _z1L[band], z2L = _z2L[band];
            float z1R = _z1R[band], z2R = _z2R[band];
            int end = offset + frameCount * 2;

            for (int idx = offset; idx < end; idx += 2)
            {
                float xL = buffer[idx];
                float yL = c.B0 * xL + z1L;
                z1L = c.B1 * xL - c.A1 * yL + z2L;
                z2L = c.B2 * xL - c.A2 * yL;
                buffer[idx] = yL;

                float xR = buffer[idx + 1];
                float yR = c.B0 * xR + z1R;
                z1R = c.B1 * xR - c.A1 * yR + z2R;
                z2R = c.B2 * xR - c.A2 * yR;
                buffer[idx + 1] = yR;
            }

            _z1L[band] = z1L; _z2L[band] = z2L;
            _z1R[band] = z1R; _z2R[band] = z2R;
        }

        ApplyPreampInterleaved(buffer, offset, frameCount * 2);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ApplyPreampPlanar(float[] left, float[] right, int count)
    {
        float gain = _preampLinear;
        if (gain == 1f) return;

        int width = Vector<float>.Count;
        int n = 0;
        if (Vector.IsHardwareAccelerated && count >= width)
        {
            var gainVector = new Vector<float>(gain);
            int vectorEnd = count - (count % width);
            for (; n < vectorEnd; n += width)
            {
                (new Vector<float>(left, n) * gainVector).CopyTo(left, n);
                (new Vector<float>(right, n) * gainVector).CopyTo(right, n);
            }
        }
        for (; n < count; n++) { left[n] *= gain; right[n] *= gain; }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ApplyPreampInterleaved(float[] buffer, int offset, int sampleCount)
    {
        float gain = _preampLinear;
        if (gain == 1f) return;

        int end = offset + sampleCount;
        int width = Vector<float>.Count;
        int i = offset;
        if (Vector.IsHardwareAccelerated && sampleCount >= width)
        {
            var gainVector = new Vector<float>(gain);
            int vectorEnd = end - ((end - i) % width);
            for (; i < vectorEnd; i += width)
                (new Vector<float>(buffer, i) * gainVector).CopyTo(buffer, i);
        }
        for (; i < end; i++) buffer[i] *= gain;
    }
}
