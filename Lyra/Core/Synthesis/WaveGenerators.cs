using MIDIRift.CleanRoom.Features.Midi;

namespace MIDIRift.CleanRoom.Features.Midi.Synthesis;

public static class WaveGeneratorRegistry
{
    private static readonly IWaveGenerator Pulse50Instance = new PulseWaveGenerator(0.50f);
    private static readonly IWaveGenerator Pulse25Instance = new PulseWaveGenerator(0.25f);
    private static readonly IWaveGenerator SineInstance = new SineWaveGenerator();
    private static readonly IWaveGenerator TriangleInstance = new TriangleWaveGenerator();
    private static readonly IWaveGenerator SawInstance = new SawWaveGenerator();
    private static readonly IWaveGenerator BassHybridInstance = new BassHybridWaveGenerator();
    private static readonly IWaveGenerator NoiseInstance = new NoiseWaveGenerator();

    public static IWaveGenerator Resolve(ChiptuneWaveType waveType)
    {
        return waveType switch
        {
            ChiptuneWaveType.Pulse50 => Pulse50Instance,
            ChiptuneWaveType.Pulse25 => Pulse25Instance,
            ChiptuneWaveType.Sine => SineInstance,
            ChiptuneWaveType.Triangle => TriangleInstance,
            ChiptuneWaveType.Saw => SawInstance,
            ChiptuneWaveType.BassHybrid => BassHybridInstance,
            ChiptuneWaveType.Noise => NoiseInstance,
            _ => Pulse50Instance
        };
    }
}

public sealed class PulseWaveGenerator : IWaveGenerator
{
    private readonly float _dutyCycle;

    public PulseWaveGenerator(float dutyCycle)
    {
        _dutyCycle = Math.Clamp(dutyCycle, 0.01f, 0.99f);
    }

    public float Generate(float phase, ref uint noiseState)
    {
        return phase < _dutyCycle ? 1f : -1f;
    }
}

public sealed class SineWaveGenerator : IWaveGenerator
{
    public float Generate(float phase, ref uint noiseState)
    {
        return FastAudioMath.Sin01(phase);
    }
}

public sealed class TriangleWaveGenerator : IWaveGenerator
{
    public float Generate(float phase, ref uint noiseState)
    {
        return 1f - 4f * MathF.Abs(phase - 0.5f);
    }
}

public sealed class SawWaveGenerator : IWaveGenerator
{
    public float Generate(float phase, ref uint noiseState)
    {
        return 2f * phase - 1f;
    }
}

public sealed class BassHybridWaveGenerator : IWaveGenerator
{
    public float Generate(float phase, ref uint noiseState)
    {
        float sine = FastAudioMath.Sin01(phase);
        float triangle = 1f - 4f * MathF.Abs(phase - 0.5f);
        return sine * 0.65f + triangle * 0.35f;
    }
}

public sealed class NoiseWaveGenerator : IWaveGenerator
{
    public float Generate(float phase, ref uint noiseState)
    {
        uint state = noiseState;
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        noiseState = state;
        return state / (float)uint.MaxValue * 2f - 1f;
    }
}
