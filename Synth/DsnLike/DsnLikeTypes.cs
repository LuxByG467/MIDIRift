namespace MIDIRift.Synth.DsnLike;

public enum DsnOscillatorWave
{
    Sine,
    Triangle,
    Saw,
    Pulse,
    Noise
}

public enum DsnFilterMode
{
    LowPass,
    BandPass,
    HighPass
}

public enum DsnLfoWave
{
    Triangle,
    Saw,
    Square,
    SampleAndHold
}

public enum DsnEnvelopeStage
{
    Off,
    Attack,
    Decay,
    Sustain,
    Release
}
