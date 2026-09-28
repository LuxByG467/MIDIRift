namespace MIDIRift;

/// <summary>
/// Ajustes de reproducción por canal, independientes del sintetizador.
/// El índice sigue el orden de canales musicales de CompiledMidiSong.
/// </summary>
public sealed class ChiptuneChannelSettings
{
    public int MidiChannel { get; }
    public WaveType WaveType { get; set; }
    public float UserGain { get; set; } = 1f;

    public ChiptuneChannelSettings(int midiChannel, WaveType waveType)
    {
        MidiChannel = midiChannel;
        WaveType = waveType;
    }
}
