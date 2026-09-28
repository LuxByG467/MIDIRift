using Android.Media;
using Android.OS;
using Java.Nio;
using System;
using System.Diagnostics;
using System.Threading;

namespace MIDIRift;

/// <summary>
/// Motor de MP3 para Android — segunda versión.
///
/// La primera versión usaba Android.Media.MediaPlayer: simple y robusto,
/// pero MediaPlayer es una caja negra que decodifica internamente y nunca
/// expone el PCM resultante. Eso dejaba a SpectrumPanel/OscilloscopePanel
/// sin nada que mostrar en modo MP3.
///
/// La alternativa que se suele sugerir para "espiar" el audio en Android es
/// android.media.audiofx.Visualizer, pero esa clase SIEMPRE exige el
/// permiso RECORD_AUDIO — incluso capturando la sesión de audio propia de
/// la app — porque a nivel de plataforma se lo trata como una forma de
/// grabación. Pedirle ese permiso a alguien para un reproductor de música
/// es exactamente la señal de alarma que no queremos mostrarle al usuario.
///
/// La solución real es no usar MediaPlayer en absoluto: se decodifica el
/// MP3 nosotros mismos con MediaExtractor (demux) + MediaCodec (decode a
/// PCM), y el PCM resultante se empuja a mano a un AudioTrack — mismo
/// patrón "pull del archivo, push al audio" que ya usa ChiptuneAudioTrack
/// para la síntesis, solo que acá el productor del PCM es un decodificador
/// en vez de un sintetizador. Como el PCM pasa por nuestras manos antes de
/// llegar al AudioTrack, podemos copiarlo a un ring buffer de captura igual
/// que ChiptuneAudioTrack — sin pedir ningún permiso extra.
///
/// MP3 no tiene "canales" en el sentido instrumental (no hay desglose por
/// pista/instrumento, solo el downmix L/R del archivo), así que
/// IPanelAudioSource.ChannelCount es siempre 0 acá: los paneles muestran
/// solo la zona MIX, igual que ya contemplaban por diseño (ver el comentario
/// original en IPanelAudioSource.cs).
/// </summary>
public class Mp3AudioPlayer : IMp3Player, IPanelAudioSource, IDisposable
{
    // Mismo tamaño de captura que ChiptuneAudioTrack, para que ambos
    // motores alimenten a SpectrumRenderer con la misma ventana de FFT.
    private const int CaptureLen = 1 << 16; // 4096

    private readonly string _path;

    private MediaExtractor? _extractor;
    private MediaCodec? _codec;
    private AudioTrack? _audioTrack;
    private Thread? _decodeThread;

    private readonly object _lock = new();
    private volatile bool _running = false;
    private volatile bool _paused = true;
    private volatile bool _completed = false;

    private volatile bool _seekRequested = false;
    private long _seekTargetUs = 0;

    private int _sampleRate = 44100;
    private int _channelCount = 2;
    private long _durationUs = 0;

    // Frames de audio ya escritos al AudioTrack — con esto calculamos la
    // posición de reproducción sin depender de ningún reloj propio del
    // decoder (MediaCodec no expone "dónde va" en tiempo de reproducción,
    // solo en tiempo de decodificación, que va adelantado por el buffering).
    private long _presentedFrames = 0;

    private float _pendingSpeed = 1f;

    // ── Captura para SpectrumPanel/OscilloscopePanel ───────────────────────
    // Mismo esquema sin locks que ChiptuneAudioTrack._mixCapture: se escribe
    // desde el hilo de decodificación, se lee ocasionalmente (30 fps) desde
    // el hilo de UI. Una lectura que "rompe" a mitad de escritura no importa
    // para una visualización.
    private readonly float[] _mixCapture = new float[CaptureLen];
    private int _captureWritePos = 0;
    private long _captureFramesWritten = 0;
    private readonly object _playbackClockLock = new();
    private uint _playbackHeadLastRaw = 0;
    private long _playbackHeadWrapBase = 0;
    private long _playbackAnchorFrame = 0;
    private long _playbackAnchorTicks = 0;

