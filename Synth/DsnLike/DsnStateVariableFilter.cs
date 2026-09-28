using System.Runtime.CompilerServices;

namespace MIDIRift.Synth.DsnLike;

/// <summary>
/// Lightweight Chamberlin-style state-variable filter. One state update yields
/// low/band/high outputs. Coefficients are updated at control-rate.
/// </summary>
internal struct DsnStateVariableFilter
{
    private float _low;
    private float _band;
    private float _f;
    private float _damping;

    public void Set(float cutoffHz, float resonance, float sampleRate)
    {
        // Conservative ceiling keeps this cheap SVF stable.
        float cutoff = DsnFastMath.Clamp(cutoffHz, 20f, sampleRate * 0.22f);
        float x = MathF.PI * cutoff / sampleRate; // <= ~0.69 rad
        _f = 2f * DsnFastMath.SinHalfPi(x);
        _f = DsnFastMath.Clamp(_f, 0.001f, 0.90f);

        float r = DsnFastMath.Clamp(resonance, 0f, 0.98f);
        _damping = 1.8f - 1.65f * r;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Process(float input, DsnFilterMode mode)
    {
        float high = input - _low - _damping * _band;
        _band += _f * high;
        _low += _f * _band;

        return mode switch
        {
            DsnFilterMode.HighPass => high,
            DsnFilterMode.BandPass => _band,
            _ => _low,
        };
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float ProcessLowPass(float input)
    {
        float high = input - _low - _damping * _band;
        _band += _f * high;
        _low += _f * _band;
        return _low;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float ProcessBandPass(float input)
    {
        float high = input - _low - _damping * _band;
        _band += _f * high;
        _low += _f * _band;
        return _band;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float ProcessHighPass(float input)
    {
        float high = input - _low - _damping * _band;
        _band += _f * high;
        _low += _f * _band;
        return high;
    }

    public void Reset()
    {
        _low = 0f;
        _band = 0f;
    }
}
