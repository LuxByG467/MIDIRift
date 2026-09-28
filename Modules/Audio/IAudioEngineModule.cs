namespace MIDIRift.Modules.Audio;

/// <summary>
/// Modular contract for a MIDI audio engine.
/// </summary>
public interface IAudioEngineModule : IMidiRiftModule
{
    ChiptuneEngineBuild Create(ChiptuneEngineInput input);
}
