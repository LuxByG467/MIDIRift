#if ANDROID
using MIDIRift.Synth.DsnLike;
using System.Diagnostics;
using LyraCompiledEvent =
    MIDIRift.CleanRoom.Features.Midi.CompiledMidiEvent;
using LyraEventKind =
    MIDIRift.CleanRoom.Features.Midi.CompiledMidiEventKind;
using LyraChannelState =
    MIDIRift.CleanRoom.Features.Midi.Synthesis.ChannelState;
using LyraChannelStateSnapshot =
    MIDIRift.CleanRoom.Features.Midi.Synthesis.ChannelStateSnapshot;

namespace MIDIRift;

/// <summary>
/// Foundation-3 transport adapter for Lyra.
///
/// Responsibilities:
/// - Implements the exact same IChiptunePlayer contract used by Classic.
/// - Uses the Foundation-2 Lyra core for synthesis.
/// - Uses Android AudioTrack only as an output backend in this phase.
/// - Exposes the same panel capture API as Classic.
/// - Keeps EQ and Bass Restoration behind the same public controls.
///
/// It deliberately does NOT parse MIDI. Its only source is ChiptuneEngineInput.
/// </summary>
public sealed class DsnLikeAudioTrackPlayer : IChiptunePlayer, IPanelAudioSource
{
    private const int SampleRate = 44_100;
    private const int OutputChannels = 2;
    private const int MaxVoices = 96;
    // ~3 s @ 44.1 kHz. Necesitamos conservar suficiente historia para
    // leer alrededor del reloj de presentación aunque el sintetizador vaya
    // adelantado. 2^17 sigue siendo razonable incluso con varios canales.
    private const int CaptureLength = 1 << 17;
    private const long ChannelCaptureKeepAliveMs = 1000;

    private readonly object _stateLock = new();
    private readonly LyraCoreSession _core;
    // DSN-like owns audible AND logical voice state. Lyra Core is retained only
    // for the compiled MIDI scheduler/channel transport model; its VoicePool is
    // deliberately not mirrored anymore. One synth per musical channel.
    private readonly DsnLikeSynth[] _dsnChannels;
    private readonly DsnProgramBank _dsnProgramBank;
    private DsnDrumKitSettings _dsnDrumSettings;
    private readonly DsnDrumKit[] _dsnDrums;
    private readonly float[][] _dsnScratch;

    // 0.1.8 real-world render telemetry. Lock-free counters are intentionally
    // cheap enough to leave enabled while stress-testing actual MIDI playback.
    private long _telemetryBlocks, _telemetryLateBlocks, _telemetryRenderTicks, _telemetryMaxTicks;
    private int _telemetryPeakVoices;
    private long _telemetryLastLogMs;
    private readonly WaveType[] _commonWaveTypes;
    private readonly WaveType?[] _waveTypeOverrides;

    private readonly int _blockFrames;
    private readonly int _ringBlockCount;
    private readonly int _audioTrackBufferMultiplier;

    private readonly float[] _leftMix;
    private readonly float[] _rightMix;
    private readonly float[] _renderInterleaved;
    private readonly float[][] _channelBlockCapture;
    private readonly bool[] _captureChannelFlags;

    private readonly float[] _mixCapture = new float[CaptureLength];
    private readonly float[] _meterCapture = new float[CaptureLength];
    private readonly float[][] _channelCapture;
    private long _mixCaptureFramesWritten;
    private long _meterCaptureFramesWritten;
    private long _channelCaptureFramesWritten;
    private long _lastMixCaptureRequestMs;
    private long _lastChannelCaptureRequestMs;

    private readonly GraphicEqualizer _eq = new();
    private readonly BassRestorationProcessor _bassRestoration = new();

    private IAudioBackend? _audioBackend;
    private string _activeBackendName = "";
    private Thread? _producerThread;
    private Thread? _writerThread;


    private float[][] _pcmRing = Array.Empty<float[]>();
    private int[] _ringFrameCounts = Array.Empty<int>();
    private long[] _ringStartSamples = Array.Empty<long>();
    private long[] _ringEndSamples = Array.Empty<long>();
    private long[] _ringOutputStartFrames = Array.Empty<long>();
    private bool[] _ringEndFlags = Array.Empty<bool>();
    private SemaphoreSlim? _freeBlocks;
    private SemaphoreSlim? _readyBlocks;
    private int _produceIndex;
    private int _consumeIndex;

    private volatile bool _running;
    private volatile bool _disposed;
    private volatile bool _producerFinished;
    private volatile bool _isFinished;

    private long _renderSampleCursor;
    // Posición musical fraccional. A diferencia del cursor de salida, avanza
    // Speed muestras MIDI por cada frame de audio generado.
    private double _timelineSampleCursor;
    private long _renderOutputFrameCursor;

    // Sample absoluto de la canción que corresponde al frame 0 del epoch
    // actual del backend. Después de seek vale el target; después de reset, 0.
    private long _presentationBaseSample;
    private long _presentationBaseFrame;

    // Fallback/telemetría: frames aceptados por el backend. Ya NO gobierna UI.
    private long _writtenSampleCursor;

    // EOF pendiente: no declaramos Finished al escribir el último bloque,
    // sino cuando el backend realmente lo ha consumido/presentado.
    private long _pendingEndSample = -1;

    // Algunos MIDIs terminan sin NoteOff/AllNotesOff final. Classic ya libera
    // esas voces al agotarse el timeline; Lyra debe hacer lo mismo o una voz
    // sostenida puede impedir IsFinished para siempre.
    private bool _eofReleaseStarted;
    private long _eofReleaseDeadlineSample;
    private const int EofReleaseTailMilliseconds = 500;

    private float _speed = 1f;
    private int _stepAdvancedPending;

