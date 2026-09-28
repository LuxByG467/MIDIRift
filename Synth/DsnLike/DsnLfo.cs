using System.Runtime.CompilerServices;

namespace MIDIRift.Synth.DsnLike;

internal struct DsnLfo
{
    private float _phase;
    private float _sampleHold;
    private uint _noiseState;

    public DsnLfo(uint seed)
    {
        _phase = 0f;
        _sampleHold = 0f;
        _noiseState = seed == 0 ? 0x9E3779B9u : seed;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Advance(DsnLfoWave wave, float increment)
    {
        _phase += increment;
        bool wrapped = false;
        if (_phase >= 1f)
        {
            _phase -= 1f;
            wrapped = true;
        }

        if (wave == DsnLfoWave.SampleAndHold && wrapped)
        {
            _noiseState ^= _noiseState << 13;
            _noiseState ^= _noiseState >> 17;
            _noiseState ^= _noiseState << 5;
            _sampleHold = ((_noiseState >> 8) * (1f / 8388607.5f)) - 1f;
        }

        return wave switch
        {
            DsnLfoWave.Saw => _phase + _phase - 1f,
            DsnLfoWave.Square => _phase < 0.5f ? 1f : -1f,
            DsnLfoWave.SampleAndHold => _sampleHold,
            _ => 1f - 4f * DsnFastMath.FastAbs(_phase - 0.5f),
        };
    }
}
