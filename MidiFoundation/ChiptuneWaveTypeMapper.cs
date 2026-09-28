namespace MIDIRift;

/// <summary>
/// Política común de timbre inicial a partir del programa GM. Ambos motores
/// reciben el mismo WaveType inicial, aunque lo sinteticen de forma diferente.
/// </summary>
public static class ChiptuneWaveTypeMapper
{
    public static WaveType FromGeneralMidi(int patch, int midiChannel)
    {
        // NAudio.Midi expone Channel con numeración MIDI 1..16.
        // GM reserva el canal 10 para percusión. Tratar el 9 como percusión
        // desplazaba todos los timbres: un canal melódico terminaba en Drums
        // y el canal 10 podía caer en WhiteNoise/otro mapping por patch.
        if (midiChannel == 10)
            return WaveType.ChipDrums;

        return patch switch
        {
            >= 0 and <= 7 => WaveType.Pulse25,
            >= 8 and <= 15 => WaveType.Triangle,
            >= 16 and <= 23 => WaveType.Sine,
            >= 24 and <= 31 => WaveType.Pulse12,
            >= 32 and <= 39 => WaveType.Square,
            >= 40 and <= 47 => WaveType.Saw,
            >= 48 and <= 55 => WaveType.Saw,
            >= 56 and <= 63 => WaveType.Square,
            >= 64 and <= 71 => WaveType.Pulse25,
            >= 72 and <= 79 => WaveType.Sine,
            >= 80 and <= 87 => WaveType.Square,
            >= 88 and <= 95 => WaveType.Triangle,
            >= 96 and <= 103 => WaveType.Sine,
            >= 104 and <= 111 => WaveType.Triangle,
            >= 112 and <= 119 => WaveType.WhiteNoise,
            >= 120 and <= 127 => WaveType.Noise,
            _ => WaveType.Sine,
        };
    }
}