    public DsnLikeAudioTrackPlayer(ChiptuneEngineInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        _core = LyraCoreFactory.Prepare(input);

        var settings = EngineSettingsRuntime.Current;
        // VoicePool RT-6 has a fixed 1024-frame scratch today. Do not allow an
        // engine setting larger than that to corrupt the core.
        _blockFrames = Math.Min(1024, Math.Max(256, settings.RenderBlockFrames));
        _ringBlockCount = Math.Clamp(settings.RingBufferBlocks, 4, 32);
        _audioTrackBufferMultiplier =
            Math.Clamp(settings.AudioTrackBufferMultiplier, 4, 24);

        _audioBackend = CreateAudioBackend(settings.AudioBackend);
        _activeBackendName = _audioBackend.Name;

        _leftMix = new float[_blockFrames];
        _rightMix = new float[_blockFrames];
        _renderInterleaved = new float[_blockFrames * OutputChannels];

        ChannelCount = _core.Channels.Length;
        _dsnProgramBank = DsnProgramBank.Load();
        _dsnDrumSettings = DsnDrumKitSettingsStore.Load();
        _dsnChannels = new DsnLikeSynth[ChannelCount];
        _dsnDrums = new DsnDrumKit[ChannelCount];
        _dsnScratch = new float[ChannelCount][];
        for (int i = 0; i < ChannelCount; i++)
        {
            _dsnChannels[i] = new DsnLikeSynth(16, SampleRate, _dsnProgramBank.Resolve(_core.Channels[i].Program));
            _dsnDrums[i] = new DsnDrumKit(SampleRate, _dsnDrumSettings);
            _dsnScratch[i] = new float[_blockFrames];
        }
        _commonWaveTypes = new WaveType[ChannelCount];
        _waveTypeOverrides = new WaveType?[ChannelCount];
        _channelBlockCapture = new float[ChannelCount][];
        _captureChannelFlags = new bool[ChannelCount];
        _channelCapture = new float[ChannelCount][];

        for (int i = 0; i < ChannelCount; i++)
        {
            WaveType common = i < input.Channels.Length
                ? input.Channels[i].WaveType
                : WaveType.Square;

            if (_core.Channels[i].IsPercussion)
                common = WaveType.ChipDrums;

            _commonWaveTypes[i] = common;
            _channelBlockCapture[i] = new float[_blockFrames];
            _channelCapture[i] = new float[CaptureLength];
        }

        _eq.ConfigureSampleRate(SampleRate);
        _bassRestoration.ConfigureSampleRate(SampleRate);

        ResetQueueLocked();
    }

    public int ChannelCount { get; }

    public bool IsFinished => Volatile.Read(ref _isFinished);

    /// <summary>
    /// TrackerPlayer no depende de este valor para ubicar la fila; usa
    /// VirtualSample. Se expone el batch siguiente como índice monotónico.
    /// </summary>
    public int CurrentRow => _core.Scheduler.NextBatchIndex;

    public float Speed
    {
        get => Volatile.Read(ref _speed);
        set
        {
            float next = Math.Clamp(value, 0.25f, 4f);
            lock (_stateLock)
            {
                float previous = Volatile.Read(ref _speed);
                if (MathF.Abs(previous - next) < 0.0001f)
                    return;

                // Reancla el reloj de presentación al cambiar velocidad para
                // que la barra/tracker no salten hacia delante o atrás.
                long presentedSample = GetPresentationSample();
                long presentedFrame = 0;
                try { presentedFrame = Math.Max(0L, _audioBackend?.PresentedFrames ?? 0L); }
                catch { }

                _presentationBaseSample = presentedSample;
                _presentationBaseFrame = presentedFrame;
                Volatile.Write(ref _speed, next);
            }
        }
    }

    public long VirtualSample => GetPresentationSample();

    public long TotalSamples => _core.Song.DurationSamples;

    public event Action? OnStepAdvanced;

    public List<WaveType> GetWaveTypes()
    {
        // _commonWaveTypes tiene longitud fija durante toda la sesión y cada
        // elemento enum se escribe atómicamente. No vale la pena bloquear el
        // hilo UI detrás de un tramo de seek sólo para sacar este snapshot.
        var snapshot = new WaveType[_commonWaveTypes.Length];
        Array.Copy(_commonWaveTypes, snapshot, snapshot.Length);
        return new List<WaveType>(snapshot);
    }

    public void SetWaveType(int channel, WaveType wave)
    {
        if ((uint)channel >= (uint)ChannelCount) return;
        if (!Enum.IsDefined(typeof(WaveType), wave)) return;

        lock (_stateLock)
        {
            if (_core.Channels[channel].IsPercussion)
            {
                _commonWaveTypes[channel] = WaveType.ChipDrums;
                return;
            }

            _commonWaveTypes[channel] = wave;
            _waveTypeOverrides[channel] = wave;

            var lyraWave = LyraWaveTypeBridge.ToLyra(wave);
            _core.Channels[channel].SetWaveType(lyraWave);
        }
    }

    public void SetChannelGain(int channel, float gain)
    {
        if ((uint)channel >= (uint)ChannelCount) return;
        lock (_stateLock)
        {
            LyraChannelState state = _core.Channels[channel];
            state.SetUserMix(Math.Clamp(gain, 0f, 2f), state.Muted, state.Solo);
        }
    }

    public float GetChannelGain(int channel)
    {
        if ((uint)channel >= (uint)ChannelCount) return 1f;
        lock (_stateLock)
            return _core.Channels[channel].UserGain;
    }

    public float[] GetChannelGains()
    {
        lock (_stateLock)
        {
            var result = new float[ChannelCount];
            for (int i = 0; i < result.Length; i++)
                result[i] = _core.Channels[i].UserGain;
            return result;
        }
    }

    public void SetEqBand(int band, float gainDb) => _eq.SetBandGain(band, gainDb);
    public float[] GetEqGains() => _eq.GetGains();
    public void SetPreamp(float gainDb) => _eq.SetPreampDb(gainDb);
    public float GetPreamp() => _eq.GetPreamp();

    public void SetBassRestorationEnabled(bool enabled) =>
        _bassRestoration.SetEnabled(enabled);

    public void SetBassRestorationIntensity(float intensity) =>
        _bassRestoration.SetIntensity(intensity);

    public void SetBassRestorationFrequency(float frequencyHz) =>
        _bassRestoration.SetFrequency(frequencyHz);

    public void SetBassRestorationMix(float mix) =>
        _bassRestoration.SetMix(mix);

    public void Play()
    {
        lock (_stateLock)
        {
            ThrowIfDisposed();

            if (_isFinished)
            {
                ResetSynthLocked(resetPresentedCursor: true);
                    }

            EnsureAudioBackendLocked();

            if (_running)
                return;

            _running = true;
            _producerFinished = false;

            // El writer arranca el backend después de que el ring tenga PCM
            // preparado. Esto replica el prime de Clean Room y evita abrir
            // AAudio para que su callback reciba silencio antes del primer bloque.
            StartWorkersLocked();
        }
    }

    /// <summary>
    /// IChiptunePlayer.Stop means pause in the current MIDIRift UI. State and
    /// cursor are therefore preserved. Reset() is the destructive operation.
    /// </summary>
    public void Stop()
    {
        Thread? producer;
        Thread? writer;

        lock (_stateLock)
        {
            if (_disposed) return;
            _running = false;
            producer = _producerThread;
            writer = _writerThread;
            _producerThread = null;
            _writerThread = null;

            try { _audioBackend?.Pause(); }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MIDIRift.Audio] Pause backend: {ex}");
            }

