namespace MIDIRift.CleanRoom.Features.Midi.Synthesis;

public interface IWaveGenerator
{
    float Generate(float phase, ref uint noiseState);
}
