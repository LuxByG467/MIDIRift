namespace MIDIRift;

/// <summary>
/// Fachada de compatibilidad. Desde Foundation-1 el archivo MIDI se compila una
/// sola vez a CompiledMidiSong y Legacy consume esa representación mediante su
/// adapter. Código antiguo puede seguir llamando BuildChannels().
/// </summary>
public sealed class MidiTranslator
{
    private readonly CompiledMidiSong _compiledSong;

    public string MidiPath => _compiledSong.SourcePath;
    public CompiledMidiSong CompiledSong => _compiledSong;

    public MidiTranslator(string midiPath)
    {
        _compiledSong = MidiCompiler.Compile(midiPath);
    }

    public List<Channel> BuildChannels() =>
        LegacyMidiAdapter.BuildChannels(_compiledSong);
}