            // Los waits del ring tienen timeout corto; no alteramos los
            // contadores de los semáforos al pausar porque la cola debe
            // conservar exactamente su estado para un resume posterior.
        }

        JoinWorker(producer);
        JoinWorker(writer);
    }

    public void Reset()
    {
        Stop();

        lock (_stateLock)
        {
            if (_disposed) return;

            try { _audioBackend?.ResetForSeek(); }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MIDIRift.Audio] Reset backend: {ex}");
            }

            ResetSynthLocked(resetPresentedCursor: true);
                ResetQueueLocked();
        }
    }

    public void SeekTo(float seconds)
    {
        bool resume;
        long target;

        lock (_stateLock)
        {
            ThrowIfDisposed();
            resume = _running;
            target = (long)(
                Math.Clamp(seconds, 0f, (float)_core.Song.DurationSeconds)
                * SampleRate);
        }

        Stop();

        lock (_stateLock)
        {
            ThrowIfDisposed();

            try { _audioBackend?.ResetForSeek(); }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MIDIRift.Audio] Seek backend reset: {ex}");
            }

            // DSN reconstruye su propio estado sin renderizar PCM ni mantener
            // un VoicePool Lyra paralelo.
            RebuildDsnStateForSeekLocked(target);

            _presentationBaseSample = Math.Min(target, _renderSampleCursor);
            _presentationBaseFrame = 0;
            _renderOutputFrameCursor = 0;
            Interlocked.Exchange(ref _writtenSampleCursor, _presentationBaseSample);
            ResetCaptureTimelineLocked(0);

            _isFinished = false;
            _producerFinished = false;
            _eofReleaseStarted = false;
            _eofReleaseDeadlineSample = 0;
            Volatile.Write(ref _pendingEndSample, -1);
            ResetQueueLocked();
        }

        if (resume)
            Play();

        OnStepAdvanced?.Invoke();
    }

    /// <summary>
    /// Reconstruct DSN state directly from MIDI events. This intentionally avoids
    /// the old shadow Lyra VoicePool. Seek is event-to-event and advances only
    /// DSN logical/envelope state, never PCM.
    /// </summary>
    private void RebuildDsnStateForSeekLocked(long targetSample)
    {
        _core.Scheduler.Reset();
        ResetDsnSynths();

        for (int i = 0; i < _core.Channels.Length; i++)
        {
            float gain = _core.Channels[i].UserGain;
            bool muted = _core.Channels[i].Muted;
            bool solo = _core.Channels[i].Solo;
            _core.Channels[i].Reset();
            _core.Channels[i].SetUserMix(gain, muted, solo);
            if (!_core.Channels[i].IsPercussion)
                _dsnChannels[i].ApplyPatch(_dsnProgramBank.Resolve(_core.Channels[i].Program));
        }

        long cursor = 0;
        while (cursor < targetSample)
        {
            _core.Scheduler.DispatchDueBatches(cursor, ApplyActionLocked);
            int maxFrames = (int)Math.Min(targetSample - cursor, int.MaxValue);
            int segment = _core.Scheduler.FramesUntilNextBatch(cursor, maxFrames);
            if (segment <= 0)
            {
                if (!_core.Scheduler.HasPendingEvents) segment = maxFrames;
                else continue;
            }

            for (int ch = 0; ch < ChannelCount; ch++)
                if (!_core.Channels[ch].IsPercussion)
                    _dsnChannels[ch].AdvanceStateOnly(segment);

            cursor += segment;
        }

        _core.Scheduler.DispatchDueBatches(targetSample, ApplyActionLocked);
        _renderSampleCursor = targetSample;
        _timelineSampleCursor = targetSample;
    }

    public void CopyMixSamples(float[] dest)
    {
        ArgumentNullException.ThrowIfNull(dest);
        Volatile.Write(ref _lastMixCaptureRequestMs, Environment.TickCount64);
        CopyWindowEndingAt(
            _mixCapture,
            dest,
            Interlocked.Read(ref _mixCaptureFramesWritten),
            GetPresentationOutputFrame());
    }

    public void CopyMeterSamples(float[] dest)
    {
        ArgumentNullException.ThrowIfNull(dest);
        Volatile.Write(ref _lastMixCaptureRequestMs, Environment.TickCount64);
        CopyWindowEndingAt(
            _meterCapture,
            dest,
            Interlocked.Read(ref _meterCaptureFramesWritten),
            GetPresentationOutputFrame());
    }

    public void CopyChannelSamples(int channel, float[] dest)
    {
        ArgumentNullException.ThrowIfNull(dest);
        Volatile.Write(ref _lastChannelCaptureRequestMs, Environment.TickCount64);

        if ((uint)channel >= (uint)_channelCapture.Length)
        {
            Array.Clear(dest);
            return;
        }

        CopyWindowEndingAt(
            _channelCapture[channel],
            dest,
            Interlocked.Read(ref _channelCaptureFramesWritten),
            GetPresentationOutputFrame());
    }

    private void StartWorkersLocked()
    {
        _producerThread = new Thread(ProducerLoop)
        {
            IsBackground = true,
            Name = "MIDIRift-Lyra-Producer",
            Priority = ThreadPriority.Highest,
        };

        _writerThread = new Thread(WriterLoop)
        {
            IsBackground = true,
            Name = "MIDIRift-Lyra-AudioOutput",
            Priority = ThreadPriority.Highest,
        };

        _producerThread.Start();
        _writerThread.Start();
    }

    private void ProducerLoop()
    {
        global::Android.OS.Process.SetThreadPriority(
            global::Android.OS.ThreadPriority.UrgentAudio);


        try
        {
            while (Volatile.Read(ref _running))
            {
                if (Volatile.Read(ref _producerFinished))
                    break;

                SemaphoreSlim? free = _freeBlocks;
                if (free is null)
                    break;

                if (!free.Wait(20))
                    continue;

                if (!Volatile.Read(ref _running))
                    break;

                int slot = _produceIndex;
                bool finished;

                lock (_stateLock)
                {
                    if (!_running)
                    {
                        TryRelease(free);
                        break;
                    }

                    long blockStartSample = _renderSampleCursor;
                    long blockStartOutputFrame = _renderOutputFrameCursor;

                    long renderStart = Stopwatch.GetTimestamp();
                    finished = RenderBlockLocked(
                        _pcmRing[slot],
                        _blockFrames,
                        publishCapture: true);
                    RecordRenderTelemetry(Stopwatch.GetTimestamp() - renderStart);

                    _ringFrameCounts[slot] = _blockFrames;
                    _ringStartSamples[slot] = blockStartSample;
                    _ringEndSamples[slot] = _renderSampleCursor;
                    _ringOutputStartFrames[slot] = blockStartOutputFrame;
                    _ringEndFlags[slot] = finished;
                    _produceIndex = (_produceIndex + 1) % _ringBlockCount;
                }

                _readyBlocks?.Release();

                if (Interlocked.Exchange(ref _stepAdvancedPending, 0) != 0)
                    OnStepAdvanced?.Invoke();

                if (finished)
                {
                    _producerFinished = true;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Lyra producer failed: {ex}");
            _running = false;
            TryRelease(_readyBlocks);
        }
    }

    private void WriterLoop()
    {
        global::Android.OS.Process.SetThreadPriority(
            global::Android.OS.ThreadPriority.UrgentAudio);

        try
        {
            while (Volatile.Read(ref _running) ||
                   (!Volatile.Read(ref _isFinished) &&
                    Volatile.Read(ref _producerFinished)))
            {
                SemaphoreSlim? ready = _readyBlocks;
                if (ready is null)
                    break;

                if (!ready.Wait(20))
                {
                    if (_producerFinished && !HasReadyBlocks())
                    {
                        if (TryFinalizePresentedEnd())
                            break;

                        // El último bloque ya fue escrito, pero todavía está en
                        // FIFO/buffer del backend. Seguimos vivos hasta oírlo.
                        if (Volatile.Read(ref _pendingEndSample) >= 0)
                            continue;

                        break;
                    }
                    continue;
                }

                if (!_running && !HasReadyBlocks())
                    break;

                int slot = _consumeIndex;
                int frames = _ringFrameCounts[slot];
                long blockStartSample = _ringStartSamples[slot];
                long blockEndSample = _ringEndSamples[slot];
                long blockStartOutputFrame = _ringOutputStartFrames[slot];
                bool end = _ringEndFlags[slot];

                if (frames <= 0)
                {
                    TryRelease(_freeBlocks);
                    continue;
                }

                IAudioBackend? backend;
                lock (_stateLock)
                {
                    EnsureAudioBackendLocked();
                    backend = _audioBackend;
                }

                if (backend is null)
                    break;

                // AAudio debe arrancar con datos ya preparados. Play() es
                // idempotente en el backend nativo de Clean Room.
                backend.Play();

                // No llenar el FIFO nativo hasta su capacidad máxima. Eso hacía
                // que Lyra pudiera ir más de un segundo por delante del DAC:
                // el tracker ya seguía PresentedFrames, pero FFT/osciloscopio
                // perdían la ventana histórica que querían mostrar.
                //
                // El colchón real queda repartido entre:
                //   managed PCM ring + unas pocas ráfagas del FIFO AAudio.
                // Conservamos protección contra underrun sin convertir a la UI
                // en arqueóloga del audio.
                if (backend is AndroidAAudioBackend aaudio)
                {
                    int burst = Math.Max(1, aaudio.FramesPerBurst);
                    int targetBufferedFrames = Math.Max(
                        _blockFrames * 4,
                        aaudio.AdaptiveBufferFrames + burst * 2);

                    while (Volatile.Read(ref _running) &&
                           aaudio.BufferedFrames > targetBufferedFrames)
                    {
                        Thread.Sleep(1);
                    }

                    if (!Volatile.Read(ref _running))
                        break;
                }

                int sampleCount = frames * OutputChannels;
                int written = backend.Write(_pcmRing[slot], sampleCount);

                if (written < 0)
                {
                    _running = false;
                    break;
                }

                long writtenFrames = written / OutputChannels;
                PublishMixCapture(
                    _pcmRing[slot],
                    frames,
                    blockStartOutputFrame);

                long writtenSongSample = frames > 0 && writtenFrames < frames
                    ? blockStartSample + (long)Math.Round(
                        (blockEndSample - blockStartSample) *
                        (writtenFrames / (double)frames))
                    : blockEndSample;

                Interlocked.Exchange(
                    ref _writtenSampleCursor,
                    writtenSongSample);

                _ringFrameCounts[slot] = 0;
                _ringStartSamples[slot] = 0;
                _ringEndSamples[slot] = 0;
                _ringOutputStartFrames[slot] = 0;
                _ringEndFlags[slot] = false;
                _consumeIndex = (_consumeIndex + 1) % _ringBlockCount;
                TryRelease(_freeBlocks);

                if (end)
                {
                    // No anunciar Finished hasta que el backend haya
                    // PRESENTADO el final, no sólo aceptado el bloque.
                    Volatile.Write(
                        ref _pendingEndSample,
                        Math.Min(
                            TotalSamples,
                            writtenSongSample));

                    if (TryFinalizePresentedEnd())
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Lyra writer failed: {ex}");
            _running = false;
        }
    }

    private bool RenderBlockLocked(
        float[] destination,
        int frameCount,
        bool publishCapture)
    {
        frameCount = Math.Clamp(frameCount, 1, _blockFrames);

        Array.Clear(_leftMix, 0, frameCount);
        Array.Clear(_rightMix, 0, frameCount);
        Array.Clear(destination, 0, frameCount * OutputChannels);

        bool captureChannels =
            publishCapture &&
            Environment.TickCount64 -
                Volatile.Read(ref _lastChannelCaptureRequestMs)
            <= ChannelCaptureKeepAliveMs;

        if (captureChannels)
        {
            for (int ch = 0; ch < ChannelCount; ch++)
            {
                Array.Clear(_channelBlockCapture[ch], 0, frameCount);
                _captureChannelFlags[ch] = true;
            }
        }
        else
        {
            Array.Clear(_captureChannelFlags);
        }

        long blockStart = (long)_timelineSampleCursor;
        long captureBlockStart = _renderOutputFrameCursor;
        int rendered = 0;
        double speed = Math.Clamp((double)Volatile.Read(ref _speed), 0.25, 4.0);

        while (rendered < frameCount)
        {
            long absoluteSample = (long)_timelineSampleCursor;

            _core.Scheduler.DispatchDueBatches(
                absoluteSample,
                ApplyActionLocked);

            int remaining = frameCount - rendered;
            int sourceFramesToEvent = _core.Scheduler.FramesUntilNextBatch(
                absoluteSample,
                int.MaxValue);

            // El scheduler está expresado en muestras de la canción, mientras
            // que RenderSegment trabaja en frames reales de salida. Convertimos
            // la distancia musical a frames de salida sin cambiar pitch.
            int segment = sourceFramesToEvent == int.MaxValue
                ? remaining
                : Math.Min(remaining, Math.Max(1,
                    (int)Math.Ceiling(sourceFramesToEvent / speed)));

            RenderDsnSegment(
                rendered,
                segment,
                captureChannels);

            rendered += segment;
            _timelineSampleCursor += segment * speed;

        }


        // Meter is intentionally pre-soft-clip. This preserves peak > 1.0
        // information exactly like Classic's metrics path.
        if (publishCapture)
            PublishMeterCapture(
                _leftMix,
                _rightMix,
                frameCount,
                captureBlockStart);

        // Same shared DSP classes used by Classic.
        _eq.ProcessBlock(_leftMix, _rightMix, frameCount);

        for (int i = 0; i < frameCount; i++)
        {
            int dst = i * OutputChannels;
            destination[dst] = SoftClip(_leftMix[i]);
            destination[dst + 1] = SoftClip(_rightMix[i]);
        }

        _bassRestoration.ProcessInterleaved(
            destination,
            0,
            frameCount);

        if (publishCapture && captureChannels)
            PublishChannelCapture(frameCount, captureBlockStart);

        _renderSampleCursor = (long)_timelineSampleCursor;
        _renderOutputFrameCursor += frameCount;

        bool timelineEnded =
            _renderSampleCursor >= _core.Song.DurationSamples &&
            !_core.Scheduler.HasPendingEvents;

        if (!timelineEnded)
            return false;

        if (!_eofReleaseStarted)
        {
            // EOF tiene autoridad. Un MIDI no está obligado a incluir un
            // AllNotesOff final para que el reproductor pueda terminar.
            // Igual que Classic, liberamos sustain/legato y mandamos cualquier
            // voz huérfana a Release cuando ya no queda timeline por consumir.
            for (int channelIndex = 0;
                 channelIndex < _core.Channels.Length;
                 channelIndex++)
            {
                LyraChannelState channel = _core.Channels[channelIndex];
                channel.SetSustain(false);
                channel.SetLegatoEnabled(false);
                channel.SetPortamentoEnabled(false);
                _dsnChannels[channelIndex].ReleaseAll();
                _dsnDrums[channelIndex].ReleaseAll();
            }

            _eofReleaseStarted = true;
            _eofReleaseDeadlineSample =
                _renderSampleCursor +
                (long)SampleRate * EofReleaseTailMilliseconds / 1000L;
        }

        bool anyDsnActive = _dsnChannels.Any(static synth => synth.ActiveVoiceCount > 0) ||
            _dsnDrums.Any(static drums => drums.ActiveVoiceCount > 0);
        if (!anyDsnActive)
            return true;

        // Protección final contra envelopes/estados corruptos. Una canción no
        // puede quedarse reproduciendo eternamente por una voz que se niega a
        // abandonar Sustain/Release.
        if (_renderSampleCursor >= _eofReleaseDeadlineSample)
        {
            ResetDsnSynths();
            return true;
        }

        return false;
    }

    private void RenderDsnSegment(int offset, int frames, bool captureChannels)
    {
        bool anySolo = HasSoloChannelsLocked();
        for (int ch = 0; ch < ChannelCount; ch++)
        {
            LyraChannelState state = _core.Channels[ch];
            if (state.Muted || (anySolo && !state.Solo))
                continue;

            Span<float> mono = _dsnScratch[ch].AsSpan(0, frames);
            mono.Clear();
            if (state.IsPercussion)
                _dsnDrums[ch].Render(mono);
            else
                _dsnChannels[ch].Render(mono);

            float gain = state.Volume * state.Expression * state.UserGain;
            float gainL = gain * state.PanGainL;
            float gainR = gain * state.PanGainR;
            for (int i = 0; i < frames; i++)
            {
                float sample = mono[i];
                // Integration safety: a non-finite DSP sample poisons the whole
                // stereo mix and Android will effectively receive silence/NaNs.
                if (!float.IsFinite(sample))
                    sample = 0f;
                _leftMix[offset + i] += sample * gainL;
                _rightMix[offset + i] += sample * gainR;
                if (captureChannels)
                    _channelBlockCapture[ch][offset + i] = sample * gain;
            }
        }
    }

    public bool IsPercussionChannel(int channel)
        => (uint)channel < (uint)_core.Channels.Length && _core.Channels[channel].IsPercussion;

    public void RefreshDrumKitSettings()
    {
        _dsnDrumSettings = DsnDrumKitSettingsStore.Load();
        for (int i = 0; i < _dsnDrums.Length; i++)
            _dsnDrums[i].ApplySettings(_dsnDrumSettings);
    }

    public int GetChannelProgram(int channel)
    {
        if ((uint)channel >= (uint)_core.Channels.Length)
            return 0;
        return Math.Clamp(_core.Channels[channel].Program, 0, 127);
    }

    /// <summary>
    /// Contextual/manual GM program selection for a live DSN channel.
    /// This changes only runtime channel state and the patch used by future
    /// notes; it does not create or modify a User Bank entry.
    /// </summary>
    public void SetChannelProgram(int channel, int program)
    {
        if ((uint)channel >= (uint)_core.Channels.Length ||
            (uint)channel >= (uint)_dsnChannels.Length ||
            _core.Channels[channel].IsPercussion)
            return;

        program = Math.Clamp(program, 0, 127);
        _core.Channels[channel].SetProgram(program);
        _dsnChannels[channel].SetPatchForNewVoices(_dsnProgramBank.Resolve(program));
    }

    /// <summary>
    /// Reloads the persistent DSN bank and applies the current GM assignment to
    /// future notes on this channel. Already sounding voices keep their patch.
    /// </summary>
    public void RefreshChannelPatchAssignment(int channel)
    {
        if ((uint)channel >= (uint)_dsnChannels.Length)
            return;

        var bank = DsnProgramBank.Load();
        int program = GetChannelProgram(channel);
        _dsnChannels[channel].SetPatchForNewVoices(bank.Resolve(program));
    }

    public DsnRealtimeTelemetrySnapshot GetRealtimeTelemetrySnapshot()
    {
        long blocks = Volatile.Read(ref _telemetryBlocks);
        long ticks = Volatile.Read(ref _telemetryRenderTicks);
        int active = 0, drums = 0, dual = 0, fm = 0, sync = 0, filter = 0, drive = 0;
        for (int i = 0; i < ChannelCount; i++)
        {
            int n = _core.Channels[i].IsPercussion ? _dsnDrums[i].ActiveVoiceCount : _dsnChannels[i].ActiveVoiceCount;
            active += n;
            if (_core.Channels[i].IsPercussion) { drums += n; continue; }
            var p = _dsnChannels[i].Patch;
            if (p.Osc2Level > 0.001f) dual += n;
            if (MathF.Abs(p.FmAmount) > 0.001f) fm += n;
            if (p.HardSync) sync += n;
            if (p.CutoffHz < 19900f || p.Resonance > 0.001f || MathF.Abs(p.EnvelopeToCutoff) > 0.001f) filter += n;
            if (p.Drive > 0.001f) drive += n;
        }
        int backendBuffered = 0, backendBuffer = 0, burst = 0, increases = 0, underruns = 0;
        try
        {
            underruns = _audioBackend?.UnderrunCount ?? 0;
            if (_audioBackend is AndroidAAudioBackend a)
            {
                backendBuffered = a.BufferedFrames; backendBuffer = a.AdaptiveBufferFrames;
                burst = a.FramesPerBurst; increases = a.AdaptiveIncreaseCount;
            }
        } catch { }
        return new DsnRealtimeTelemetrySnapshot(
            blocks, Volatile.Read(ref _telemetryLateBlocks),
            blocks == 0 ? 0 : ticks * 1000.0 / Stopwatch.Frequency / blocks,
            Volatile.Read(ref _telemetryMaxTicks) * 1000.0 / Stopwatch.Frequency,
            _blockFrames * 1000.0 / SampleRate, active, Volatile.Read(ref _telemetryPeakVoices), drums,
            dual, fm, sync, filter, drive, _readyBlocks?.CurrentCount ?? 0, _ringBlockCount,
            backendBuffered, backendBuffer, burst, increases, underruns,
            GC.GetTotalAllocatedBytes(false), GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
    }

    private void RecordRenderTelemetry(long ticks)
    {
        long blocks = Interlocked.Increment(ref _telemetryBlocks);
        Interlocked.Add(ref _telemetryRenderTicks, ticks);
        long prev;
        while (ticks > (prev = Volatile.Read(ref _telemetryMaxTicks)) &&
               Interlocked.CompareExchange(ref _telemetryMaxTicks, ticks, prev) != prev) { }

        double ms = ticks * 1000.0 / Stopwatch.Frequency;
        double deadlineMs = _blockFrames * 1000.0 / SampleRate;
        if (ms > deadlineMs) Interlocked.Increment(ref _telemetryLateBlocks);

        int voices = 0;
        for (int i = 0; i < ChannelCount; i++)
            voices += _core.Channels[i].IsPercussion ? _dsnDrums[i].ActiveVoiceCount : _dsnChannels[i].ActiveVoiceCount;
        int peak;
        while (voices > (peak = Volatile.Read(ref _telemetryPeakVoices)) &&
               Interlocked.CompareExchange(ref _telemetryPeakVoices, voices, peak) != peak) { }

        long now = Environment.TickCount64;
        if (now - Volatile.Read(ref _telemetryLastLogMs) >= 1000 &&
            Interlocked.Exchange(ref _telemetryLastLogMs, now) != now)
        {
            long totalTicks = Volatile.Read(ref _telemetryRenderTicks);
            long maxTicks = Volatile.Read(ref _telemetryMaxTicks);
            long late = Volatile.Read(ref _telemetryLateBlocks);
            int underruns = 0; try { underruns = _audioBackend?.UnderrunCount ?? 0; } catch { }
            Debug.WriteLine($"DSN RT: mean={(totalTicks * 1000.0 / Stopwatch.Frequency / Math.Max(1, blocks)):F2}ms max={(maxTicks * 1000.0 / Stopwatch.Frequency):F2}ms deadline={deadlineMs:F2}ms late={late}/{blocks} voices={voices} peak={Volatile.Read(ref _telemetryPeakVoices)} underruns={underruns}");
        }
    }

    private void ResetDsnSynths()
    {
        for (int i = 0; i < _dsnChannels.Length; i++)
        {
            _dsnChannels[i].Reset();
            _dsnDrums[i].Reset();
        }
    }

    private void ApplyActionLocked(LyraCompiledEvent action)
    {
        int channelIndex = action.ChannelIndex;
        if ((uint)channelIndex >= (uint)_core.Channels.Length)
            return;

        LyraChannelState channel = _core.Channels[channelIndex];

        switch (action.Kind)
        {
            case LyraEventKind.NoteOn:
                if (channel.IsPercussion)
                    _dsnDrums[channelIndex].NoteOn(action.Data1, action.Value);
                else
                {
                    int? portamentoSource = channel.ConsumePortamentoSourceNote();
                    _dsnChannels[channelIndex].NoteOn(
                        action.Data1,
                        action.Value,
                        channel.PortamentoEnabled || channel.LegatoEnabled || portamentoSource.HasValue,
                        channel.PortamentoEnabled ? channel.PortamentoSeconds : 0.005f,
                        portamentoSource);
                }
                break;

            case LyraEventKind.NoteOff:
                if (!channel.IsPercussion)
                    _dsnChannels[channelIndex].NoteOff(action.Data1, channel.Sustain);
                break;

            case LyraEventKind.Program:
                channel.SetProgram(action.Data1);
                if (!channel.IsPercussion)
                    _dsnChannels[channelIndex].SetPatchForNewVoices(_dsnProgramBank.Resolve(channel.Program));

                if (_waveTypeOverrides[channelIndex] is WaveType commonOverride)
                {
                    // Un override manual del usuario siempre gana sobre GM.
                    channel.SetWaveType(
                        LyraWaveTypeBridge.ToLyra(commonOverride));
                    _commonWaveTypes[channelIndex] = commonOverride;
                }
                else if (channel.IsPercussion)
                {
                    _commonWaveTypes[channelIndex] = WaveType.ChipDrums;
                }
                else
                {
                    // IMPORTANTÍSIMO: el tracker consulta GetWaveTypes(), no
                    // ChannelState directamente. Sin esta sincronización Lyra
                    // podía renderizar SAW mientras la UI seguía diciendo P25,
                    // o al revés, después de un ProgramChange.
                    _commonWaveTypes[channelIndex] =
                        LyraWaveTypeBridge.ToCommon(channel.WaveType);
                }
                break;

            case LyraEventKind.Volume:
                channel.SetVolume(action.Value);
                break;

            case LyraEventKind.Expression:
                channel.SetExpression(action.Value);
                break;

            case LyraEventKind.Pan:
                channel.SetPan(action.Value);
                break;

            case LyraEventKind.PitchBend:
                channel.SetPitchBend(action.Value);
                if (!channel.IsPercussion)
                    _dsnChannels[channelIndex].SetPitchBendRatio(channel.PitchBendRatio);
                break;

            case LyraEventKind.PitchBendRange:
                channel.SetPitchBendRange(action.Value);
                if (!channel.IsPercussion)
                    _dsnChannels[channelIndex].SetPitchBendRatio(channel.PitchBendRatio);
                break;

            case LyraEventKind.FineTuning:
                channel.SetFineTuning(action.Value);
                if (!channel.IsPercussion)
                    _dsnChannels[channelIndex].SetPitchBendRatio(channel.PitchBendRatio);
                break;

            case LyraEventKind.CoarseTuning:
                channel.SetCoarseTuning(action.Value);
                if (!channel.IsPercussion)
                    _dsnChannels[channelIndex].SetPitchBendRatio(channel.PitchBendRatio);
                break;

            case LyraEventKind.Modulation:
                channel.SetModulation(action.Value);
                if (!channel.IsPercussion)
                    _dsnChannels[channelIndex].SetModulation(channel.Modulation);
                break;

            case LyraEventKind.Brightness:
                if (!channel.IsPercussion)
                    _dsnChannels[channelIndex].SetBrightness(action.Value);
                break;

            case LyraEventKind.Resonance:
                if (!channel.IsPercussion)
                    _dsnChannels[channelIndex].SetResonance(action.Value);
                break;

            case LyraEventKind.AttackTime:
                if (!channel.IsPercussion)
                    _dsnChannels[channelIndex].SetAttackTime(action.Value);
                break;

            case LyraEventKind.DecayTime:
                if (!channel.IsPercussion)
                    _dsnChannels[channelIndex].SetDecayTime(action.Value);
                break;

            case LyraEventKind.ReleaseTime:
                if (!channel.IsPercussion)
                    _dsnChannels[channelIndex].SetReleaseTime(action.Value);
                break;

            case LyraEventKind.VibratoRate:
                if (!channel.IsPercussion)
                    _dsnChannels[channelIndex].SetVibratoRate(action.Value);
                break;

            case LyraEventKind.VibratoDepth:
                if (!channel.IsPercussion)
                    _dsnChannels[channelIndex].SetVibratoDepth(action.Value);
                break;

            case LyraEventKind.VibratoDelay:
                if (!channel.IsPercussion)
                    _dsnChannels[channelIndex].SetVibratoDelay(action.Value);
                break;

            case LyraEventKind.ResetAllControllers:
                channel.ResetControllers();
                if (!channel.IsPercussion)
                {
                    _dsnChannels[channelIndex].ResetMidiControllers();
                    _dsnChannels[channelIndex].ReleasePending();
                }
                break;

            case LyraEventKind.PortamentoControl:
                channel.SetPortamentoSourceNote((int)MathF.Round(action.Value));
                break;

            case LyraEventKind.PortamentoTime:
                channel.SetPortamentoTime(action.Value);
                break;

            case LyraEventKind.PortamentoSwitch:
                channel.SetPortamentoEnabled(action.Value >= 0.5f);
                break;

            case LyraEventKind.LegatoSwitch:
                channel.SetLegatoEnabled(action.Value >= 0.5f);
                break;

            case LyraEventKind.Sostenuto:
            {
                bool enabled = action.Value >= 0.5f;
                channel.SetSostenuto(enabled);
                if (!channel.IsPercussion)
                    _dsnChannels[channelIndex].SetSostenuto(enabled, channel.Sustain);
                break;
            }

            case LyraEventKind.SoftPedal:
            {
                bool enabled = action.Value >= 0.5f;
                channel.SetSoftPedal(enabled);
                if (!channel.IsPercussion)
                    _dsnChannels[channelIndex].SetSoftPedal(enabled);
                break;
            }

            case LyraEventKind.Sustain:
            {
                bool enabled = action.Value >= 0.5f;
                channel.SetSustain(enabled);
                if (!enabled)
                {
                    // Release only voices whose individual NoteOff was consumed
                    // while sustain was down. Physically held notes remain held.
                    _dsnChannels[channelIndex].ReleasePending();
                }
                break;
            }

            case LyraEventKind.AllSoundOff:
                _dsnChannels[channelIndex].AllSoundOff();
                _dsnDrums[channelIndex].Reset();
                break;

            case LyraEventKind.AllNotesOff:
                _dsnChannels[channelIndex].ReleaseAll();
                _dsnDrums[channelIndex].ReleaseAll();
                break;
        }

        Interlocked.Exchange(ref _stepAdvancedPending, 1);
    }

    private void ResetSynthLocked(bool resetPresentedCursor)
    {
        _core.Scheduler.Reset();
        ResetDsnSynths();

        for (int i = 0; i < _core.Channels.Length; i++)
        {
            _core.Channels[i].Reset();
            if (!_core.Channels[i].IsPercussion)
                _dsnChannels[i].ApplyPatch(_dsnProgramBank.Resolve(_core.Channels[i].Program));

            // Preserve persisted channel gain.
            float gain = _core.Channels[i].UserGain;
            _core.Channels[i].SetUserMix(gain, muted: false, solo: false);

            WaveType common = _commonWaveTypes[i];
            if (_core.Channels[i].IsPercussion)
            {
                _commonWaveTypes[i] = WaveType.ChipDrums;
            }
            else
            {
                _core.Channels[i].SetWaveType(
                    LyraWaveTypeBridge.ToLyra(common));
            }
        }

        _renderSampleCursor = 0;
        _timelineSampleCursor = 0;
        _renderOutputFrameCursor = 0;
        _eofReleaseStarted = false;
        _eofReleaseDeadlineSample = 0;
        Volatile.Write(ref _pendingEndSample, -1);

        if (resetPresentedCursor)
        {
            _presentationBaseSample = 0;
            _presentationBaseFrame = 0;
            Interlocked.Exchange(ref _writtenSampleCursor, 0);
            ResetCaptureTimelineLocked(0);
        }

        _producerFinished = false;
        _isFinished = false;
        Interlocked.Exchange(ref _stepAdvancedPending, 0);
    }

    private bool HasSoloChannelsLocked()
    {
        for (int i = 0; i < _core.Channels.Length; i++)
            if (_core.Channels[i].Solo)
                return true;
        return false;
    }

    private void EnsureAudioBackendLocked()
    {
        if (_audioBackend is not null)
            return;

        _audioBackend = CreateAudioBackend(EngineSettingsRuntime.Current.AudioBackend);
        _activeBackendName = _audioBackend.Name;
    }

    private IAudioBackend CreateAudioBackend(string? requested)
    {
        int requestedFrames = Math.Max(
            _blockFrames * _audioTrackBufferMultiplier,
            _blockFrames * 4);

        bool wantsAAudio = !string.Equals(
            requested,
            "AudioTrack",
            StringComparison.OrdinalIgnoreCase);

        if (wantsAAudio && AndroidAAudioBackend.IsSupported)
        {
            try
            {
                var aaudio = new AndroidAAudioBackend(
                    SampleRate,
                    OutputChannels,
                    requestedFrames);
                aaudio.Initialize();
                Debug.WriteLine(
                    $"[MIDIRift.Audio] Lyra backend: {aaudio.Name}; " +
                    $"burst={aaudio.FramesPerBurst}, buffer={aaudio.AdaptiveBufferFrames}.");
                return aaudio;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[MIDIRift.Audio] AAudio no pudo inicializarse; fallback a AudioTrack: {ex}");
            }
        }

        var audioTrack = new AndroidAudioTrackBackend(
            SampleRate,
            OutputChannels,
            requestedFrames);
        audioTrack.Initialize();
        Debug.WriteLine("[MIDIRift.Audio] Lyra backend: AudioTrack.");
        return audioTrack;
    }

    private void ResetQueueLocked()
    {
        try { _freeBlocks?.Dispose(); } catch { }
        try { _readyBlocks?.Dispose(); } catch { }

        _pcmRing = new float[_ringBlockCount][];
        _ringFrameCounts = new int[_ringBlockCount];
        _ringStartSamples = new long[_ringBlockCount];
        _ringEndSamples = new long[_ringBlockCount];
        _ringOutputStartFrames = new long[_ringBlockCount];
        _ringEndFlags = new bool[_ringBlockCount];

        for (int i = 0; i < _ringBlockCount; i++)
            _pcmRing[i] =
                new float[_blockFrames * OutputChannels];

        _freeBlocks = new SemaphoreSlim(
            _ringBlockCount,
            _ringBlockCount + 2);

        _readyBlocks = new SemaphoreSlim(
            0,
            _ringBlockCount + 2);

        _produceIndex = 0;
        _consumeIndex = 0;
    }

    private bool HasReadyBlocks()
    {
        SemaphoreSlim? ready = _readyBlocks;
        return ready is not null && ready.CurrentCount > 0;
    }

    private void PublishMeterCapture(
        float[] left,
        float[] right,
        int frames,
        long firstFrame)
    {
        for (int i = 0; i < frames; i++)
        {
            long frame = firstFrame + i;
            int dst = (int)(frame & (CaptureLength - 1));
            _meterCapture[dst] = (left[i] + right[i]) * 0.5f;
        }

        Interlocked.Exchange(
            ref _meterCaptureFramesWritten,
            firstFrame + frames);
    }

    private void PublishChannelCapture(
        int frames,
        long firstFrame)
    {
        for (int ch = 0; ch < ChannelCount; ch++)
        {
            float[] source = _channelBlockCapture[ch];
            float[] target = _channelCapture[ch];

            for (int i = 0; i < frames; i++)
            {
                long frame = firstFrame + i;
                target[(int)(frame & (CaptureLength - 1))] = source[i];
            }
        }

        Interlocked.Exchange(
            ref _channelCaptureFramesWritten,
            firstFrame + frames);
    }

    private void PublishMixCapture(
        float[] interleaved,
        int frames,
        long firstFrame)
    {
        for (int i = 0; i < frames; i++)
        {
            long frame = firstFrame + i;
            int src = i * OutputChannels;
            _mixCapture[(int)(frame & (CaptureLength - 1))] =
                (interleaved[src] + interleaved[src + 1]) * 0.5f;
        }

        Interlocked.Exchange(
            ref _mixCaptureFramesWritten,
            firstFrame + frames);
    }

    /// <summary>
    /// Copia la ventana que TERMINA en el sample que el backend está
    /// presentando. Así FFT/osciloscopio pueden leer PCM viejo del capture ring
    /// aunque el productor vaya cientos de ms adelantado.
    /// </summary>
    private static void CopyWindowEndingAt(
        float[] ring,
        float[] destination,
        long newestCapturedExclusive,
        long presentationEndExclusive)
    {
        if (destination.Length == 0)
            return;

        if (newestCapturedExclusive <= 0 ||
            presentationEndExclusive <= 0)
        {
            Array.Clear(destination);
            return;
        }

        long end = Math.Min(
            newestCapturedExclusive,
            presentationEndExclusive);

        long requestedStart = end - destination.Length;
        long oldestAvailable = Math.Max(
            0,
            newestCapturedExclusive - CaptureLength);

        long actualStart = Math.Max(
            requestedStart,
            oldestAvailable);

        int leadingZeros = (int)Math.Clamp(
            actualStart - requestedStart,
            0,
            destination.Length);

        if (leadingZeros > 0)
            Array.Clear(destination, 0, leadingZeros);

        int write = leadingZeros;
        for (long frame = actualStart;
             frame < end && write < destination.Length;
             frame++)
        {
            destination[write++] =
                ring[(int)(frame & (CaptureLength - 1))];
        }

        if (write < destination.Length)
            Array.Clear(
                destination,
                write,
                destination.Length - write);
    }

    private long GetPresentationOutputFrame()
    {
        IAudioBackend? backend = Volatile.Read(ref _audioBackend);
        if (backend is null)
            return Math.Max(0L, _renderOutputFrameCursor);

        try { return Math.Max(0L, backend.PresentedFrames); }
        catch { return Math.Max(0L, _renderOutputFrameCursor); }
    }

    private long GetPresentationSample()
    {
        IAudioBackend? backend = Volatile.Read(ref _audioBackend);
        if (backend is null)
            return Math.Max(
                0,
                Interlocked.Read(ref _writtenSampleCursor));

        long relative;
        try
        {
            relative = Math.Max(0L, backend.PresentedFrames - _presentationBaseFrame);
        }
        catch
        {
            relative = Math.Max(
                0,
                (long)Math.Round((Interlocked.Read(ref _writtenSampleCursor) -
                _presentationBaseSample) / Math.Clamp((double)Volatile.Read(ref _speed), 0.25, 4.0)));
        }

        double presentationSpeed = Math.Clamp((double)Volatile.Read(ref _speed), 0.25, 4.0);
        long sample = _presentationBaseSample + (long)Math.Round(relative * presentationSpeed);

        // Nunca permitir que el reloj visual se adelante a lo que realmente
        // hemos escrito, incluso si un driver reporta un contador extraño.
        long written = Interlocked.Read(ref _writtenSampleCursor);
        if (written >= _presentationBaseSample)
            sample = Math.Min(sample, written);

        return Math.Clamp(sample, 0, TotalSamples);
    }

    private bool TryFinalizePresentedEnd()
    {
        long endSample = Volatile.Read(ref _pendingEndSample);
        if (endSample < 0)
            return false;

        if (GetPresentationSample() < endSample)
            return false;

        Volatile.Write(ref _pendingEndSample, -1);
        _isFinished = true;
        _running = false;
        OnStepAdvanced?.Invoke();
        return true;
    }

    private void ResetCaptureTimelineLocked(long absoluteSample)
    {
        Array.Clear(_mixCapture);
        Array.Clear(_meterCapture);
        for (int ch = 0; ch < _channelCapture.Length; ch++)
            Array.Clear(_channelCapture[ch]);

        Interlocked.Exchange(
            ref _mixCaptureFramesWritten,
            absoluteSample);
        Interlocked.Exchange(
            ref _meterCaptureFramesWritten,
            absoluteSample);
        Interlocked.Exchange(
            ref _channelCaptureFramesWritten,
            absoluteSample);
    }

    private static float SoftClip(float sample)
    {
        // Igual que Classic/Legacy: aproximación racional suave de tanh.
        // La curva anterior x/(1+|x|) reducía también señales normales y,
        // combinada con la antigua atenuación 0.18 del VoicePool, hacía a Lyra demasiado bajo.
        float x2 = sample * sample;
        float y = sample * (27f + x2) / (27f + 9f * x2);
        return Math.Clamp(y, -1f, 1f);
    }

    private static void TryRelease(SemaphoreSlim? semaphore)
    {
        if (semaphore is null)
            return;

        try { semaphore.Release(); }
        catch (SemaphoreFullException) { }
        catch (ObjectDisposedException) { }
    }

    private static void JoinWorker(Thread? thread)
    {
        if (thread is null ||
            thread == Thread.CurrentThread)
            return;

        try { thread.Join(350); }
        catch (ThreadStateException) { }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(
                nameof(LyraAudioTrackPlayer));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Stop();

        lock (_stateLock)
        {
            if (_disposed)
                return;

            _disposed = true;

            try { _audioBackend?.Stop(); }
            catch { }

            try { _audioBackend?.Dispose(); }
            catch { }

            _audioBackend = null;

            try { _freeBlocks?.Dispose(); }
            catch { }

            try { _readyBlocks?.Dispose(); }
            catch { }

            _freeBlocks = null;
            _readyBlocks = null;
        }
    }
}
#endif
