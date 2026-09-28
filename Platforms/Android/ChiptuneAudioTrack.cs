using Android.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace MIDIRift;

/// <summary>
/// Motor de audio para Android. Reemplaza el placeholder anterior
/// (ChiptuneOpenAL.cs), que en realidad contenía una clase ChiptuneNAudio
/// heredando de NAudio.Wave.WaveProvider32 — API de Windows, copiada tal
/// cual del proyecto desktop y que nunca podía compilar/correr en Android.
///
/// El DSP (síntesis, ADSR, portamento, LFO, ruido DMG, etc.) es una
/// migración 1:1 del motor desktop — no cambia una sola línea de esa
/// lógica. Lo único que cambia es el "output":
///
///   Desktop (NAudio):  el sistema operativo LLAMA a Read(buffer) cuando
///                       necesita más muestras (modelo pull).
///   Android (AudioTrack): nosotros generamos bloques y los EMPUJAMOS con
///                       Write(buffer, ..., WriteMode.Blocking) desde un
///                       hilo dedicado (modelo push). Write() bloquea
///                       hasta que hay espacio en el buffer interno del
///                       AudioTrack, así que el hilo se auto-regula solo.
/// </summary>
public class ChiptuneAudioTrack : IChiptunePlayer, IPanelAudioSource
{
    private const int SampleRate = 44100;
    private const double InvSampleRate = 1.0 / SampleRate;
    private const int NumChannelsAudio = 2;

    // Tamaño de bloque generado por iteración del hilo de render.
    // Se usan 1024 frames (~23ms). Un bloque de 2048 (~46ms) hacía cada iteración del
    // hilo hace más trabajo por "despertar", lo que reduce la cantidad de
    // veces por segundo que el hilo depende de que el scheduler de Android
    // le dé CPU a tiempo. Con bloques demasiado grandes, un pico de síntesis retiene el hilo durante más tiempo; con bloques chicos, cualquier hueco de scheduling
    // (típico cuando la app pasa a segundo plano y el proceso pierde
    // prioridad) hace más probable que Write() se quede esperando datos que
    // todavía no se generaron -> corte audible. Con bloques más grandes el
    // engine "adelanta" más trabajo por ciclo, dando más margen antes de
    // que un hueco de CPU se note. Sigue siendo chiptune, no un
    // instrumento en vivo, así que ~23ms mantiene margen sin concentrar tanto trabajo en una sola ráfaga.
    private readonly int _blockFrames;
    private readonly int _ringBufferBlocks;
    private readonly int _audioTrackBufferMultiplier;

    private readonly Channel[] _channels;
    private readonly int _channelCount;

    // Los cambios de WaveType llegan desde el hilo de UI, pero alterar voces y
    // parámetros del canal mientras RenderBlock los recorre provoca carreras
    // intermitentes. Cada posición guarda -1 o el WaveType pendiente y solo el
    // hilo de síntesis aplica la mutación real al inicio de un bloque.
    private readonly int[] _pendingWaveTypes;
    private long _globalSample = 0;
    private long _loopEndSample = 0;
    private double _virtualSample = 0;
    private float _tanhDrive = 0.8f;

    // ── Captura para paneles de visualización (Spectrum/Oscilloscope) ─────
    // Buffers circulares planos, escritos por el hilo de render (RenderBlock)
    // sin locks — igual de espíritu que el resto del motor, que ya evita
    // sincronización en el hot path de audio. Una lectura ocasional (30 fps,
    // desde CopyMixSamples/CopyChannelSamples) puede "romper" en un punto
    // intermedio del buffer sin que se note visualmente: es una visualización,
    // no algo que requiera consistencia estricta.
    private const int CaptureLen = 1 << 16; // 65536 frames (~1.49 s a 44.1 kHz)
    private readonly float[] _mixCapture = new float[CaptureLen];
    // Medición previa al soft clip. Permite informar cuánto supera 1.0 el
    // mix interno sin alterar la forma de onda realmente enviada a AudioTrack.
    private readonly float[] _meterCapture = new float[CaptureLen];
    private readonly float[][] _chCapture;
    private int _mixCaptureWritePos = 0;
    private int _meterCaptureWritePos = 0;
    private int _channelCaptureWritePos = 0;
    private long _mixCaptureFramesWritten = 0;
    private long _renderCaptureFramesWritten = 0;

    // AudioTrack publica PlaybackHeadPosition con una granularidad que puede ser
    // menor que el VSync. Se desenvuelve el contador de 32 bits y se extrapola
    // entre observaciones con Stopwatch para obtener un cursor de reproducción
    // continuo, siempre limitado por los frames realmente escritos.
    private readonly object _playbackClockLock = new();
    private uint _playbackHeadLastRaw = 0;
    private long _playbackHeadWrapBase = 0;
    private long _playbackAnchorFrame = 0;
    private long _playbackAnchorTicks = 0;
    private long _lastChannelCaptureRequestMs = 0;
    private long _lastMixCaptureRequestMs = 0;
    private const long ChannelCaptureKeepAliveMs = 250;

    // Tabla senoidal para el LFO. 2048 entradas dan error despreciable para
    // vibrato y eliminan Math.Sin() del bucle de audio.
    private const int LfoTableBits = 11;
    private const int LfoTableSize = 1 << LfoTableBits;
    private const int LfoTableMask = LfoTableSize - 1;
    private static readonly float[] LfoSineTable = BuildLfoSineTable();
    private static readonly float[] MidiFrequencyTable = BuildMidiFrequencyTable();

    public int ChannelCount => _channelCount;

    // XorShift — sin Random, sin lock, mismo que desktop
    private uint _rngState = 0x12345678u;

    // ── Ecualizador gráfico (DSP real vía GraphicEqualizer) ─────────────────
    // GraphicEqualizer es el componente compartido con Mp3AudioPlayer (ver
    // ese archivo y GraphicEqualizer.cs, portado 1:1 desde Desktop) — ambos
    // motores pasan su PCM por la misma cascada de biquads antes de
    // escribir al AudioTrack.
    private readonly GraphicEqualizer _eq = new();
    private readonly BassRestorationProcessor _bassRestoration = new();

    public void SetEqBand(int band, float gainDb) => _eq.SetBandGain(band, gainDb);

    public float[] GetEqGains() => _eq.GetGains();

    public void SetPreamp(float gainDb) => _eq.SetPreampDb(gainDb);

    public float GetPreamp() => _eq.GetPreamp();

    public void SetBassRestorationEnabled(bool enabled) => _bassRestoration.SetEnabled(enabled);
    public void SetBassRestorationIntensity(float intensity) => _bassRestoration.SetIntensity(intensity);
    public void SetBassRestorationFrequency(float frequencyHz) => _bassRestoration.SetFrequency(frequencyHz);
    public void SetBassRestorationMix(float mix) => _bassRestoration.SetMix(mix);

    public long TotalSamples => _loopEndSample;
    public float Speed { get; set; } = 1f;
    public long VirtualSample => (long)_virtualSample;
    public long GlobalSample => _globalSample;
    public bool IsFinished { get; private set; } = false;

    public int CurrentRow
    {
        get
        {
            int max = 0;
            for (int i = 0; i < _channelCount; i++)
            {
                int v = Math.Max(0, _channels[i].Index - 1);
                if (v > max) max = v;
            }
            return max;
        }
    }

    public event Action? OnStepAdvanced;

    // ── AudioTrack + hilo de render ─────────────────────────────────────
    private AudioTrack? _audioTrack;
    private Thread? _renderThread;
    private Thread? _writerThread;
    private float[][] _pcmRing = Array.Empty<float[]>();
    private SemaphoreSlim? _freeBlocks;
    private SemaphoreSlim? _readyBlocks;
    private int _produceIndex;
    private int _consumeIndex;
    private volatile bool _producerFinished;
    private volatile bool _running = false;
    private readonly object _playLock = new();
    private float[] _renderBuffer = Array.Empty<float>();

    // ── Constructor ───────────────────────────────────────────────────────

    public ChiptuneAudioTrack(List<Channel> channels)
    {
        _eq.ConfigureSampleRate(SampleRate);
        _bassRestoration.ConfigureSampleRate(SampleRate);
        var settings = EngineSettingsRuntime.Current;
        _blockFrames = settings.RenderBlockFrames;
        _ringBufferBlocks = settings.RingBufferBlocks;
        _audioTrackBufferMultiplier = settings.AudioTrackBufferMultiplier;

        _channelCount = channels.Count;
        _channels = new Channel[_channelCount];
        _pendingWaveTypes = new int[_channelCount];
        Array.Fill(_pendingWaveTypes, -1);
        _chCapture = new float[_channelCount][];
        for (int i = 0; i < _channelCount; i++)
            _chCapture[i] = new float[CaptureLen];

        float baseVolume = 0.6f / MathF.Sqrt(Math.Max(1, _channelCount));
        _tanhDrive = Math.Clamp(0.8f + (_channelCount - 1) * 0.04f, 0.8f, 1.4f);

        for (int i = 0; i < _channelCount; i++)
        {
            var ch = channels[i];
            ch.Volume = baseVolume;
            ch.SealPlaybackTrack();
            ApplyNoiseAdsrDefaults(ch);
            ch.RebuildAdsrSamples(SampleRate);
            _channels[i] = ch;

            double total = 0;
            for (int s = 0; s < ch.PlaybackTrackCount; s++)
                total += (ch.PlaybackTrack[s].DurationMs / 1000.0) * SampleRate;
            if (total > _loopEndSample) _loopEndSample = (long)total;
        }
    }

    // ── AudioTrack setup ──────────────────────────────────────────────────

