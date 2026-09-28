#if ANDROID
using Android.Media;

namespace MIDIRift;

public sealed class AndroidAudioTrackBackend : IAudioBackend
{
    private readonly int _sampleRate;
    private readonly int _channelCount;
    private readonly int _requestedBufferFrames;
    private AudioTrack? _audioTrack;
    private bool _disposed;

    private readonly object _clockGate = new();
    private uint _clockBaseRaw;
    private uint _clockLastRaw;
    private ulong _clockWrapBase;

    public AndroidAudioTrackBackend(int sampleRate, int channelCount, int requestedBufferFrames)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (channelCount != 2)
            throw new ArgumentOutOfRangeException(nameof(channelCount), "Esta implementación espera salida estéreo.");
        if (requestedBufferFrames <= 0)
            throw new ArgumentOutOfRangeException(nameof(requestedBufferFrames));

        _sampleRate = sampleRate;
        _channelCount = channelCount;
        _requestedBufferFrames = requestedBufferFrames;
    }

    public string Name => "AudioTrack";
    public int SampleRate => _sampleRate;
    public int ChannelCount => _channelCount;
    public bool IsInitialized => _audioTrack is not null;
    public int UnderrunCount => _audioTrack?.UnderrunCount ?? 0;

    public long PresentedFrames
    {
        get
        {
            AudioTrack? track = _audioTrack;
            if (track is null)
                return 0;

            lock (_clockGate)
            {
                uint raw = unchecked((uint)track.PlaybackHeadPosition);

                // PlaybackHeadPosition es un contador uint32 de frames.
                if (raw < _clockLastRaw &&
                    _clockLastRaw - raw > 0x80000000u)
                {
                    _clockWrapBase += 1UL << 32;
                }

                _clockLastRaw = raw;
                ulong absolute = _clockWrapBase + raw;
                ulong baseAbsolute = _clockBaseRaw;
                if (absolute < baseAbsolute)
                    return 0;

                ulong delta = absolute - baseAbsolute;
                return delta > long.MaxValue
                    ? long.MaxValue
                    : (long)delta;
            }
        }
    }

    public void Initialize()
    {
        ThrowIfDisposed();
        if (_audioTrack is not null)
            return;

        int minimumBytes = AudioTrack.GetMinBufferSize(_sampleRate, ChannelOut.Stereo, Encoding.PcmFloat);
        int requestedBytes = _requestedBufferFrames * _channelCount * sizeof(float);
        int bufferBytes = Math.Max(minimumBytes, requestedBytes);

        AudioAttributes attributes = new AudioAttributes.Builder()!
            .SetUsage(AudioUsageKind.Media)!
            .SetContentType(AudioContentType.Music)!
            .Build();
        AudioFormat format = new AudioFormat.Builder()!
            .SetEncoding(Encoding.PcmFloat)!
            .SetSampleRate(_sampleRate)!
            .SetChannelMask(ChannelOut.Stereo)!
            .Build();

        _audioTrack = new AudioTrack(
            attributes,
            format,
            bufferBytes,
            AudioTrackMode.Stream,
            AudioManager.AudioSessionIdGenerate);

        ResetPresentationClock();
    }

    public void Play()
    {
        ThrowIfDisposed();
        Initialize();
        if (_audioTrack!.PlayState != PlayState.Playing)
            _audioTrack.Play();
    }

    public void Pause()
    {
        if (_disposed || _audioTrack is null)
            return;
        if (_audioTrack.PlayState == PlayState.Playing)
            _audioTrack.Pause();
    }

    public void Flush()
    {
        if (_disposed || _audioTrack is null)
            return;
        _audioTrack.Flush();
    }

    public void ResetForSeek()
    {
        if (_disposed)
            return;

        Initialize();
        Pause();
        Flush();
        ResetPresentationClock();
    }

    public void Stop()
    {
        if (_disposed || _audioTrack is null)
            return;
        if (_audioTrack.PlayState != PlayState.Stopped)
            _audioTrack.Stop();
    }

    public int Write(float[] samples, int sampleCount)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ThrowIfDisposed();
        Initialize();

        int boundedCount = Math.Clamp(sampleCount, 0, samples.Length);
        int offset = 0;
        while (offset < boundedCount)
        {
            int written = _audioTrack!.Write(samples, offset, boundedCount - offset, WriteMode.Blocking);
            if (written <= 0)
                return written;
            offset += written;
        }
        return offset;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            _audioTrack?.Stop();
        }
        catch
        {
        }
        _audioTrack?.Release();
        _audioTrack?.Dispose();
        _audioTrack = null;
    }

    private void ResetPresentationClock()
    {
        AudioTrack? track = _audioTrack;
        if (track is null)
            return;

        lock (_clockGate)
        {
            uint raw = unchecked((uint)track.PlaybackHeadPosition);
            _clockBaseRaw = raw;
            _clockLastRaw = raw;
            _clockWrapBase = 0;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AndroidAudioTrackBackend));
    }
}
#endif
