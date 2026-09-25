using System;
using System.Runtime.CompilerServices;
using System.Threading;
using MIDIRift.CleanRoom.Features.Midi.Synthesis;

namespace MIDIRift;

/// <summary>
/// Bass Restoration ligero y reutilizable. Trabaja sobre PCM estéreo float
/// intercalado, sin asignaciones por bloque ni sincronización bloqueante.
/// La UI solo publica objetivos atómicos; el hilo de audio los lee y suaviza.
/// </summary>
public sealed class BassRestorationProcessor
{
    public const float DefaultIntensity = 0.45f;
    public const float DefaultFrequencyHz = 90f;
    public const float DefaultMix = 0.35f;

    private int _enabled;
    private int _targetIntensityBits = BitConverter.SingleToInt32Bits(DefaultIntensity);
    private int _targetFrequencyBits = BitConverter.SingleToInt32Bits(DefaultFrequencyHz);
    private int _targetMixBits = BitConverter.SingleToInt32Bits(DefaultMix);

    private float _sampleRate = 44100f;
    private float _intensity;
    private float _frequency = DefaultFrequencyHz;
    private float _mix;

    // Dos polos de entrada, envolvente, DC blocker y dos polos de salida.
    private float _inLp1, _inLp2;
    private float _envelope;
    private float _dcX1, _dcY1;
    private float _outLp1, _outLp2;

    private float _inAlpha;
    private float _outAlpha;
    private float _attackAlpha;
    private float _releaseAlpha;
    private float _dcR;
    private float _paramAlpha;

    public BassRestorationProcessor() => ConfigureSampleRate(44100);

    public bool Enabled => Volatile.Read(ref _enabled) != 0;
    public float Intensity => ReadFloat(ref _targetIntensityBits);
    public float FrequencyHz => ReadFloat(ref _targetFrequencyBits);
    public float Mix => ReadFloat(ref _targetMixBits);

    public void ConfigureSampleRate(int sampleRate)
    {
        if (sampleRate < 8000) sampleRate = 44100;
        _sampleRate = sampleRate;
        RecomputeCoefficients(_frequency);
    }

    public void SetEnabled(bool enabled) => Volatile.Write(ref _enabled, enabled ? 1 : 0);
    public void SetIntensity(float value) => WriteFloat(ref _targetIntensityBits, Math.Clamp(value, 0f, 1.5f));
    public void SetFrequency(float hz) => WriteFloat(ref _targetFrequencyBits, Math.Clamp(hz, 45f, 160f));
    public void SetMix(float value) => WriteFloat(ref _targetMixBits, Math.Clamp(value, 0f, 1f));

    public void ResetState()
    {
        _inLp1 = _inLp2 = _envelope = 0f;
        _dcX1 = _dcY1 = _outLp1 = _outLp2 = 0f;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void ProcessInterleaved(float[] buffer, int offset, int frameCount)
    {
        bool enabled = Volatile.Read(ref _enabled) != 0;
        float targetIntensity = enabled ? ReadFloat(ref _targetIntensityBits) : 0f;
        float targetMix = enabled ? ReadFloat(ref _targetMixBits) : 0f;
        float targetFrequency = ReadFloat(ref _targetFrequencyBits);

        // Bypass prácticamente gratuito cuando está establemente apagado.
        if (!enabled && _intensity < 0.00001f && _mix < 0.00001f)
            return;

        for (int n = 0; n < frameCount; n++)
        {
            _intensity += (targetIntensity - _intensity) * _paramAlpha;
            _mix += (targetMix - _mix) * _paramAlpha;
            float oldFrequency = _frequency;
            _frequency += (targetFrequency - _frequency) * _paramAlpha;
            if (MathF.Abs(_frequency - oldFrequency) > 0.02f)
                RecomputeFilterAlphas(_frequency);

            int i = offset + n * 2;
            float l = Sanitize(buffer[i]);
            float r = Sanitize(buffer[i + 1]);
            float mono = 0.5f * (l + r);

            // Aislamiento grave de dos polos.
            _inLp1 += _inAlpha * (mono - _inLp1);
            _inLp2 += _inAlpha * (_inLp1 - _inLp2);

            // Rectificación + seguidor de envolvente. En silencio converge a cero.
            float rectified = MathF.Abs(_inLp2);
            float envAlpha = rectified > _envelope ? _attackAlpha : _releaseAlpha;
            _envelope += envAlpha * (rectified - _envelope);

            // La envolvente estima la componente media de la señal rectificada.
            // Restarla conserva su energía dinámica sin dejar un offset positivo.
            float centeredRectified = rectified - _envelope;
            float dc = centeredRectified - _dcX1 + _dcR * _dcY1;
            _dcX1 = centeredRectified;
            _dcY1 = dc;

            // Suavizado final de dos polos y saturación suave solo del componente generado.
            _outLp1 += _outAlpha * (dc - _outLp1);
            _outLp2 += _outAlpha * (_outLp1 - _outLp2);
            float generated = SoftSaturate(_outLp2 * (2.4f * _intensity));
            float add = generated * _mix;

            float outL = l + add;
            float outR = r + add; // exactamente mono y centrado

            // La protección solo interviene si el efecto realmente añadió señal;
            // Mix/Intensidad en cero conservan el PCM original bit a bit.
            if (MathF.Abs(add) > 0.000001f)
            {
                if (MathF.Abs(outL) > 0.90f) outL = SoftLimit(outL);
                if (MathF.Abs(outR) > 0.90f) outR = SoftLimit(outR);
            }

            buffer[i] = Sanitize(outL);
            buffer[i + 1] = Sanitize(outR);
        }
    }

    private void RecomputeCoefficients(float frequency)
    {
        RecomputeFilterAlphas(frequency);
        _attackAlpha = OnePoleAlpha(0.010f);   // 10 ms
        _releaseAlpha = OnePoleAlpha(0.120f); // 120 ms
        _dcR = FastAudioMath.ExpNeg(6.28318530718f * 18f / _sampleRate);
        _paramAlpha = OnePoleAlpha(0.025f);    // 25 ms anti-click
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RecomputeFilterAlphas(float frequency)
    {
        _inAlpha = FrequencyAlpha(frequency);
        _outAlpha = FrequencyAlpha(Math.Clamp(frequency * 0.72f, 32f, 115f));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float FrequencyAlpha(float hz) => 1f - FastAudioMath.ExpNeg(6.28318530718f * hz / _sampleRate);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float OnePoleAlpha(float seconds) => 1f - FastAudioMath.ExpNeg(1f / (seconds * _sampleRate));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float SoftSaturate(float x) => x / (1f + MathF.Abs(x));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float SoftLimit(float x)
    {
        float sign = x < 0f ? -1f : 1f;
        float amount = MathF.Abs(x) - 0.90f;
        float drive = amount * 10f;
        float drive2 = drive * drive;
        float tanhApprox = drive * (27f + drive2) / (27f + 9f * drive2);
        tanhApprox = Math.Clamp(tanhApprox, -1f, 1f);
        return sign * (0.90f + 0.10f * tanhApprox);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Sanitize(float x) => float.IsFinite(x) ? x : 0f;

    private static float ReadFloat(ref int bits) => BitConverter.Int32BitsToSingle(Volatile.Read(ref bits));
    private static void WriteFloat(ref int bits, float value) => Volatile.Write(ref bits, BitConverter.SingleToInt32Bits(value));
}