    private void EnsureAudioTrack()
    {
        if (_audioTrack != null) return;

        // Constructor "legacy" (no el Builder, que pide API 23+) para
        // mantener compatibilidad con SupportedOSPlatformVersion 21 del
        // proyecto. Encoding.PcmFloat existe desde API 21.
        int channelConfig = (int)ChannelOut.Stereo;
        int minBufBytes = AudioTrack.GetMinBufferSize(SampleRate, ChannelOut.Stereo, Encoding.PcmFloat);
        if (minBufBytes <= 0) minBufBytes = _blockFrames * NumChannelsAudio * sizeof(float);

        // Headroom x8 (antes x4): el buffer interno de AudioTrack es lo que
        // absorbe los huecos de scheduling que provoca Android cuando la
        // app pasa a segundo plano (el proceso pierde prioridad de CPU y el
        // hilo de render puede tardar en volver a correr). Con Write() en
        // modo Blocking, más buffer no significa que el hilo "adelante"
        // reproducción de más: solo le da más colchón antes de que un hueco
        // de CPU se traduzca en un corte audible. Es un balance latencia
        // vs. estabilidad — para chiptune de fondo, priorizar estabilidad
        // es lo correcto.
        int desiredBufBytes = _blockFrames * NumChannelsAudio * sizeof(float) * _audioTrackBufferMultiplier;
        int bufBytes = Math.Max(minBufBytes, desiredBufBytes);

        _audioTrack = new AudioTrack(
            Android.Media.Stream.Music,
            SampleRate,
            ChannelOut.Stereo,
            Encoding.PcmFloat,
            bufBytes,
            AudioTrackMode.Stream);

        _renderBuffer = new float[_blockFrames * NumChannelsAudio];
        _pcmRing = new float[_ringBufferBlocks][];
        for (int i = 0; i < _pcmRing.Length; i++)
            _pcmRing[i] = new float[_blockFrames * NumChannelsAudio];
        ResetPcmQueue();
    }

    // ── Public API ────────────────────────────────────────────────────────

    public void SetWaveType(int idx, WaveType wave)
    {
        if ((uint)idx >= (uint)_channelCount) return;
        if (!Enum.IsDefined(typeof(WaveType), wave)) return;

        // No tocar Channel ni VoicePool desde la UI. Interlocked publica la
        // orden sin locks y RenderBlock la consume en el hilo propietario.
        Interlocked.Exchange(ref _pendingWaveTypes[idx], (int)wave);

        // Si el engine está detenido no habrá un RenderBlock que la consuma.
        // En ese caso es seguro aplicarla aquí, protegido contra un Play()
        // simultáneo mediante el mismo lock de ciclo de vida.
        lock (_playLock)
        {
            if (!_running)
                ApplyPendingWaveType(idx);
        }
    }

    private void ApplyPendingWaveType(int idx)
    {
        int raw = Interlocked.Exchange(ref _pendingWaveTypes[idx], -1);
        if (raw < 0) return;

        var wave = (WaveType)raw;
        var ch = _channels[idx];
        if (ch.WaveType == wave) return;

        ch.WaveType = wave;
        ch.ClearVoices();
        ch.Lfsr = 0x7FFFu;
        ch.NoiseTimer = double.MaxValue;
        ch.NoisePeriod = double.MaxValue;
        ch.NoiseOut = 0f;

        ApplyNoiseAdsrDefaults(ch);
        ch.RebuildAdsrSamples(SampleRate);
    }

    private void ApplyPendingWaveTypes()
    {
        for (int i = 0; i < _channelCount; i++)
            ApplyPendingWaveType(i);
    }

    public void SetChannelGain(int channel, float gain)
    {
        if ((uint)channel >= (uint)_channelCount) return;
        _channels[channel].UserGain = Math.Clamp(gain, 0f, 2f);
    }

    public float GetChannelGain(int channel)
    {
        if ((uint)channel >= (uint)_channelCount) return 1f;
        return _channels[channel].UserGain;
    }

    public float[] GetChannelGains()
    {
        var result = new float[_channelCount];
        for (int i = 0; i < _channelCount; i++) result[i] = _channels[i].UserGain;
        return result;
    }

    public List<WaveType> GetWaveTypes()
    {
        var list = new List<WaveType>(_channelCount);
        for (int i = 0; i < _channelCount; i++)
        {
            int pending = Volatile.Read(ref _pendingWaveTypes[i]);
            list.Add(pending >= 0 ? (WaveType)pending : _channels[i].WaveType);
        }
        return list;
    }

    public void SetLfoRate(int idx, double hz)
    {
        if ((uint)idx < (uint)_channelCount)
            _channels[idx].LfoRate = Math.Clamp(hz, 0.1, 20.0);
    }

    // ── IPanelAudioSource ────────────────────────────────────────────────

    public void CopyMixSamples(float[] dest)
    {
        Volatile.Write(ref _lastMixCaptureRequestMs, Environment.TickCount64);
        long written = Interlocked.Read(ref _mixCaptureFramesWritten);
        long playbackFrame = GetEstimatedPlaybackFrame(written);
        CopyRingAtFrame(_mixCapture, dest, playbackFrame, written);
    }

    public void CopyMeterSamples(float[] dest)
    {
        Volatile.Write(ref _lastMixCaptureRequestMs, Environment.TickCount64);
        long rendered = Interlocked.Read(ref _renderCaptureFramesWritten);
        long playbackFrame = GetEstimatedPlaybackFrame(rendered);
        CopyRingAtFrame(_meterCapture, dest, playbackFrame, rendered);
    }

    public void CopyChannelSamples(int channel, float[] dest)
    {
        Volatile.Write(ref _lastChannelCaptureRequestMs, Environment.TickCount64);
        if ((uint)channel >= (uint)_chCapture.Length)
        {
            Array.Clear(dest, 0, dest.Length);
            return;
        }

        long rendered = Interlocked.Read(ref _renderCaptureFramesWritten);
        long playbackFrame = GetEstimatedPlaybackFrame(rendered);
        CopyRingAtFrame(_chCapture[channel], dest, playbackFrame, rendered);
    }

    private long GetEstimatedPlaybackFrame(long maxAvailableFrame)
    {
        var track = _audioTrack;
        if (track == null || maxAvailableFrame <= 0)
            return Math.Max(0, maxAvailableFrame);

        uint raw;
        try { raw = unchecked((uint)track.PlaybackHeadPosition); }
        catch { return Math.Max(0, maxAvailableFrame); }

        long now = Stopwatch.GetTimestamp();
        lock (_playbackClockLock)
        {
            if (raw < _playbackHeadLastRaw && _playbackHeadLastRaw - raw > 0x80000000u)
                _playbackHeadWrapBase += 1L << 32;

            long observed = _playbackHeadWrapBase + raw;
            if (_playbackAnchorTicks == 0 || observed != _playbackAnchorFrame)
            {
                _playbackAnchorFrame = observed;
                _playbackAnchorTicks = now;
            }
            _playbackHeadLastRaw = raw;

            long estimated = observed;
            if (track.PlayState == PlayState.Playing && _playbackAnchorTicks != 0)
            {
                double elapsed = (now - _playbackAnchorTicks) / (double)Stopwatch.Frequency;
                estimated = _playbackAnchorFrame + (long)(elapsed * SampleRate);
            }

            if (estimated < 0) estimated = 0;
            if (estimated > maxAvailableFrame) estimated = maxAvailableFrame;
            return estimated;
        }
    }

    private static void CopyRingAtFrame(float[] ring, float[] dest, long endFrameExclusive, long maxAvailableFrame)
    {
        int len = Math.Min(dest.Length, ring.Length);
        long earliestAvailable = Math.Max(0, maxAvailableFrame - ring.Length);
        long startFrame = endFrameExclusive - len;
        int leadingZeros = 0;

        if (startFrame < earliestAvailable)
        {
            leadingZeros = (int)Math.Min(len, earliestAvailable - startFrame);
            startFrame = earliestAvailable;
        }

        if (leadingZeros > 0)
            Array.Clear(dest, 0, leadingZeros);

        int copyCount = len - leadingZeros;
        for (int i = 0; i < copyCount; i++)
        {
            long frame = startFrame + i;
            dest[leadingZeros + i] = ring[(int)(frame & (ring.Length - 1))];
        }

        if (len < dest.Length)
            Array.Clear(dest, len, dest.Length - len);
    }

    public void Play()
    {
        lock (_playLock)
        {
            if (_running) return;
            EnsureAudioTrack();

            _running = true;
            _audioTrack!.Play();

            ResetPcmQueue();
            _writerThread = CreateAudioThread(AudioWriterLoop, "MIDIRift-AudioWriter");
            _renderThread = CreateAudioThread(RenderLoop, "MIDIRift-Synth");
            _writerThread.Start();
            _renderThread.Start();
        }
    }

    public void Stop()
    {
        StopRenderThread();
        try
        {
            _audioTrack?.Pause();
            _audioTrack?.Flush();
        }
        catch (ObjectDisposedException) { /* ya liberado */ }
    }

    public void Reset()
    {
        // Defensivo: TrackerPlayer.Reset() llama _engine.Reset() ANTES de
        // _engine.Stop() (ver TrackerPlayer.cs). Si el hilo de render
        // sigue vivo mientras reseteamos los campos de _channels, hay
        // carrera de datos. Por eso frenamos el hilo acá también, sin
        // depender del orden en que el caller invoque los métodos —
        // Stop() posterior queda como no-op seguro (_running ya es false).
        StopRenderThread();
        ResetChannelState();
    }

