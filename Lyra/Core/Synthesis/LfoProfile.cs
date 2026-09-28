namespace MIDIRift.CleanRoom.Features.Midi.Synthesis;

public readonly record struct LfoProfile(
    float RateHz,
    float DelaySeconds,
    float FadeSeconds,
    float BaseDepthSemitones,
    float WheelDepthSemitones)
{
    public static LfoProfile ForProgram(int program)
    {
        int family = Math.Clamp(program, 0, 127) / 8;
        return family switch
        {
            0 => new(5.4f, 0.18f, 0.16f, 0.00f, 0.28f), // Piano
            1 => new(5.8f, 0.12f, 0.12f, 0.00f, 0.24f), // Chromatic percussion
            2 => new(5.2f, 0.08f, 0.14f, 0.03f, 0.34f), // Organ
            3 => new(5.7f, 0.16f, 0.18f, 0.00f, 0.30f), // Guitar
            4 => new(5.0f, 0.22f, 0.20f, 0.00f, 0.20f), // Bass
            5 => new(5.5f, 0.10f, 0.28f, 0.06f, 0.42f), // Strings
            6 => new(5.4f, 0.12f, 0.26f, 0.04f, 0.38f), // Ensemble
            7 => new(5.8f, 0.09f, 0.16f, 0.02f, 0.34f), // Brass
            8 => new(5.6f, 0.08f, 0.18f, 0.03f, 0.40f), // Reed
            9 => new(5.9f, 0.06f, 0.18f, 0.04f, 0.46f), // Pipe
            10 => new(6.1f, 0.05f, 0.14f, 0.03f, 0.48f), // Synth lead
            11 => new(4.8f, 0.16f, 0.32f, 0.05f, 0.38f), // Synth pad
            12 => new(5.3f, 0.10f, 0.24f, 0.02f, 0.36f), // Synth effects
            13 => new(5.6f, 0.09f, 0.20f, 0.02f, 0.34f), // Ethnic
            14 => new(5.8f, 0.08f, 0.16f, 0.00f, 0.26f), // Percussive
            _ => new(5.2f, 0.12f, 0.20f, 0.01f, 0.30f)  // Sound effects
        };
    }
}
