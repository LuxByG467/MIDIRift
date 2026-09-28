using System.Runtime.CompilerServices;

namespace MIDIRift.Synth.DsnLike;

internal struct DsnOscillator
{
    public float Phase;
    private uint _noiseState;

    public DsnOscillator(uint seed)
    {
        Phase = 0f;
        _noiseState = seed == 0 ? 0xA341316Cu : seed;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float RenderSine() => DsnFastMath.Sin01(Phase);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float RenderTriangle() => 1f - 4f * DsnFastMath.FastAbs(Phase - 0.5f);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float RenderSaw() => Phase + Phase - 1f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float RenderPulse(float pulseWidth) => Phase < pulseWidth ? 1f : -1f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float RenderNoise()
    {
        _noiseState ^= _noiseState << 13;
        _noiseState ^= _noiseState >> 17;
        _noiseState ^= _noiseState << 5;
        return ((_noiseState >> 8) * (1f / 8388607.5f)) - 1f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Render(DsnOscillatorWave wave, float pulseWidth)
    {
        switch (wave)
        {
            case DsnOscillatorWave.Sine:
                return DsnFastMath.Sin01(Phase);
            case DsnOscillatorWave.Saw:
                return Phase + Phase - 1f;

            case DsnOscillatorWave.Pulse:
                return Phase < pulseWidth ? 1f : -1f;

            case DsnOscillatorWave.Noise:
                _noiseState ^= _noiseState << 13;
                _noiseState ^= _noiseState >> 17;
                _noiseState ^= _noiseState << 5;
                return ((_noiseState >> 8) * (1f / 8388607.5f)) - 1f;

            default:
                // Triangle in [-1, 1], no transcendental operation.
                return 1f - 4f * DsnFastMath.FastAbs(Phase - 0.5f);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Advance(float increment)
    {
        Phase += increment;
        if (Phase >= 1f)
        {
            Phase -= 1f;
            return true;
        }

        if (Phase < 0f)
            Phase += 1f;

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ResetPhase() => Phase = 0f;
}
