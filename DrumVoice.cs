namespace MIDIRift;

/// <summary>
/// Voz one-shot de percusión sintética. Combina una capa tonal con barrido
/// de pitch y una capa de ruido, sin samples ni allocations durante playback.
/// </summary>
public struct DrumVoice
{
    public bool Active;
    public double Phase;
    public double Frequency;
    public double PitchMultiplier;
    public int PitchSamplesLeft;

    public float TonalLevel;
    public float TonalDecay;
    public float NoiseLevel;
    public float NoiseDecay;
    public float Velocity;
    public float Duty;

    public bool WhiteNoise;
    public uint Lfsr;
    public double NoiseTimer;
    public double NoisePeriod;
    public float NoiseOut;
}