    /// <summary>
    /// Vuelve todos los canales a su estado inicial (sin notas activas, sin
    /// portamento/LFO/ruido en curso) y _virtualSample a 0. Compartido por
    /// Reset() y SeekTo() — este último la usa como punto de partida para
    /// "adelantar en silencio" hasta la posición pedida (ver SeekTo).
    /// </summary>
    private void ResetChannelState()
    {
        _globalSample = 0;
        _virtualSample = 0;
        IsFinished = false;
        _rngState = 0x12345678u;

        for (int i = 0; i < _channelCount; i++)
        {
            var ch = _channels[i];
            ch.Index = 0;
            ch.TimeLeft = 0;
            ch.PitchMult = 1f;
            ch.CcVolume = 1f;
            ch.SetPan(0f);
            ch.CcExpression = 1f;
            ch.CcSustain = false;
            ch.AfterTouch = 0f;
            ch.Brightness = 1f;
            ch.SoftPedalGain = 1f;
            ch.VibratoDepthController = 64f / 127f;
            ch.VibratoDelayController = 64f / 127f;
            ch.VibratoAgeSamples = 0;
            ch.ModDepth = 0f;
            ch.LfoPhase = 0;
            ch.LfoRate = 5.0;
            ch.PortamentoOn = false;
            ch.PortamentoTime = 0f;
            ch.PortamentoFreq = 0;
            ch.PortamentoTarget = 0;
            ch.PortamentoSourceNote = -1;
            ch.Attack = ch.DefaultAttack;
            ch.Decay = ch.DefaultDecay;
            ch.Sustain = ch.DefaultSustain;
            ch.Release = ch.DefaultRelease;
            ch.RebuildAdsrSamples(SampleRate);
            ch.Lfsr = 0x7FFF;
            ch.NoiseTimer = double.MaxValue;
            ch.NoisePeriod = double.MaxValue;
            ch.NoiseOut = 0f;
            ch.SostenutoActive = false;
            ch.LegatoOn = false;
            ch.SoundVariation = 0f;
            ch.SetResonance(0f);
            ch.ClearVoices();
        }
    }

    /// <summary>
    /// Salta a <paramref name="seconds"/>. El motor sintetiza en tiempo real
    /// a partir de eventos MIDI secuenciales (notas, portamento, CC, ADSR):
    /// no existe un "punto medio" válido sin haber procesado todo lo
    /// anterior, así que la única forma correcta de saltar es volver a
    /// _virtualSample=0 (misma lógica que Reset()) y "adelantar en
    /// silencio" — llamar RenderBlock() reusando _renderBuffer como scratch
    /// (se descarta, nunca se escribe al AudioTrack) hasta alcanzar el
    /// sample objetivo. Esto reconstruye exactamente el mismo estado
    /// (voces activas, pan, modulación, portamento) que tendría el motor
    /// si hubiera tocado normalmente hasta ese punto — no es una
    /// aproximación visual, es el mismo código de síntesis.
    /// El chunk de avance se mide en frames "reales" (no virtuales); con
    /// Speed != 1 el último chunk puede pasarse por una fracción de
    /// _virtualSample — irrelevante para un seek (no se necesita precisión
    /// de muestra).
    /// </summary>
    public void SeekTo(float seconds)
    {
        // No mantener _playLock mientras se hace Join ni durante la reconstrucción:
        // Play()/StopRenderThread() también usan ese lock y, además, el seek puede
        // tardar varios segundos en un MIDI denso.
        bool wasRunning;
        lock (_playLock)
            wasRunning = _running;

        StopRenderThread();

        try
        {
            _audioTrack?.Pause();
            _audioTrack?.Flush();
        }
        catch (ObjectDisposedException) { }
        catch (Java.Lang.IllegalStateException) { }

        // El hilo ya está detenido; aplicar cualquier cambio solicitado justo
        // antes del seek evita reconstruir el estado con el timbre anterior.
        ApplyPendingWaveTypes();
        ResetChannelState();

        if (_renderBuffer.Length != _blockFrames * NumChannelsAudio)
            _renderBuffer = new float[_blockFrames * NumChannelsAudio];

        long targetSample = (long)(Math.Max(0f, seconds) * SampleRate);
        targetSample = Math.Clamp(targetSample, 0L, _loopEndSample);

        // Reconstrucción silenciosa rápida. Antes se llamaba RenderBlock()
        // muestra por muestra, pagando osciladores, mezcla, clipping, capturas
        // y EQ para un PCM que inmediatamente se descartaba. Ahora se avanza
        // de frontera MIDI en frontera MIDI y se actualiza matemáticamente el
        // estado temporal de voces, envolventes, portamento y percusión.
        FastForwardToSample(targetSample);

        // El ring buffer anterior contiene PCM de la posición vieja. Debe
        // descartarse incluso si el motor estaba pausado.
        ResetPcmQueue();

        if (wasRunning && !IsFinished)
            Play(); // Reinicia productor Y escritor; no solo el sintetizador.
    }

    /// <summary>
    /// Reconstruye el estado del sintetizador sin generar PCM. El coste queda
    /// ligado a la cantidad de MidiStep, no a la cantidad de muestras entre 0
    /// y el destino. En un MIDI largo la diferencia es de millones de vueltas
    /// del hot path frente a unos miles de eventos, porque aparentemente el
    /// tiempo humano también merece cierta consideración.
    /// </summary>
    private void FastForwardToSample(long targetSample)
    {
        if (targetSample <= 0)
            return;

        double speed = Math.Max(0.01, Speed);
        double remainingVirtual = targetSample;
        const double Epsilon = 0.0001;

        while (remainingVirtual > Epsilon)
        {
            double nextBoundary = remainingVirtual;
            bool anyTimeline = false;

            // Cargar todos los eventos que caen exactamente en la posición
            // actual. Un canal puede tener varios steps de duración cero.
            for (int ci = 0; ci < _channelCount; ci++)
            {
                Channel ch = _channels[ci];
                int zeroGuard = 0;
                while (ch.TimeLeft <= Epsilon && ch.Index < ch.PlaybackTrackCount)
                {
                    AdvanceStep(ch, ch.PlaybackTrack[ch.Index]);
                    if (++zeroGuard > 4096) break;
                }

                if (ch.TimeLeft <= Epsilon && ch.Index >= ch.PlaybackTrackCount)
                {
                    ReleaseFinishedChannel(ch);
                    ch.TimeLeft = double.MaxValue;
                }

                if (ch.TimeLeft < double.MaxValue)
                {
                    anyTimeline = true;
                    if (ch.TimeLeft < nextBoundary)
                        nextBoundary = ch.TimeLeft;
                }
            }

            if (!anyTimeline)
            {
                _virtualSample = targetSample;
                _globalSample = (long)Math.Ceiling(targetSample / speed);
                IsFinished = targetSample >= _loopEndSample;
                return;
            }

            double virtualAdvance = Math.Min(remainingVirtual, Math.Max(Epsilon, nextBoundary));
            double renderedAdvance = virtualAdvance / speed;

            for (int ci = 0; ci < _channelCount; ci++)
            {
                Channel ch = _channels[ci];
                AdvanceChannelSilently(ch, renderedAdvance);
                if (ch.TimeLeft < double.MaxValue)
                {
                    ch.TimeLeft -= virtualAdvance;
                    if (ch.TimeLeft < Epsilon) ch.TimeLeft = 0;
                }
            }

            remainingVirtual -= virtualAdvance;
            _virtualSample += virtualAdvance;
            _globalSample += (long)Math.Ceiling(renderedAdvance);
        }

        _virtualSample = targetSample;
        _globalSample = (long)Math.Ceiling(targetSample / speed);
        IsFinished = targetSample >= _loopEndSample;
    }

    private static void ReleaseFinishedChannel(Channel ch)
    {
        // Al terminar el track ya no llegará otro evento que levante sustain o
        // sostenuto. Respetarlos aquí dejaba voces retenidas para siempre en
        // MIDIs que terminan con el pedal abajo o con NoteOff faltantes.
        ch.CcSustain = false;
        ch.SostenutoActive = false;
        ch.LegatoOn = false;

        for (int vi = 0; vi < ch.VoiceCount; vi++)
        {
            ref Voice voice = ref ch.VoicePool[vi];
            voice.KeyDown = false;
            voice.SostenutoHeld = false;
            if (voice.Active)
                voice.EnvState = EnvState.Release;
        }
    }

    /// <summary>Avanza únicamente el estado mutable que normalmente cambia por muestra.</summary>
    private static void AdvanceChannelSilently(Channel ch, double samples)
    {
        if (samples <= 0.0) return;

        if (ch.ModDepth > 0f)
        {
            ch.LfoPhase = (ch.LfoPhase + ch.LfoRate * samples / SampleRate) % 1.0;
            if (ch.LfoPhase < 0.0) ch.LfoPhase += 1.0;
        }

        if (ch.WaveType == WaveType.ChipDrums)
        {
            AdvanceDrumsSilently(ch, samples);
            return;
        }

        bool tonal = ch.WaveType != WaveType.Noise && ch.WaveType != WaveType.WhiteNoise;
        bool needsCompact = false;

        for (int i = 0; i < ch.VoiceCount; i++)
        {
            ref Voice v = ref ch.VoicePool[i];
            if (!v.Active) { needsCompact = true; continue; }

            if (tonal)
            {
                if (ch.PortamentoOn && ch.PortamentoTime > 0 && v.PortamentoActive)
                {
                    double moved = v.Freq * Math.Pow(v.PortamentoMultiplier, samples);
                    double target = v.PortamentoTarget;
                    bool reached = (v.PortamentoMultiplier > 1.0 && moved >= target) ||
                                   (v.PortamentoMultiplier < 1.0 && moved <= target) ||
                                   Math.Abs(moved - target) <= 0.01;
                    v.Freq = reached ? target : moved;
                    if (reached) v.PortamentoActive = false;
                }

                // La fase exacta bajo LFO requeriría integrar la senoide. Para
                // el punto inicial audible basta conservar continuidad de fase
                // con la frecuencia base; CC, notas, ADSR y portamento sí quedan
                // reconstruidos en su estado temporal correcto.
                double cycles = v.Freq * ch.PitchMult * samples / SampleRate;
                v.Phase = (v.Phase + cycles) % 1.0;
            }

            AdvanceEnvelopeSilently(ref v, samples);
            if (!v.Active) needsCompact = true;
        }

        if (needsCompact) ch.CompactVoices();

        if (!tonal && ch.VoiceCount > 0 && ch.WaveType == WaveType.Noise &&
            ch.NoisePeriod > 0.0 && ch.NoisePeriod < double.MaxValue)
        {
            // Evitar recorrer cada pulso del LFSR durante minutos de seek. La
            // secuencia se reinicia de forma determinista y el temporizador se
            // coloca en la fase temporal correspondiente; al ser ruido, no hay
            // una fase tonal audible que preservar.
            double timer = ch.NoiseTimer - samples;
            double wraps = Math.Floor(Math.Max(0.0, -timer) / ch.NoisePeriod) + (timer <= 0.0 ? 1.0 : 0.0);
            ch.NoiseTimer = timer + wraps * ch.NoisePeriod;
            if (wraps > 0.0)
            {
                ch.Lfsr = 0x7FFFu;
                ch.NoiseOut = 0f;
            }
        }
    }

