#if ANDROID
using System.Runtime.InteropServices;

namespace MIDIRift;

/// <summary>
/// AAudio backend backed by libmidirift_rt_audio.so.
/// The AAudio data callback and SPSC FIFO live entirely in native C++.
/// No real-time AAudio callback ever crosses into the .NET runtime.
/// </summary>
public sealed unsafe class AndroidAAudioBackend : IAudioBackend
{
    private const int AAudioOk = 0;
    private readonly object _gate = new();
    private readonly int _sampleRate;
    private readonly int _channelCount;
    private readonly int _requestedBufferFrames;

    private IntPtr _handle;
    private bool _disposed;

    public AndroidAAudioBackend(
        int sampleRate,
        int channelCount,
        int requestedBufferFrames)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (channelCount != 2)
            throw new ArgumentOutOfRangeException(
                nameof(channelCount),
                "La ruta RT nativa espera salida estéreo.");
        if (requestedBufferFrames <= 0)
            throw new ArgumentOutOfRangeException(nameof(requestedBufferFrames));

        _sampleRate = sampleRate;
        _channelCount = channelCount;
        _requestedBufferFrames = requestedBufferFrames;
    }

    public string Name => "AAudio Native RT";
    public int SampleRate => _sampleRate;
    public int ChannelCount => _channelCount;

    public bool IsInitialized
    {
        get
        {
            lock (_gate)
                return _handle != IntPtr.Zero;
        }
    }

    public int UnderrunCount
    {
        get
        {
            IntPtr handle = Volatile.Read(ref _handle);
            return handle == IntPtr.Zero ? 0 : Math.Max(0, Native.GetXruns(handle));
        }
    }

    public long PresentedFrames
    {
        get
        {
            IntPtr handle = Volatile.Read(ref _handle);
            return handle == IntPtr.Zero
                ? 0L
                : Math.Max(0L, Native.GetPresentedFrames(handle));
        }
    }

    public int BufferedFrames
    {
        get
        {
            IntPtr handle = Volatile.Read(ref _handle);
            return handle == IntPtr.Zero ? 0 : Math.Max(0, Native.GetBufferedFrames(handle));
        }
    }

    public int FramesPerBurst
    {
        get
        {
            IntPtr handle = Volatile.Read(ref _handle);
            return handle == IntPtr.Zero ? 0 : Math.Max(0, Native.GetFramesPerBurst(handle));
        }
    }

    public int AdaptiveBufferFrames
    {
        get
        {
            IntPtr handle = Volatile.Read(ref _handle);
            return handle == IntPtr.Zero ? 0 : Math.Max(0, Native.GetBufferFrames(handle));
        }
    }

    public int AdaptiveBufferBursts
    {
        get
        {
            int burst = FramesPerBurst;
            int frames = AdaptiveBufferFrames;
            return burst <= 0 ? 0 : (frames + burst - 1) / burst;
        }
    }

    public int AdaptiveIncreaseCount
    {
        get
        {
            IntPtr handle = Volatile.Read(ref _handle);
            return handle == IntPtr.Zero
                ? 0
                : Math.Max(0, Native.GetAdaptiveIncreaseCount(handle));
        }
    }

    public static bool IsSupported =>
        global::Android.OS.Build.VERSION.SdkInt >=
        global::Android.OS.BuildVersionCodes.O;

    public void Initialize()
    {
        lock (_gate)
        {
            ThrowIfDisposedLocked();
            InitializeLocked();
        }
    }

    public void Play()
    {
        lock (_gate)
        {
            ThrowIfDisposedLocked();
            InitializeLocked();
            CheckResult(Native.Start(_handle), "iniciar stream nativo");
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (_disposed || _handle == IntPtr.Zero)
                return;

            int result = Native.Pause(_handle);
            if (result != AAudioOk)
                CheckResult(result, "pausar stream nativo");
        }
    }

    public void Flush()
    {
        lock (_gate)
        {
            if (_disposed || _handle == IntPtr.Zero)
                return;

            int result = Native.Flush(_handle);
            if (result != AAudioOk)
                CheckResult(result, "vaciar stream nativo");
        }
    }

    public void ResetForSeek()
    {
        lock (_gate)
        {
            ThrowIfDisposedLocked();
            InitializeLocked();

            CheckResult(
                Native.ResetForSeek(_handle),
                "recrear stream nativo después de seek");

            Console.WriteLine(
                "[MIDIRift.Audio] AAudio Native RT recreado después de seek.");
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_disposed || _handle == IntPtr.Zero)
                return;

            int result = Native.Stop(_handle);
            if (result != AAudioOk)
                CheckResult(result, "detener stream nativo");
        }
    }

    public int Write(float[] samples, int sampleCount)
    {
        ArgumentNullException.ThrowIfNull(samples);

        IntPtr handle = Volatile.Read(ref _handle);
        if (handle == IntPtr.Zero || _disposed)
            return 0;

        int bounded = Math.Clamp(sampleCount, 0, samples.Length);
        bounded -= bounded % _channelCount;
        if (bounded == 0)
            return 0;

        fixed (float* pointer = samples)
        {
            int written = Native.Write(handle, pointer, bounded);
            return written < 0 ? written : Math.Min(written, bounded);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            IntPtr handle = _handle;
            _handle = IntPtr.Zero;

            if (handle != IntPtr.Zero)
                Native.Destroy(handle);
        }
    }

    private void InitializeLocked()
    {
        if (_handle != IntPtr.Zero)
            return;

        if (!IsSupported)
            throw new PlatformNotSupportedException(
                "AAudio requiere Android 8.0 (API 26) o posterior.");

        IntPtr handle = Native.Create(
            _sampleRate,
            _channelCount,
            _requestedBufferFrames);

        if (handle == IntPtr.Zero)
            throw new InvalidOperationException(
                "libmidirift_rt_audio no pudo crear el stream AAudio.");

        _handle = handle;

        Console.WriteLine(
            $"[MIDIRift.Audio] AAudio Native RT inicializado: " +
            $"rate={Native.GetSampleRate(handle)} channels={Native.GetChannelCount(handle)} " +
            $"burst={FramesPerBurst} buffer={AdaptiveBufferFrames} " +
            $"bursts={AdaptiveBufferBursts}.");
    }

    private void ThrowIfDisposedLocked()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AndroidAAudioBackend));
    }

    private static void CheckResult(int result, string operation)
    {
        if (result == AAudioOk)
            return;

        throw new InvalidOperationException(
            $"AAudio Native RT no pudo {operation}: {result}.");
    }

    private static class Native
    {
        private const string Library = "midirift_rt_audio";

        [DllImport(Library, EntryPoint = "midirift_rt_create")]
        internal static extern IntPtr Create(
            int sampleRate,
            int channelCount,
            int requestedBufferFrames);

        [DllImport(Library, EntryPoint = "midirift_rt_start")]
        internal static extern int Start(IntPtr handle);

        [DllImport(Library, EntryPoint = "midirift_rt_pause")]
        internal static extern int Pause(IntPtr handle);

        [DllImport(Library, EntryPoint = "midirift_rt_flush")]
        internal static extern int Flush(IntPtr handle);

        [DllImport(Library, EntryPoint = "midirift_rt_reset_for_seek")]
        internal static extern int ResetForSeek(IntPtr handle);

        [DllImport(Library, EntryPoint = "midirift_rt_stop")]
        internal static extern int Stop(IntPtr handle);

        [DllImport(Library, EntryPoint = "midirift_rt_write")]
        internal static extern int Write(
            IntPtr handle,
            float* samples,
            int sampleCount);

        [DllImport(Library, EntryPoint = "midirift_rt_get_xruns")]
        internal static extern int GetXruns(IntPtr handle);

        [DllImport(Library, EntryPoint = "midirift_rt_get_buffered_frames")]
        internal static extern int GetBufferedFrames(IntPtr handle);

        [DllImport(Library, EntryPoint = "midirift_rt_get_presented_frames")]
        internal static extern long GetPresentedFrames(IntPtr handle);

        [DllImport(Library, EntryPoint = "midirift_rt_get_frames_per_burst")]
        internal static extern int GetFramesPerBurst(IntPtr handle);

        [DllImport(Library, EntryPoint = "midirift_rt_get_buffer_frames")]
        internal static extern int GetBufferFrames(IntPtr handle);

        [DllImport(Library, EntryPoint = "midirift_rt_get_adaptive_increase_count")]
        internal static extern int GetAdaptiveIncreaseCount(IntPtr handle);

        [DllImport(Library, EntryPoint = "midirift_rt_get_sample_rate")]
        internal static extern int GetSampleRate(IntPtr handle);

        [DllImport(Library, EntryPoint = "midirift_rt_get_channel_count")]
        internal static extern int GetChannelCount(IntPtr handle);

        [DllImport(Library, EntryPoint = "midirift_rt_destroy")]
        internal static extern void Destroy(IntPtr handle);
    }
}
#endif
