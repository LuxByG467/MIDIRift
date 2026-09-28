namespace MIDIRift.CleanRoom.Features.Midi;

public enum ChiptuneWaveType
{
    Pulse50,
    Pulse25,
    Sine,
    Triangle,
    Saw,
    BassHybrid,
    Noise
}

public static class ChiptuneWaveTypeMapper
{
    public static readonly ChiptuneWaveType[] SelectableWaveTypes =
    [
        ChiptuneWaveType.Pulse50,
        ChiptuneWaveType.Pulse25,
        ChiptuneWaveType.Sine,
        ChiptuneWaveType.Triangle,
        ChiptuneWaveType.Saw,
        ChiptuneWaveType.BassHybrid,
        ChiptuneWaveType.Noise
    ];
    public static ChiptuneWaveType FromProgram(int midiChannel, int program)
    {
        // Lyra usa canales 0..15 internamente; 9 == canal MIDI 10 (percusión).
        if (midiChannel == 9) return ChiptuneWaveType.Noise;

        program = Math.Clamp(program, 0, 127);

        // Mapa GM tonal. La intención no es imitar General MIDI de forma realista,
        // sino repartir las familias entre las ondas nativas de Lyra para conservar
        // su carácter chip-like sin hacer que Pulse50/Square sea el fallback universal.
        return program switch
        {
            >= 0 and <= 7 => ChiptuneWaveType.Pulse25,      // Piano
            >= 8 and <= 15 => ChiptuneWaveType.Triangle,    // Chromatic percussion
            >= 16 and <= 23 => ChiptuneWaveType.Sine,       // Organ
            >= 24 and <= 31 => ChiptuneWaveType.Pulse25,    // Guitar (Pulse12 compat -> P25)
            >= 32 and <= 39 => ChiptuneWaveType.BassHybrid, // Bass
            >= 40 and <= 47 => ChiptuneWaveType.Saw,        // Strings
            >= 48 and <= 55 => ChiptuneWaveType.Saw,        // Ensemble
            >= 56 and <= 63 => ChiptuneWaveType.Pulse50,    // Brass
            >= 64 and <= 71 => ChiptuneWaveType.Pulse25,    // Reed
            >= 72 and <= 79 => ChiptuneWaveType.Sine,       // Pipe
            >= 80 and <= 87 => ChiptuneWaveType.Saw,        // Synth lead
            >= 88 and <= 95 => ChiptuneWaveType.Triangle,   // Synth pad
            >= 96 and <= 103 => ChiptuneWaveType.Sine,      // Synth FX
            >= 104 and <= 111 => ChiptuneWaveType.Triangle, // Ethnic
            >= 112 and <= 119 => ChiptuneWaveType.Pulse25,  // Percussive melodic
            >= 120 and <= 127 => ChiptuneWaveType.Pulse50,  // Sound FX
            _ => ChiptuneWaveType.Pulse50
        };
    }

    public static string DisplayName(ChiptuneWaveType type) => type switch
    {
        ChiptuneWaveType.Pulse50 => "Pulse 50%",
        ChiptuneWaveType.Pulse25 => "Pulse 25%",
        ChiptuneWaveType.Sine => "Sin",
        ChiptuneWaveType.Triangle => "Triangle",
        ChiptuneWaveType.Saw => "Saw",
        ChiptuneWaveType.BassHybrid => "Bass Hybrid",
        ChiptuneWaveType.Noise => "Noise",
        _ => type.ToString()
    };
}