    private static void AdvanceEnvelopeSilently(ref Voice v, double samples)
    {
        double left = samples;
        while (left > 0.0001 && v.Active)
        {
            switch (v.EnvState)
            {
                case EnvState.Attack:
                {
                    double needed = Math.Max(0.0, (1.0 - v.EnvLevel) * v.AttackSamples);
                    if (left < needed)
                    {
                        v.EnvLevel += left / v.AttackSamples;
                        return;
                    }
                    v.EnvLevel = 1.0;
                    v.EnvState = EnvState.Decay;
                    left -= needed;
                    break;
                }
                case EnvState.Decay:
                {
                    double slope = (1.0 - v.SustainLevel) / v.DecaySamples;
                    if (slope <= 0.0)
                    {
                        v.EnvLevel = v.SustainLevel;
                        v.EnvState = v.SustainLevel <= 0.0 ? EnvState.Release : EnvState.Sustain;
                        break;
                    }
                    double needed = Math.Max(0.0, (v.EnvLevel - v.SustainLevel) / slope);
                    if (left < needed)
                    {
                        v.EnvLevel -= slope * left;
                        return;
                    }
                    v.EnvLevel = v.SustainLevel;
                    v.EnvState = v.SustainLevel <= 0.0 ? EnvState.Release : EnvState.Sustain;
                    left -= needed;
                    break;
                }
                case EnvState.Sustain:
                    return;
                case EnvState.Release:
                {
                    if (v.SostenutoHeld) return;
                    double needed = Math.Max(0.0, (v.EnvLevel - 0.01) * v.ReleaseSamples);
                    if (left < needed)
                    {
                        v.EnvLevel -= left / v.ReleaseSamples;
                        return;
                    }
                    v.EnvLevel = 0.0;
                    v.EnvState = EnvState.Off;
                    v.Active = false;
                    return;
                }
                default:
                    v.Active = false;
                    return;
            }
        }
    }

    private static void AdvanceDrumsSilently(Channel ch, double samples)
    {
        bool needsCompact = false;
        for (int i = 0; i < ch.DrumVoiceCount; i++)
        {
            ref DrumVoice drum = ref ch.DrumPool[i];
            if (!drum.Active) { needsCompact = true; continue; }

            double pitchSamples = Math.Min(samples, drum.PitchSamplesLeft);
            if (pitchSamples > 0.0)
            {
                drum.Frequency *= Math.Pow(drum.PitchMultiplier, pitchSamples);
                drum.PitchSamplesLeft = Math.Max(0, drum.PitchSamplesLeft - (int)Math.Ceiling(pitchSamples));
            }

            drum.Phase = (drum.Phase + drum.Frequency * samples / SampleRate) % 1.0;
            drum.TonalLevel = MathF.Max(0f, drum.TonalLevel - drum.TonalDecay * (float)samples);
            drum.NoiseLevel = MathF.Max(0f, drum.NoiseLevel - drum.NoiseDecay * (float)samples);

            if (drum.TonalLevel <= 0.0001f && drum.NoiseLevel <= 0.0001f)
            {
                drum.Active = false;
                needsCompact = true;
            }
        }

        if (needsCompact) ch.CompactDrumVoices();
    }

    public void Dispose()
    {
        StopRenderThread();
        try
        {
            _audioTrack?.Release();
            _audioTrack?.Dispose();
        }
        catch (ObjectDisposedException) { }
        _audioTrack = null;
    }

    private void StopRenderThread()
    {
        Thread? render;
        Thread? writer;

        lock (_playLock)
        {
            _running = false;
            render = _renderThread;
            writer = _writerThread;
            _renderThread = null;
            _writerThread = null;
        }

        // Pause/Flush antes del Join ayuda a desbloquear AudioTrack.Write().
        // Antes se hacía después del Join desde Stop(), dejando al escritor
        // potencialmente bloqueado mientras el seek reutilizaba la cola.
        try
        {
            _audioTrack?.Pause();
            _audioTrack?.Flush();
        }
        catch (ObjectDisposedException) { }
        catch (Java.Lang.IllegalStateException) { }

        _freeBlocks?.ReleaseSafely();
        _readyBlocks?.ReleaseSafely();

        if (render != null && render != Thread.CurrentThread)
            render.Join(2000);
        if (writer != null && writer != Thread.CurrentThread)
            writer.Join(2000);
    }

    // ── Hilo de render: genera bloques y los empuja al AudioTrack ────────

    private static Thread CreateAudioThread(ThreadStart action, string name) => new(action)
    {
        IsBackground = true,
        Name = name,
        Priority = System.Threading.ThreadPriority.Highest,
    };

    private static void SetUrgentAudioPriority()
    {
        try { Android.OS.Process.SetThreadPriority(Android.OS.ThreadPriority.UrgentAudio); }
        catch { }
    }

    private void ResetPcmQueue()
    {
        ResetPlaybackCaptureClock();
        _produceIndex = 0;
        _consumeIndex = 0;
        _producerFinished = false;
        _freeBlocks?.Dispose();
        _readyBlocks?.Dispose();
        _freeBlocks = new SemaphoreSlim(_ringBufferBlocks, _ringBufferBlocks);
        _readyBlocks = new SemaphoreSlim(0, _ringBufferBlocks);
    }


    private void ResetPlaybackCaptureClock()
    {
        lock (_playbackClockLock)
        {
            _playbackHeadLastRaw = 0;
            _playbackHeadWrapBase = 0;
            _playbackAnchorFrame = 0;
            _playbackAnchorTicks = 0;
        }
        Interlocked.Exchange(ref _mixCaptureFramesWritten, 0);
        Interlocked.Exchange(ref _renderCaptureFramesWritten, 0);
        _mixCaptureWritePos = 0;
        _meterCaptureWritePos = 0;
        _channelCaptureWritePos = 0;
        Array.Clear(_mixCapture, 0, _mixCapture.Length);
        Array.Clear(_meterCapture, 0, _meterCapture.Length);
        for (int i = 0; i < _chCapture.Length; i++)
            Array.Clear(_chCapture[i], 0, _chCapture[i].Length);
    }

    // Productor: solo sintetiza y llena el ring PCM. Puede adelantarse varios
    // bloques y absorber períodos en que Android favorece a otra aplicación.
    private void RenderLoop()
    {
        SetUrgentAudioPriority();
        while (_running)
        {
            try { _freeBlocks!.Wait(); }
            catch (ObjectDisposedException) { break; }
            if (!_running) break;

            float[] block = _pcmRing[_produceIndex];
            bool finished = RenderBlock(block, _blockFrames);
            _produceIndex = (_produceIndex + 1) % _pcmRing.Length;
            _readyBlocks!.Release();

            if (finished)
            {
                _producerFinished = true;
                break;
            }
        }
    }

    // Consumidor: no ejecuta DSP; únicamente mantiene AudioTrack alimentado.
    private void AudioWriterLoop()
    {
        SetUrgentAudioPriority();
        var track = _audioTrack;
        if (track == null) return;

        while (_running)
        {
            try { _readyBlocks!.Wait(); }
            catch (ObjectDisposedException) { break; }
            if (!_running) break;

            float[] block = _pcmRing[_consumeIndex];
            int written = track.Write(block, 0, block.Length, WriteMode.Blocking);
            if (written < 0)
            {
                _running = false;
                _freeBlocks!.ReleaseSafely();
                break;
            }

            // Publicar al visualizador desde el consumidor, después del write
            // bloqueante, no desde RenderBlock. El productor puede adelantarse
            // varios bloques en el ring PCM; publicar allí hacía que el FFT
            // recibiera ráfagas de audio futuro y luego periodos sin cambios.
            if (written > 0)
                PublishMixCapture(block, written);

            _consumeIndex = (_consumeIndex + 1) % _pcmRing.Length;
            _freeBlocks!.Release();

            if (_producerFinished && _readyBlocks.CurrentCount == 0)
            {
                _running = false;
                break;
            }
        }
    }

    private bool IsMixCaptureRequested()
    {
        long last = Volatile.Read(ref _lastMixCaptureRequestMs);
        return last > 0 && Environment.TickCount64 - last <= ChannelCaptureKeepAliveMs;
    }

    private void PublishMixCapture(float[] interleaved, int writtenSamples)
    {
        int sampleCount = Math.Min(writtenSamples, interleaved.Length);
        int frames = sampleCount / NumChannelsAudio;
        long firstFrame = Interlocked.Read(ref _mixCaptureFramesWritten);

        for (int n = 0; n < frames; n++)
        {
            int idx = n * NumChannelsAudio;
            int ringIndex = (int)((firstFrame + n) & (CaptureLen - 1));
            _mixCapture[ringIndex] = (interleaved[idx] + interleaved[idx + 1]) * 0.5f;
        }

        long total = firstFrame + frames;
        Volatile.Write(ref _mixCaptureWritePos, (int)(total & (CaptureLen - 1)));
        Interlocked.Exchange(ref _mixCaptureFramesWritten, total);
    }