    // ── Ecualizador gráfico (DSP real vía GraphicEqualizer) ─────────────────
    // Mismo componente que ChiptuneAudioTrack (ver ese archivo y
    // GraphicEqualizer.cs, portado 1:1 desde Desktop). GraphicEqualizer
    // asume 44100Hz fijo (igual que Mp3AudioSource en Desktop, que
    // resamplea todo a 44100 antes del EQ) — acá no se resamplea, así que
    // en archivos a otro sample rate (48kHz, etc.) las frecuencias de las
    // bandas quedan levemente corridas; el efecto tonal general (graves/
    // agudos) sigue siendo correcto.
    private readonly GraphicEqualizer _eq = new();
    private readonly BassRestorationProcessor _bassRestoration = new();
    private float[] _eqScratch = Array.Empty<float>();

    public void SetEqBand(int band, float gainDb) => _eq.SetBandGain(band, gainDb);

    public float[] GetEqGains() => _eq.GetGains();

    public void SetPreamp(float gainDb) => _eq.SetPreampDb(gainDb);

    public float GetPreamp() => _eq.GetPreamp();

    public void SetBassRestorationEnabled(bool enabled) => _bassRestoration.SetEnabled(enabled);
    public void SetBassRestorationIntensity(float intensity) => _bassRestoration.SetIntensity(intensity);
    public void SetBassRestorationFrequency(float frequencyHz) => _bassRestoration.SetFrequency(frequencyHz);
    public void SetBassRestorationMix(float mix) => _bassRestoration.SetMix(mix);

    public event Action? OnCompleted;

    public bool IsFinished => _completed;
    public bool IsPlaying => _running && !_paused;

    public float PositionSeconds => _sampleRate > 0 ? _presentedFrames / (float)_sampleRate : 0f;
    public float DurationSeconds => _durationUs / 1_000_000f;
    public int SampleRate => _sampleRate;

    public float Speed
    {
        get => _pendingSpeed;
        set
        {
            _pendingSpeed = value <= 0f ? 1f : value;
            ApplySpeed();
        }
    }

    // ── IPanelAudioSource ────────────────────────────────────────────────
    // 0 = "solo mezcla", ver comentario de clase.
    public int ChannelCount => 0;

    public Mp3AudioPlayer(string path)
    {
        _path = path;
        Prepare();
    }

    // ── Setup ────────────────────────────────────────────────────────────

    private void Prepare()
    {
        _extractor = new MediaExtractor();
        _extractor.SetDataSource(_path);

        int trackIndex = -1;
        MediaFormat? format = null;

        for (int i = 0; i < _extractor.TrackCount; i++)
        {
            var f = _extractor.GetTrackFormat(i);
            var mime = f.GetString(MediaFormat.KeyMime);
            if (mime != null && mime.StartsWith("audio/"))
            {
                trackIndex = i;
                format = f;
                break;
            }
        }

        if (trackIndex < 0 || format == null)
            throw new InvalidOperationException("El archivo no tiene una pista de audio reconocible.");

        _extractor.SelectTrack(trackIndex);

        _sampleRate = format.GetInteger(MediaFormat.KeySampleRate);
        _eq.ConfigureSampleRate(_sampleRate);
        _bassRestoration.ConfigureSampleRate(_sampleRate);
        _channelCount = format.GetInteger(MediaFormat.KeyChannelCount);

        try { _durationUs = format.GetLong(MediaFormat.KeyDuration); }
        catch { _durationUs = 0; }

        string mimeType = format.GetString(MediaFormat.KeyMime)!;
        _codec = MediaCodec.CreateDecoderByType(mimeType);
        _codec.Configure(format, null, null, MediaCodecConfigFlags.None);
        _codec.Start();

        EnsureAudioTrack();
    }

