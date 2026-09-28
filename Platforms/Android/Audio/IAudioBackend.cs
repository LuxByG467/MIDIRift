namespace MIDIRift;

public interface IAudioBackend : IDisposable
{
    string Name { get; }
    int SampleRate { get; }
    int ChannelCount { get; }
    bool IsInitialized { get; }
    int UnderrunCount { get; }

    /// <summary>
    /// Frames que el backend ya presentó/consumió desde el último ResetForSeek.
    /// No significa "frames escritos". Es el reloj de presentación que debe
    /// gobernar tracker, FFT y osciloscopio.
    /// </summary>
    long PresentedFrames { get; }

    void Initialize();
    void Play();
    void Pause();
    void Flush();
    void ResetForSeek();
    void Stop();
    int Write(float[] samples, int sampleCount);
}