    /// <summary>
    /// Genera un bloque de audio en <paramref name="buffer"/> (frameCount
    /// frames × 2 canales, intercalado). Devuelve true si la canción
    /// terminó durante este bloque.
    /// Equivalente exacto al Read() de la versión NAudio del desktop,
    /// solo que acá "empujamos" en vez de que nos "llamen".
    /// </summary>
    private bool RenderBlock(float[] buffer, int frameCount)
    {
        // Único punto donde una reproducción activa cambia WaveType. Así no
        // puede ocurrir ClearVoices mientras este mismo bloque recorre voces.
        ApplyPendingWaveTypes();

        float speed = Math.Max(0.01f, Speed);
        int renderedFrames = 0;
        long captureBaseFrame = Interlocked.Read(ref _renderCaptureFramesWritten);
        int channelCapturePos = (int)(captureBaseFrame & (CaptureLen - 1));
        int meterCapturePos = channelCapturePos;
        long lastCaptureRequest = Volatile.Read(ref _lastChannelCaptureRequestMs);
        long nowMs = Environment.TickCount64;
        bool captureChannels = lastCaptureRequest > 0 && nowMs - lastCaptureRequest <= ChannelCaptureKeepAliveMs;
        long lastMixRequest = Volatile.Read(ref _lastMixCaptureRequestMs);
        bool captureMix = lastMixRequest > 0 && nowMs - lastMixRequest <= ChannelCaptureKeepAliveMs;

        // Render segmentado por eventos: fuera del loop por muestra se aplican
        // fronteras MIDI y se calcula cuántos frames pueden sintetizarse con un
        // estado de control inmutable. Esto elimina de cada muestra las ramas de
        // TimeLeft/Index/fin de pista que antes se repetían por cada canal.
        while (renderedFrames < frameCount)
        {
            if (_virtualSample >= _loopEndSample)
            {
                IsFinished = true;
                OnStepAdvanced?.Invoke();
                Array.Clear(buffer, renderedFrames * NumChannelsAudio,
                    buffer.Length - renderedFrames * NumChannelsAudio);
                break;
            }

            ProcessEventBoundary();

            int remainingFrames = frameCount - renderedFrames;
            long loopFramesLeft = (long)Math.Ceiling((_loopEndSample - _virtualSample) / speed);
            if (loopFramesLeft <= 0)
                continue;

            int segmentFrames = ComputeFramesToNextEvent(speed, remainingFrames);
            if (loopFramesLeft < segmentFrames)
                segmentFrames = (int)Math.Max(1, loopFramesLeft);

            int segmentEnd = renderedFrames + segmentFrames;

            for (int n = renderedFrames; n < segmentEnd; n++)
            {
                float mixL = 0f, mixR = 0f;

                for (int ci = 0; ci < _channelCount; ci++)
                {
                    Channel ch = _channels[ci];
                    if (ch.PlaybackTrackCount == 0)
                    {
                        if (captureChannels) _chCapture[ci][channelCapturePos] = 0f;
                        continue;
                    }

                    bool hasActiveAudio = ch.WaveType == WaveType.ChipDrums
                        ? ch.DrumVoiceCount > 0
                        : ch.VoiceCount > 0;

                    if (!hasActiveAudio)
                    {
                        if (ch.ModDepth > 0f)
                        {
                            ch.LfoPhase += ch.LfoRate * InvSampleRate;
                            if (ch.LfoPhase >= 1.0) ch.LfoPhase -= 1.0;
                        }

                        if (captureChannels) _chCapture[ci][channelCapturePos] = 0f;
                        continue;
                    }

                    float lfoValue = 0f;
                    if (ch.ModDepth > 0f)
                    {
                        ch.LfoPhase += ch.LfoRate * InvSampleRate;
                        if (ch.LfoPhase >= 1.0) ch.LfoPhase -= 1.0;
                        int tableIndex = ((int)(ch.LfoPhase * LfoTableSize)) & LfoTableMask;
                        float depthCtl = (ch.VibratoDepthController - (64f / 127f)) * (127f / 63f);
                        float effectiveDepth = Math.Clamp(ch.ModDepth + depthCtl, 0f, 1f);
                        float delayCtl = Math.Max(0f, (ch.VibratoDelayController - (64f / 127f)) * (127f / 63f));
                        double delaySamples = delayCtl * 2.0 * SampleRate;
                        double delayGain = delaySamples <= 0 ? 1.0 : Math.Clamp((ch.VibratoAgeSamples - delaySamples) / (0.08 * SampleRate), 0.0, 1.0);
                        lfoValue = LfoSineTable[tableIndex] * effectiveDepth * 0.05946f * (float)delayGain;
                        ch.VibratoAgeSamples++;
                    }

                    float ccGain = ch.CcVolume * ch.CcExpression * ch.Brightness * ch.SoftPedalGain * ch.UserGain;
                    float chSample = ch.WaveType switch
                    {
                        WaveType.ChipDrums => SynthesizeDrumKit(ch, ccGain),
                        WaveType.Noise or WaveType.WhiteNoise => SynthesizeNoise(ch, ccGain),
                        _ => SynthesizeTonal(ch, ccGain, lfoValue),
                    };

                    if (captureChannels) _chCapture[ci][channelCapturePos] = chSample;

                    mixL += chSample * ch.PanGainL;
                    mixR += chSample * ch.PanGainR;
                }

                int outIdx = n * NumChannelsAudio;
                float drivenL = mixL * _tanhDrive;
                float drivenR = mixR * _tanhDrive;

                if (captureMix)
                {
                    _meterCapture[meterCapturePos] = (drivenL + drivenR) * 0.5f;
                    meterCapturePos++;
                    if (meterCapturePos >= CaptureLen) meterCapturePos = 0;
                }

                // Se guarda la señal pre-limitador. El clip se aplica en una
                // pasada vectorizada al terminar la síntesis del bloque.
                buffer[outIdx] = drivenL;
                buffer[outIdx + 1] = drivenR;

                if (captureChannels)
                {
                    channelCapturePos++;
                    if (channelCapturePos >= CaptureLen) channelCapturePos = 0;
                }
            }

            double virtualAdvance = segmentFrames * (double)speed;
            for (int ci = 0; ci < _channelCount; ci++)
            {
                Channel ch = _channels[ci];
                if (ch.TimeLeft < double.MaxValue)
                {
                    ch.TimeLeft -= virtualAdvance;
                    if (ch.TimeLeft > -0.0001 && ch.TimeLeft < 0.0001)
                        ch.TimeLeft = 0;
                }
            }

            _virtualSample += virtualAdvance;
            _globalSample += segmentFrames;
            renderedFrames = segmentEnd;
        }

        long captureEndFrame = captureBaseFrame + renderedFrames;
        Interlocked.Exchange(ref _renderCaptureFramesWritten, captureEndFrame);
        if (captureChannels) _channelCaptureWritePos = (int)(captureEndFrame & (CaptureLen - 1));
        if (captureMix) _meterCaptureWritePos = (int)(captureEndFrame & (CaptureLen - 1));

        if (renderedFrames > 0)
        {
            SoftClipInterleaved(buffer, renderedFrames * NumChannelsAudio);
            _eq.ProcessInterleaved(buffer, 0, renderedFrames);
            _bassRestoration.ProcessInterleaved(buffer, 0, renderedFrames);
        }

        // MIX se publica en AudioWriterLoop, alineado con el AudioTrack.
        // RenderBlock puede adelantarse varios bloques y no representa el reloj
        // audible real. El meter pre-limitador y los canales siguen siendo datos
        // de síntesis, pero el espectro MIX ya no consume audio futuro en ráfagas.
        return renderedFrames < frameCount;
    }