    private void EnsureAudioTrack()
    {
        var channelConfig = _channelCount >= 2 ? ChannelOut.Stereo : ChannelOut.Mono;
        int minBufBytes = AudioTrack.GetMinBufferSize(_sampleRate, channelConfig, Encoding.Pcm16bit);
        if (minBufBytes <= 0) minBufBytes = 8192;

        // x4 de margen: deja lugar de sobra para reproducir hasta 2.0x de
        // velocidad (el máximo del slider) sin que AudioTrack rechace el
        // cambio de PlaybackParams por buffer insuficiente.
        int bufBytes = minBufBytes * 4;

        _audioTrack = new AudioTrack(
            Android.Media.Stream.Music,
            _sampleRate,
            channelConfig,
            Encoding.Pcm16bit,
            bufBytes,
            AudioTrackMode.Stream);

        ResetPlaybackCaptureClock();
        ApplySpeed();
    }

    private void ApplySpeed()
    {
        if (_audioTrack == null) return;
        if (Build.VERSION.SdkInt < BuildVersionCodes.M) return; // PlaybackParams requiere API 23+

        try
        {
            var parameters = _audioTrack.PlaybackParams;
            parameters.SetSpeed(_pendingSpeed);
            _audioTrack.PlaybackParams = parameters;
        }
        catch
        {
            // Igual que en la versión MediaPlayer: si el dispositivo no
            // soporta velocidad variable para este formato, fallamos en
            // silencio y seguimos a velocidad normal.
        }
    }

    // ── Transporte ───────────────────────────────────────────────────────

    public void Play()
    {
        lock (_lock)
        {
            _completed = false;
            _paused = false;

            if (_running)
            {
                try { _audioTrack?.Play(); } catch { }
                return;
            }

            _running = true;
            try { _audioTrack?.Play(); } catch { }

            _decodeThread = new Thread(DecodeLoop)
            {
                IsBackground = true,
                Name = "MIDIRift-Mp3Decode",
                Priority = System.Threading.ThreadPriority.Highest,
            };
            _decodeThread.Start();
        }
    }

    public void Pause()
    {
        _paused = true;
        try { _audioTrack?.Pause(); } catch { }
    }

    public void Stop()
    {
        StopDecodeThread();
        try
        {
            _audioTrack?.Pause();
            _audioTrack?.Flush();
        }
        catch { }
        _paused = true;
    }

    public void Reset()
    {
        SeekTo(0);
        _completed = false;
    }

    public void SeekTo(float seconds)
    {
        Interlocked.Exchange(ref _seekTargetUs, (long)(Math.Max(0, seconds) * 1_000_000));
        _seekRequested = true;

        // Si el hilo de decodificación no está corriendo (p.ej. venimos de
        // Stop()), no hay quién procese el seek — lo resolvemos síncrono
        // acá mismo para que Reset()+Play() arranque ya desde el lugar correcto.
        if (!_running && _codec != null && _extractor != null)
            DoSeek(_codec, _extractor);
    }

    private void StopDecodeThread()
    {
        bool wasRunning;
        lock (_lock)
        {
            wasRunning = _running;
            _running = false;
        }
        if (wasRunning || _decodeThread != null)
        {
            _decodeThread?.Join(500);
            _decodeThread = null;
        }
    }

    public void Dispose()
    {
        StopDecodeThread();

        try { _audioTrack?.Release(); _audioTrack?.Dispose(); } catch { }
        try { _codec?.Stop(); } catch { }
        try { _codec?.Release(); } catch { }
        try { _extractor?.Release(); } catch { }

        _audioTrack = null;
        _codec = null;
        _extractor = null;
    }

    // ── IPanelAudioSource ────────────────────────────────────────────────

    public void CopyMixSamples(float[] dest)
    {
        long written = Interlocked.Read(ref _captureFramesWritten);
        long playbackFrame = GetEstimatedPlaybackFrame(written);
        CopyRingAtFrame(_mixCapture, dest, playbackFrame, written);
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
                estimated = _playbackAnchorFrame + (long)(elapsed * _sampleRate * Math.Max(0.01f, _pendingSpeed));
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
            dest[leadingZeros + i] = ring[(int)((startFrame + i) & (ring.Length - 1))];

        if (len < dest.Length)
            Array.Clear(dest, len, dest.Length - len);
    }

    public void CopyMeterSamples(float[] dest) => CopyMixSamples(dest);

    /// <summary>MP3 no tiene desglose por canal instrumental — ver ChannelCount.</summary>
    public void CopyChannelSamples(int channel, float[] dest) => Array.Clear(dest, 0, dest.Length);

