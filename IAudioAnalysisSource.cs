namespace MIDIRift;

/// <summary>Format/engine-neutral PCM analysis capability used by visualizers and meters.</summary>
public interface IAudioAnalysisSource
{
    int ChannelCount { get; }
    void CopyMixSamples(float[] destination);
    void CopyMeterSamples(float[] destination);
    void CopyChannelSamples(int channel, float[] destination);
}