    /// <summary>
    /// Aplica como máximo un MidiStep vencido por canal. Conservar un evento
    /// por muestra mantiene la protección previa contra timestamps densos: si
    /// hay varios steps con duración cero, ComputeFramesToNextEvent devuelve un
    /// segmento de un frame y el siguiente step se procesa en la muestra siguiente.
    /// </summary>
    private void ProcessEventBoundary()
    {
        const double Epsilon = 0.0001;

        for (int ci = 0; ci < _channelCount; ci++)
        {
            Channel ch = _channels[ci];
            if (ch.PlaybackTrackCount == 0 || ch.TimeLeft > Epsilon)
                continue;

            if (ch.Index >= ch.PlaybackTrackCount)
            {
                ReleaseFinishedChannel(ch);
                ch.TimeLeft = double.MaxValue;
            }
            else
            {
                AdvanceStep(ch, ch.PlaybackTrack[ch.Index]);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ComputeFramesToNextEvent(float speed, int maxFrames)
    {
        double nearestVirtualSamples = double.MaxValue;

        for (int ci = 0; ci < _channelCount; ci++)
        {
            double left = _channels[ci].TimeLeft;
            if (left < nearestVirtualSamples)
                nearestVirtualSamples = left;
        }

        if (nearestVirtualSamples == double.MaxValue)
            return maxFrames;

        // Ceil garantiza que no se aplique un evento antes de su frontera.
        // Un valor <= 0 fuerza un frame, preservando el reparto de eventos de
        // duración cero entre muestras consecutivas.
        int frames = nearestVirtualSamples <= 0.0001
            ? 1
            : (int)Math.Ceiling(nearestVirtualSamples / speed);

        return Math.Clamp(frames, 1, maxFrames);
    }

    private static void SoftClipInterleaved(float[] buffer, int sampleCount)
    {
        int i = 0;

        if (Vector.IsHardwareAccelerated && sampleCount >= Vector<float>.Count)
        {
            int width = Vector<float>.Count;
            var c27 = new Vector<float>(27f);
            var c9 = new Vector<float>(9f);
            var min = new Vector<float>(-1f);
            var max = new Vector<float>(1f);
            int vectorEnd = sampleCount - (sampleCount % width);

            for (; i < vectorEnd; i += width)
            {
                var x = new Vector<float>(buffer, i);
                var x2 = x * x;
                var y = x * (c27 + x2) / (c27 + c9 * x2);
                y = Vector.Min(max, Vector.Max(min, y));
                y.CopyTo(buffer, i);
            }
        }

        for (; i < sampleCount; i++)
            buffer[i] = FastSoftClip(buffer[i]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float FastSoftClip(float x)
    {
        // Aproximación racional suave de tanh. Mucho más barata que Math.Tanh,
        // monótona y acotada aproximadamente a [-1, 1] en el rango útil.
        float x2 = x * x;
        float y = x * (27f + x2) / (27f + 9f * x2);
        return Math.Clamp(y, -1f, 1f);
    }

    private static float[] BuildLfoSineTable()
    {
        var table = new float[LfoTableSize];
        float step = 2f * MathF.PI / LfoTableSize;
        for (int i = 0; i < table.Length; i++)
            table[i] = MathF.Sin(i * step);
        return table;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float LookupSine(double phase)
    {
        double scaled = phase * LfoTableSize;
        int index = (int)scaled;
        float fraction = (float)(scaled - index);
        int i0 = index & LfoTableMask;
        int i1 = (i0 + 1) & LfoTableMask;
        float a = LfoSineTable[i0];
        return a + (LfoSineTable[i1] - a) * fraction;
    }

    // ── AdvanceStep ───────────────────────────────────────────────────────

    private void AdvanceStep(Channel ch, MidiStep step)
    {
        ch.PitchMult = step.PitchMult;
        ch.CcVolume = step.Volume;
        if (ch.CcPan != step.Pan) ch.SetPan(step.Pan);
        ch.CcExpression = step.Expression;
        ch.ModDepth = step.Modulation;
        ch.Brightness = step.Brightness;
        ch.AfterTouch = step.AfterTouch;
        ch.LfoRate = step.LfoRate;
        ch.SoundVariation = step.SoundVariation;
        ch.SetResonance(step.Resonance);
        ch.LegatoOn = step.Legato;
        ch.SoftPedalGain = step.SoftPedal >= 0.5f ? 0.72f : 1f;
        ch.VibratoDepthController = step.VibratoDepth;
        ch.VibratoDelayController = step.VibratoDelay;
        ch.PortamentoSourceNote = step.PortamentoSourceNote;

        if (step.PortamentoTime >= 0f) ch.PortamentoTime = step.PortamentoTime;
        ch.PortamentoOn = step.PortamentoOn;

        // Los CC de ataque/liberación suelen repetirse en cada MidiStep. En MIDIs
        // rápidos como Spider Dance, recalcular dos Math.Pow por frontera crea
        // picos de CPU aunque el valor no haya cambiado. Solo convertir cuando
        // cambia el parámetro crudo del stream.
        float attackParameter = step.Attack;
        float decayParameter = step.Decay;
        float releaseParameter = step.Release;
        bool attackChanged = attackParameter != ch.LastAttackParameter;
        bool decayChanged = decayParameter != ch.LastDecayParameter;
        bool releaseChanged = releaseParameter != ch.LastReleaseParameter;

        double nextAttack = ch.Attack;
        double nextDecay = ch.Decay;
        double nextRelease = ch.Release;
        if (attackChanged)
        {
            nextAttack = attackParameter >= 0f ? ScaleAttack(attackParameter) : ch.DefaultAttack;
            ch.LastAttackParameter = attackParameter;
        }
        if (decayChanged)
        {
            nextDecay = decayParameter >= 0f ? ScaleDecay(decayParameter) : ch.DefaultDecay;
            ch.LastDecayParameter = decayParameter;
        }
        if (releaseChanged)
        {
            nextRelease = releaseParameter >= 0f ? ScaleRelease(releaseParameter) : ch.DefaultRelease;
            ch.LastReleaseParameter = releaseParameter;
        }

        if (attackChanged || decayChanged || releaseChanged)
        {
            ch.Attack = nextAttack;
            ch.Decay = nextDecay;
            ch.Release = nextRelease;
            ch.RebuildAdsrSamples(SampleRate);
        }

        bool prevSostenuto = ch.SostenutoActive;
        ch.SostenutoActive = step.Sostenuto;

        if (!prevSostenuto && step.Sostenuto)
        {
            // Sostenuto captura únicamente las voces cuyas teclas siguen
            // abajo en el instante de pisar el pedal.
            for (int i = 0; i < ch.VoiceCount; i++)
                ch.VoicePool[i].SostenutoHeld = ch.VoicePool[i].KeyDown;
        }
        else if (prevSostenuto && !step.Sostenuto)
        {
            for (int i = 0; i < ch.VoiceCount; i++)
            {
                ref var v = ref ch.VoicePool[i];
                if (!v.SostenutoHeld) continue;
                v.SostenutoHeld = false;
                if (!v.KeyDown && !step.Sustain)
                    v.EnvState = EnvState.Release;
            }
        }

        bool prevSustain = ch.CcSustain;
        ch.CcSustain = step.Sustain;
        if (prevSustain && !step.Sustain)
        {
            for (int i = 0; i < ch.VoiceCount; i++)
            {
                ref var v = ref ch.VoicePool[i];
                if (!v.KeyDown && !v.SostenutoHeld)
                    v.EnvState = EnvState.Release;
            }
        }

        // Primero procesar NoteOff y después NoteOn. Esto respeta el caso
        // común de retrigger de la misma nota en el mismo tick.
        ProcessNoteOffs(ch, step);

        // Poly-aftertouch se guarda como fotografía para la UI. Actualizar
        // voces existentes por frecuencia no crea voces ni reinicia ADSR.
        bool hasPolyAfterTouch = false;
        for (int noteIndex = 0; noteIndex < step.NoteCount; noteIndex++)
        {
            if (step.GetPolyAT(noteIndex) > 0f)
            {
                hasPolyAfterTouch = true;
                break;
            }
        }
        if (step.NoteCount > 0 && hasPolyAfterTouch)
        {
            for (int i = 0; i < ch.VoiceCount; i++)
            {
                ref var v = ref ch.VoicePool[i];
                for (int si = 0; si < step.NoteCount; si++)
                {
                    if (Math.Abs(v.Freq - step.GetFreq(si)) < 0.5f)
                    {
                        v.PolyAfterTouch = step.GetPolyAT(si);
                        break;
                    }
                }
            }
        }

        if (ch.WaveType == WaveType.ChipDrums)
            TriggerDrumNoteOns(ch, step);
        else if (ch.WaveType == WaveType.Noise || ch.WaveType == WaveType.WhiteNoise)
            TriggerNoiseNoteOns(ch, step);
        else
            TriggerTonalNoteOns(ch, step);

        ch.TimeLeft = (step.DurationMs / 1000.0) * SampleRate;
        ch.Index++;
        if (ch.Index > ch.PlaybackTrackCount) ch.Index = ch.PlaybackTrackCount;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double ScaleAttack(float norm)
        => 0.001 * Math.Pow(2000.0, Math.Clamp(norm, 0f, 1f));
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double ScaleRelease(float norm)
        => 0.005 * Math.Pow(800.0, Math.Clamp(norm, 0f, 1f));
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double ScaleDecay(float norm)
        => 0.005 * Math.Pow(800.0, Math.Clamp(norm, 0f, 1f));


    // ── Voice triggering ──────────────────────────────────────────────────

    private static void ProcessNoteOffs(Channel ch, MidiStep step)
    {
        for (int oi = 0; oi < step.NoteOffCount; oi++)
        {
            int midiNote = step.GetOffNote(oi);
            for (int vi = 0; vi < ch.VoiceCount; vi++)
            {
                ref var voice = ref ch.VoicePool[vi];
                if (!voice.Active || voice.MidiNote != midiNote || !voice.KeyDown)
                    continue;

                voice.KeyDown = false;
                if (!ch.CcSustain && !voice.SostenutoHeld)
                    voice.EnvState = EnvState.Release;

                // Una pareja NoteOff corresponde a una instancia NoteOn.
                break;
            }
        }
    }

    private void TriggerTonalNoteOns(Channel ch, MidiStep step)
    {
        for (int ni = 0; ni < step.NoteOnCount; ni++)
        {
            int midiNote = step.GetOnNote(ni);
            ch.VibratoAgeSamples = 0;
            float f = MidiNoteToFrequency(midiNote);
            float vel = step.GetOnVelocity(ni);

            // CC68 (legato) sólo puede reutilizar una voz cuando el pasaje es
            // inequívocamente monofónico. Aplicarlo con acordes o con varias
            // teclas sostenidas colapsaba toda la polifonía del canal.
            if (ch.LegatoOn && step.NoteOnCount == 1 && ch.VoiceCount > 0)
            {
                int legatoIndex = -1;
                int heldVoiceCount = 0;
                for (int i = 0; i < ch.VoiceCount; i++)
                {
                    ref var candidate = ref ch.VoicePool[i];
                    if (candidate.Active && candidate.KeyDown &&
                        candidate.EnvState != EnvState.Release && candidate.EnvState != EnvState.Off)
                    {
                        heldVoiceCount++;
                        legatoIndex = i;
                        if (heldVoiceCount > 1) break;
                    }
                }

                if (heldVoiceCount == 1 && legatoIndex >= 0)
                {
                    ref var legVoice = ref ch.VoicePool[legatoIndex];
                    legVoice.MidiNote = midiNote;
                    legVoice.KeyDown = true;
                    if (ch.PortamentoOn && ch.PortamentoTime > 0)
                    {
                        ch.PortamentoTarget = f;
                        ConfigurePortamento(ref legVoice, f, ch.PortamentoTime);
                    }
                    else
                    {
                        legVoice.Freq = f;
                        legVoice.PortamentoTarget = f;
                        legVoice.PortamentoActive = false;
                    }

                    legVoice.Velocity = vel;
                    ch.PortamentoFreq = f;
                    ch.PortamentoTarget = f;
                    continue;
                }
            }

            bool explicitPortamentoSource = ch.PortamentoSourceNote >= 0;
            double startFreq;
            if (explicitPortamentoSource)
            {
                startFreq = MidiNoteToFrequency(ch.PortamentoSourceNote);
                ch.PortamentoSourceNote = -1;
                ch.PortamentoTarget = f;
            }
            else if (ch.PortamentoOn && ch.PortamentoFreq > 0)
            {
                startFreq = ch.PortamentoFreq;
                ch.PortamentoTarget = f;
            }
            else
            {
                startFreq = f;
                ch.PortamentoFreq = f;
                ch.PortamentoTarget = f;
            }

            var newVoice = Voice.Create(midiNote, f, startFreq, vel, 0f);
            float glideSeconds = ch.PortamentoOn ? ch.PortamentoTime : (explicitPortamentoSource ? 0.005f : 0f);
            if (glideSeconds > 0 && Math.Abs(startFreq - f) > 0.01)
                ConfigurePortamento(ref newVoice, f, glideSeconds);
            ch.StampAdsr(ref newVoice);
            ch.AddVoice(newVoice);
        }
    }

    private static float[] BuildMidiFrequencyTable()
    {
        var table = new float[128];
        for (int note = 0; note < table.Length; note++)
            table[note] = 440f * MathF.Pow(2f, (note - 69) / 12f);
        return table;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float MidiNoteToFrequency(int midiNote)
        => MidiFrequencyTable[Math.Clamp(midiNote, 0, 127)];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ConfigurePortamento(ref Voice voice, double target, float seconds)
    {
        voice.PortamentoTarget = target;
        if (seconds <= 0f || target <= 0.0 || voice.Freq <= 0.0 || Math.Abs(voice.Freq - target) <= 0.01)
        {
            voice.Freq = target;
            voice.PortamentoMultiplier = 1.0;
            voice.PortamentoActive = false;
            return;
        }

        voice.PortamentoMultiplier = Math.Pow(
            target / Math.Max(voice.Freq, 1.0),
            1.0 / (seconds * SampleRate));
        voice.PortamentoActive = true;
    }

    private void TriggerNoiseNoteOns(Channel ch, MidiStep step)
    {
        for (int ni = 0; ni < step.NoteOnCount; ni++)
        {
            int midiNote = step.GetOnNote(ni);
            float freq = MidiNoteToFrequency(midiNote);
            float velocity = step.GetOnVelocity(ni);

            SetNoiseParamsForDrumNote(ch, midiNote);
            ch.Lfsr = 0x7FFFu;
            ch.NoiseTimer = ch.NoisePeriod;
            ch.NoiseOut = 0f;
            ch.ClearVoices();

            var nv = Voice.Create(midiNote, freq, freq, velocity, 0f);
            ch.StampAdsr(ref nv);
            ch.AddVoice(nv);
        }
    }

    private void TriggerDrumNoteOns(Channel ch, MidiStep step)
    {
        for (int ni = 0; ni < step.NoteOnCount; ni++)
        {
            int midiNote = step.GetOnNote(ni);
            float velocity = step.GetOnVelocity(ni);
            ch.AddDrumVoice(CreateDrumVoice(midiNote, velocity));
        }
    }

    private static DrumVoice CreateDrumVoice(int midiNote, float velocity)
    {
        // Valores base: pequeño click tonal + ruido corto. El switch adapta
        // la receta a familias GM de percusión.
        float startHz = 180f, endHz = 100f, pitchMs = 45f;
        float tonal = 0.55f, noise = 0.35f;
        float tonalMs = 90f, noiseMs = 100f;
        float duty = 0.5f;
        bool whiteNoise = true;
        double noisePeriod = 1.0;

        switch (midiNote)
        {
            // Kick: square con descenso fuerte. Un poco de ruido solo como click.
            case 35:
            case 36:
                startHz = midiNote == 36 ? 210f : 180f;
                endHz = 42f; pitchMs = 90f;
                tonal = 1.0f; noise = 0.06f;
                tonalMs = 180f; noiseMs = 20f;
                duty = 0.5f;
                break;

            // Snare / clap: cuerpo tonal corto + arena blanca.
            case 38:
            case 40:
                startHz = midiNote == 38 ? 230f : 190f;
                endHz = 125f; pitchMs = 45f;
                tonal = 0.62f; noise = 0.78f;
                tonalMs = 85f; noiseMs = midiNote == 38 ? 145f : 105f;
                duty = 0.5f;
                break;
            case 39:
                startHz = 260f; endHz = 170f; pitchMs = 25f;
                tonal = 0.42f; noise = 0.85f;
                tonalMs = 50f; noiseMs = 80f;
                duty = 0.25f;
                break;

            // Closed / pedal / open hats: white noise puro.
            case 42:
            case 44:
                tonal = 0f; noise = 1.0f;
                noiseMs = midiNote == 42 ? 32f : 45f;
                break;
            case 46:
                tonal = 0f; noise = 0.95f;
                noiseMs = 260f;
                break;

            // Toms: pulse con barrido descendente y una pizca de ruido.
            case 41: case 43: case 45: case 47: case 48: case 50:
                startHz = midiNote switch
                {
                    41 => 115f, 43 => 135f, 45 => 155f,
                    47 => 180f, 48 => 205f, _ => 235f,
                };
                endHz = startHz * 0.58f; pitchMs = 90f;
                tonal = 0.9f; noise = 0.10f;
                tonalMs = 170f; noiseMs = 55f;
                duty = 0.5f;
                break;

            // Cymbals: ruido blanco largo con una capa pulse muy pequeña.
            case 49: case 51: case 52: case 53: case 55: case 57: case 59:
                startHz = 420f; endHz = 360f; pitchMs = 35f;
                tonal = 0.12f; noise = 0.95f;
                tonalMs = 80f;
                noiseMs = midiNote is 49 or 57 ? 520f : 300f;
                duty = 0.125f;
                break;

            // Rimshot / clave / blocks: ruido periódico más seco.
            case 31: case 37: case 56: case 60: case 61: case 62: case 63: case 64:
                startHz = 300f + (midiNote - 31) * 8f;
                endHz = startHz * 0.8f; pitchMs = 18f;
                tonal = 0.58f; noise = 0.28f;
                tonalMs = 45f; noiseMs = 35f;
                duty = 0.25f;
                whiteNoise = false;
                noisePeriod = 1.4;
                break;

            // Shakers/maracas/cabasa: ruido corto sin cuerpo tonal.
            case 69: case 70: case 75: case 82:
                tonal = 0f; noise = 0.9f;
                noiseMs = 55f;
                break;
        }

        int pitchSamples = Math.Max(1, (int)(pitchMs * SampleRate / 1000f));
        double pitchMultiplier = startHz > 0f && endHz > 0f
            ? Math.Pow(endHz / startHz, 1.0 / pitchSamples)
            : 1.0;

        return new DrumVoice
        {
            Active = true,
            Phase = 0.0,
            Frequency = startHz,
            PitchMultiplier = pitchMultiplier,
            PitchSamplesLeft = pitchSamples,
            TonalLevel = tonal,
            TonalDecay = tonal <= 0f ? 0f : tonal / Math.Max(1f, tonalMs * SampleRate / 1000f),
            NoiseLevel = noise,
            NoiseDecay = noise <= 0f ? 0f : noise / Math.Max(1f, noiseMs * SampleRate / 1000f),
            Velocity = velocity,
            Duty = duty,
            WhiteNoise = whiteNoise,
            Lfsr = 0x7FFFu,
            NoiseTimer = noisePeriod,
            NoisePeriod = noisePeriod,
            NoiseOut = 0f,
        };
    }

    private float SynthesizeDrumKit(Channel ch, float ccGain)
    {
        float sample = 0f;
        bool needsCompact = false;

        for (int i = 0; i < ch.DrumVoiceCount; i++)
        {
            ref var drum = ref ch.DrumPool[i];
            if (!drum.Active) { needsCompact = true; continue; }

            float tonalSample = 0f;
            if (drum.TonalLevel > 0f)
            {
                drum.Phase += drum.Frequency * InvSampleRate;
                if (drum.Phase >= 1.0) drum.Phase -= 1.0;
                tonalSample = drum.Phase < drum.Duty ? 1f : -1f;
                drum.TonalLevel = MathF.Max(0f, drum.TonalLevel - drum.TonalDecay);
            }

            float noiseSample = 0f;
            if (drum.NoiseLevel > 0f)
            {
                noiseSample = drum.WhiteNoise ? NextNoise() : GenerateDrumNoise(ref drum);
                drum.NoiseLevel = MathF.Max(0f, drum.NoiseLevel - drum.NoiseDecay);
            }

            if (drum.PitchSamplesLeft > 0)
            {
                drum.Frequency *= drum.PitchMultiplier;
                drum.PitchSamplesLeft--;
            }

            sample += (tonalSample * drum.TonalLevel + noiseSample * drum.NoiseLevel) * drum.Velocity;

            if (drum.TonalLevel <= 0.0001f && drum.NoiseLevel <= 0.0001f)
            {
                drum.Active = false;
                needsCompact = true;
            }
        }

        if (needsCompact) ch.CompactDrumVoices();
        return sample * ch.Volume * ccGain;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float GenerateDrumNoise(ref DrumVoice drum)
    {
        drum.NoiseTimer--;
        if (drum.NoiseTimer <= 0.0)
        {
            drum.NoiseTimer += drum.NoisePeriod;
            uint xorBit = (drum.Lfsr & 1u) ^ ((drum.Lfsr >> 1) & 1u);
            drum.Lfsr = (drum.Lfsr >> 1) | (xorBit << 14);
            drum.NoiseOut = (drum.Lfsr & 1u) == 0 ? 1f : -1f;
        }
        return drum.NoiseOut;
    }

    // ── Síntesis ──────────────────────────────────────────────────────────

    private float SynthesizeNoise(Channel ch, float ccGain)
    {
        if (ch.VoiceCount == 0) return 0f;

        ref var v = ref ch.VoicePool[0];
        if (!v.Active)
        {
            ch.VoiceCount = 0;
            return 0f;
        }

        float noiseOut = ch.WaveType == WaveType.WhiteNoise
            ? NextNoise()
            : GenerateDMGNoise(ch);

        float result = noiseOut * ch.Volume * (float)v.EnvLevel * v.Velocity * ccGain;

        TickEnvelope(ref v);
        if (!v.Active) ch.VoiceCount = 0;

        return result;
    }

    private float SynthesizeTonal(Channel ch, float ccGain, float lfoValue)
    {
        float sample = 0f;
        bool hasPortamento = ch.PortamentoOn && ch.PortamentoTime > 0f;
        bool needsCompact = false;

        // Valores comunes al canal: antes se leían y multiplicaban otra vez por
        // cada voz en cada muestra. En acordes de seis voces eso era puro peaje.
        double phaseScale = ch.PitchMult * (1.0 + lfoValue) * InvSampleRate;
        float channelGain = ch.Volume * ccGain;
        float variationBlend = ch.SoundVariation * 0.5f;
        bool hasVariation = variationBlend > 0.0001f;
        WaveType waveType = ch.WaveType;
        double duty = ch.EffectiveDuty;
        float channelAfterTouch = ch.AfterTouch;
        double sustain = ch.Sustain;

        for (int i = 0; i < ch.VoiceCount; i++)
        {
            ref Voice v = ref ch.VoicePool[i];
            if (!v.Active)
            {
                needsCompact = true;
                continue;
            }

            if (hasPortamento && v.PortamentoActive)
            {
                v.Freq *= v.PortamentoMultiplier;
                double target = v.PortamentoTarget;
                if ((v.PortamentoMultiplier > 1.0 && v.Freq >= target) ||
                    (v.PortamentoMultiplier < 1.0 && v.Freq <= target) ||
                    Math.Abs(v.Freq - target) <= 0.01)
                {
                    v.Freq = target;
                    v.PortamentoActive = false;
                }
            }

            double phase = v.Phase + v.Freq * phaseScale;
            if (phase >= 1.0) phase -= 1.0;
            v.Phase = phase;

            float wave = waveType switch
            {
                WaveType.Square => phase < duty ? 1f : -1f,
                WaveType.Triangle => 1f - 4f * MathF.Abs((float)phase - 0.5f),
                WaveType.Saw => phase < 0.5 ? (float)(phase * 2.0) : (float)(phase * 2.0 - 2.0),
                WaveType.Sine => LookupSine(phase),
                WaveType.Pulse25 => phase < 0.25 ? 1f : -1f,
                WaveType.Pulse12 => phase < 0.125 ? 1f : -1f,
                WaveType.WhiteNoise => NextNoise(),
                _ => 0f
            };

            if (hasVariation && waveType != WaveType.Sine)
            {
                float sine = LookupSine(phase);
                wave += (sine - wave) * variationBlend;
            }

            if (v.EnvState == EnvState.Sustain)
            {
                float atValue = v.PolyAfterTouch > 0f ? v.PolyAfterTouch : channelAfterTouch;
                if (atValue > 0f)
                    v.EnvLevel = sustain + atValue * (1.0 - sustain) * 0.3;
            }

            TickEnvelope(ref v);
            sample += wave * channelGain * (float)v.EnvLevel * v.Velocity;

            if (!v.Active) needsCompact = true;
        }

        if (needsCompact) ch.CompactVoices();
        return sample;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void TickEnvelope(ref Voice v)
    {
        switch (v.EnvState)
        {
            case EnvState.Attack:
                v.EnvLevel += v.AttackStep;
                if (v.EnvLevel >= 1.0) { v.EnvLevel = 1.0; v.EnvState = EnvState.Decay; }
                break;

            case EnvState.Decay:
                v.EnvLevel -= v.DecayStep;
                if (v.EnvLevel <= v.SustainLevel)
                {
                    v.EnvLevel = v.SustainLevel;
                    v.EnvState = v.SustainLevel <= 0.0 ? EnvState.Release : EnvState.Sustain;
                }
                break;

            case EnvState.Sustain:
                break;

            case EnvState.Release:
                if (v.SostenutoHeld) break;
                v.EnvLevel -= v.ReleaseStep;
                if (v.EnvLevel <= 0.01) { v.EnvLevel = 0; v.EnvState = EnvState.Off; v.Active = false; }
                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float NextNoise()
    {
        _rngState ^= _rngState << 13;
        _rngState ^= _rngState >> 17;
        _rngState ^= _rngState << 5;
        return (_rngState * (1f / uint.MaxValue)) * 2f - 1f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float GenerateDMGNoise(Channel ch)
    {
        ch.NoiseTimer--;
        if (ch.NoiseTimer <= 0)
        {
            ch.NoiseTimer += ch.NoisePeriod;
            uint xorBit = (ch.Lfsr & 1u) ^ ((ch.Lfsr >> 1) & 1u);
            ch.Lfsr = (ch.Lfsr >> 1) | (xorBit << 14);
            if (ch.NoiseShort)
                ch.Lfsr = (ch.Lfsr & ~(1u << 6)) | (xorBit << 6);
            ch.NoiseOut = (ch.Lfsr & 1u) == 0 ? 1f : -1f;
        }
        return ch.NoiseOut;
    }

    private static void ApplyNoiseAdsrDefaults(Channel ch)
    {
        if (ch.WaveType != WaveType.Noise && ch.WaveType != WaveType.WhiteNoise && ch.WaveType != WaveType.ChipDrums) return;
        ch.DefaultAttack = ch.Attack = 0.001;
        ch.DefaultDecay = ch.Decay = 0.08;
        ch.DefaultSustain = ch.Sustain = 0.0;
        ch.DefaultRelease = ch.Release = 0.05;
    }

    private static void SetNoiseParamsForDrumNote(Channel ch, int midiNote)
    {
        ch.Attack = 0.001; ch.Decay = 0.045; ch.Sustain = 0.0;
        ch.Release = 0.020; ch.NoisePeriod = 2.0; ch.NoiseShort = false;

        switch (midiNote)
        {
            case 35: ch.Decay = 0.18; ch.Release = 0.10; ch.NoisePeriod = 10.0; break;
            case 36: ch.Decay = 0.14; ch.Release = 0.08; ch.NoisePeriod = 9.0; break;
            case 38: ch.Decay = 0.055; ch.Release = 0.030; ch.NoisePeriod = 3.0; break;
            case 40: ch.Decay = 0.040; ch.Release = 0.020; ch.NoisePeriod = 2.5; break;
            case 37:
            case 31: ch.Decay = 0.018; ch.Release = 0.010; ch.NoisePeriod = 1.2; ch.NoiseShort = true; break;
            case 39: ch.Attack = 0.003; ch.Decay = 0.035; ch.Release = 0.018; ch.NoisePeriod = 1.8; ch.NoiseShort = true; break;
            case 42: ch.Decay = 0.012; ch.Release = 0.006; ch.NoisePeriod = 0.5; ch.NoiseShort = true; break;
            case 44: ch.Decay = 0.016; ch.Release = 0.008; ch.NoisePeriod = 0.6; ch.NoiseShort = true; break;
            case 46: ch.Decay = 0.10; ch.Release = 0.06; ch.NoisePeriod = 0.6; break;
            case 49:
            case 57: ch.Decay = 0.18; ch.Release = 0.10; ch.NoisePeriod = 0.4; break;
            case 51:
            case 59: ch.Decay = 0.12; ch.Release = 0.07; ch.NoisePeriod = 0.5; break;
            case 53: ch.Decay = 0.08; ch.Release = 0.04; ch.NoisePeriod = 0.8; ch.NoiseShort = true; break;
            case 55: ch.Decay = 0.06; ch.Release = 0.03; ch.NoisePeriod = 0.4; break;
            case 52: ch.Decay = 0.14; ch.Release = 0.08; ch.NoisePeriod = 0.35; break;
            case 41:
            case 43: ch.Decay = 0.09; ch.Release = 0.05; ch.NoisePeriod = 6.0; break;
            case 45:
            case 47: ch.Decay = 0.07; ch.Release = 0.04; ch.NoisePeriod = 5.0; break;
            case 48:
            case 50: ch.Decay = 0.055; ch.Release = 0.030; ch.NoisePeriod = 4.0; break;
            case 56: ch.Decay = 0.07; ch.Release = 0.04; ch.NoisePeriod = 1.4; break;
            case 54: ch.Decay = 0.035; ch.Release = 0.018; ch.NoisePeriod = 0.9; ch.NoiseShort = true; break;
            case 69:
            case 70: ch.Decay = 0.025; ch.Release = 0.012; ch.NoisePeriod = 0.7; ch.NoiseShort = true; break;
            case 60:
            case 61: ch.Decay = 0.050; ch.Release = 0.025; ch.NoisePeriod = 3.5; break;
            case 62:
            case 63:
            case 64: ch.Decay = 0.060; ch.Release = 0.030; ch.NoisePeriod = 4.0; break;
        }
    }
}