    private void ResetPlaybackCaptureClock()
    {
        lock (_playbackClockLock)
        {
            _playbackHeadLastRaw = 0;
            _playbackHeadWrapBase = 0;
            _playbackAnchorFrame = 0;
            _playbackAnchorTicks = 0;
        }
        Interlocked.Exchange(ref _captureFramesWritten, 0);
        _captureWritePos = 0;
        Array.Clear(_mixCapture, 0, _mixCapture.Length);
    }

    // ── Hilo de decodificación ───────────────────────────────────────────
    // Bucle síncrono clásico de MediaCodec (dequeue input -> feed ->
    // dequeue output -> consumir -> release), igual al patrón que documenta
    // Android para decodificación fuera de un Surface. La diferencia con el
    // ejemplo de los docs es que acá el "consumo" del buffer de salida es
    // escribirlo en un AudioTrack en vez de tirarlo a un archivo.

    private void DecodeLoop()
    {
        var codec = _codec;
        var extractor = _extractor;
        var track = _audioTrack;
        if (codec == null || extractor == null || track == null) return;

        var bufferInfo = new MediaCodec.BufferInfo();
        bool inputDone = false;
        bool outputDone = false;
        byte[] pcmBuffer = Array.Empty<byte>();

        const long TimeoutUs = 10_000;

        while (_running && !outputDone)
        {
            if (_seekRequested)
            {
                DoSeek(codec, extractor);
                inputDone = false;
                outputDone = false;
                _completed = false;
            }

            if (_paused)
            {
                Thread.Sleep(20);
                continue;
            }

            if (!inputDone)
            {
                int inIndex = codec.DequeueInputBuffer(TimeoutUs);
                if (inIndex >= 0)
                {
                    var inputBuffer = codec.GetInputBuffer(inIndex);
                    if (inputBuffer != null)
                    {
                        inputBuffer.Clear();
                        int sampleSize = extractor.ReadSampleData(inputBuffer, 0);
                        if (sampleSize < 0)
                        {
                            codec.QueueInputBuffer(inIndex, 0, 0, 0, MediaCodecBufferFlags.EndOfStream);
                            inputDone = true;
                        }
                        else
                        {
                            long pts = extractor.SampleTime;
                            codec.QueueInputBuffer(inIndex, 0, sampleSize, pts, MediaCodecBufferFlags.None);
                            extractor.Advance();
                        }
                    }
                }
            }

            int outIndex = codec.DequeueOutputBuffer(bufferInfo, TimeoutUs);
            if (outIndex >= 0)
            {
                if (bufferInfo.Size > 0)
                {
                    var outputBuffer = codec.GetOutputBuffer(outIndex);
                    if (outputBuffer != null)
                    {
                        if (pcmBuffer.Length < bufferInfo.Size)
                            pcmBuffer = new byte[bufferInfo.Size];

                        outputBuffer.Get(pcmBuffer, 0, bufferInfo.Size);
                        WritePcm(track, pcmBuffer, bufferInfo.Size);
                    }
                }

                bool eos = (bufferInfo.Flags & MediaCodecBufferFlags.EndOfStream) != 0;
                codec.ReleaseOutputBuffer(outIndex, false);

                if (eos) outputDone = true;
            }
            else if (outIndex == (int)MediaCodecInfoState.OutputFormatChanged)
            {
                // El sample rate/canal real puede diferir del declarado por
                // MediaExtractor en el MediaFormat original.
                var newFormat = codec.OutputFormat;
                if (newFormat != null)
                {
                    _sampleRate = newFormat.GetInteger(MediaFormat.KeySampleRate);
                    _eq.ConfigureSampleRate(_sampleRate);
                    _bassRestoration.ConfigureSampleRate(_sampleRate);
                    _channelCount = newFormat.GetInteger(MediaFormat.KeyChannelCount);
                }
            }
            // OutputBuffersChanged (API vieja) / TryAgainLater: nada que hacer.
        }

        _running = false;

        if (outputDone && !_seekRequested)
        {
            _completed = true;
            OnCompleted?.Invoke();
        }
    }

