namespace MIDIRift
{
    public enum WaveType
    {
        Square,
        Triangle,
        Saw,
        Sine,
        Pulse25,
        Pulse12,
        Noise,      // DMG LFSR noise
        WhiteNoise, // random white noise
        ChipDrums   // percusión multicapa tonal + ruido
    }
}