    private void DoSeek(MediaCodec codec, MediaExtractor extractor)
    {
        _seekRequested = false;
        try
        {
            long targetUs = Interlocked.Read(ref _seekTargetUs);
            extractor.SeekTo(targetUs, MediaExtractorSeekTo.ClosestSync);
            try { _audioTrack?.Flush(); } catch { }
            ResetPlaybackCaptureClock();

            // Reflejar la nueva posición YA, sin esperar a que el hilo de
            // decodificación produzca PCM en el nuevo punto — si no,
            // PositionSeconds (y por lo tanto la UI) queda mostrando la
            // posición vieja hasta el próximo bloque decodificado.
            _presentedFrames = (long)(targetUs / 1_000_000.0 * _sampleRate);
        }
        catch { }
    }

    /// <summary>
    /// Aplica el ecualizador sobre el PCM16 intercalado (in-place sobre
    /// <paramref name="pcm"/>), lo escribe al AudioTrack y, con el mismo
    /// PCM ya ecualizado, hace un downmix a mono float en el ring buffer de
    /// captura para los paneles — mismo orden que ChiptuneAudioTrack /
    /// Mp3AudioSource en Desktop (EQ antes de que la visualización lo vea).
    /// </summary>
    private void WritePcm(AudioTrack track, byte[] pcm, int byteCount)
    {
        int channels = Math.Max(1, _channelCount);
        int frameBytes = channels * 2;
        int frameCount = byteCount / frameBytes;

        // GraphicEqualizer.ProcessInterleaved trabaja sobre float[] L,R,L,R.
        // channels==1 se sube a un par L=R temporal para reusar exactamente
        // la misma cascada de biquads sin duplicar DSP (ver GraphicEqualizer.cs).
        if (_eqScratch.Length < frameCount * 2)
            _eqScratch = new float[frameCount * 2];
        var scratch = _eqScratch;

        for (int i = 0, f = 0; f < frameCount; i += frameBytes, f++)
        {
            short l = (short)((pcm[i + 1] << 8) | (pcm[i] & 0xFF));
            float lf = l / 32768f;
            float rf = lf;
            if (channels >= 2)
            {
                short r = (short)((pcm[i + 3] << 8) | (pcm[i + 2] & 0xFF));
                rf = r / 32768f;
            }
            scratch[f * 2] = lf;
            scratch[f * 2 + 1] = rf;
        }

        _eq.ProcessInterleaved(scratch, 0, frameCount);
        _bassRestoration.ProcessInterleaved(scratch, 0, frameCount);

        for (int i = 0, f = 0; f < frameCount; i += frameBytes, f++)
        {
            float lf = scratch[f * 2];
            float rf = scratch[f * 2 + 1];

            short l = (short)Math.Clamp(lf * 32768f, short.MinValue, short.MaxValue);
            pcm[i] = (byte)(l & 0xFF);
            pcm[i + 1] = (byte)((l >> 8) & 0xFF);

            if (channels >= 2)
            {
                short r = (short)Math.Clamp(rf * 32768f, short.MinValue, short.MaxValue);
                pcm[i + 2] = (byte)(r & 0xFF);
                pcm[i + 3] = (byte)((r >> 8) & 0xFF);
            }

        }

        int written = track.Write(pcm, 0, byteCount, WriteMode.Blocking);
        if (written <= 0) return;

        int writtenFrames = Math.Min(frameCount, written / frameBytes);
        long firstFrame = Interlocked.Read(ref _captureFramesWritten);
        for (int f = 0; f < writtenFrames; f++)
        {
            float lf = scratch[f * 2];
            float rf = scratch[f * 2 + 1];
            _mixCapture[(int)((firstFrame + f) & (CaptureLen - 1))] = (lf + rf) * 0.5f;
        }
        long captureTotal = firstFrame + writtenFrames;
        Volatile.Write(ref _captureWritePos, (int)(captureTotal & (CaptureLen - 1)));
        Interlocked.Exchange(ref _captureFramesWritten, captureTotal);

        _presentedFrames += writtenFrames;
    }
}
