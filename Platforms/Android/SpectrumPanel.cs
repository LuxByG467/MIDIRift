using Microsoft.Maui.Controls;
using NAudio.Dsp;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
using System;
using System.Diagnostics;

namespace MIDIRift;

/// <summary>
/// Analizador de espectro impulsado por VSync.
///
/// A diferencia del pipeline anterior, no existe un worker periódico que
/// publique fotografías discretas del FFT. Cada frame visual toma una ventana
/// deslizante de las muestras PCM más recientes y calcula un espectro nuevo.
/// De este modo, una pantalla de 60/90/120 Hz recibe ventanas solapadas que
/// avanzan junto con el reloj del audio, evitando el patrón actualizar/esperar.
/// </summary>
public sealed class SpectrumPanel : SKGLView
{
    private const int VisualFftSize = 8192;
    private const int ChannelFftSize = 2048;
    private const int MixBins = 241;
    private const int ChannelBins = 24;
    private const int ChannelsPerFrame = 1;
    private static readonly long AnalysisIntervalTicks = Stopwatch.Frequency / 20; // 20 Hz DSP visual; paint puede seguir a VSync
    private const float ChannelZoneFraction = 1f / 3f;
    private const float MixZoneFraction = 1f - ChannelZoneFraction;
    private const float FocusedSpectrumHeightFraction = 1f / 3f;
    private const int FocusedMix = -2;
    private const float SampleRate = 44100f;
    private const float MinFrequency = 20f;
    private const float MaxFrequency = 16000f;
    private const float MinDb = -80f;

    // Ataque y caída se expresan en segundos y, por tanto, se mantienen
    // consistentes a 60, 90 y 120 Hz.
    private const float AttackSeconds = 0.030f;
    private const float ReleaseSeconds = 0.145f;
    private const float MetricsIntervalSeconds = 0.10f;

    private static SKColor BgColor = new(13, 13, 15);
    private static SKColor SepColor = new(42, 42, 58);
    private static SKColor LabelColor = new(194, 156, 255);
    private static SKColor IdleTextColor = new(90, 86, 110);
    private static SKColor MixBarColor = new(174, 112, 255);
    private static SKColor ChannelBarColor = new(137, 82, 214);

    private static readonly SKPaint SeparatorPaint = new() { Color = SepColor, IsAntialias = false };
    private static readonly SKTypeface Monospace = SKTypeface.FromFamilyName("monospace");
    private static readonly SKPaint LabelPaint = new() { Color = LabelColor, IsAntialias = true };
    private static readonly SKFont LabelFont = new(Monospace, 20f);
    private static readonly SKFont MixLabelFont = new(Monospace, 24f);
    private static readonly SKPaint IdlePaint = new() { Color = IdleTextColor, IsAntialias = true };
    private static readonly SKFont IdleFont = new(Monospace, 14f);
    // Mismas etiquetas compactas del osciloscopio, usando el acento morado
    // propio del panel FFT.
    private static readonly SKPaint MeterPaint = new() { Color = MixBarColor, IsAntialias = true };
    private static readonly SKPaint ClipPaint = new() { Color = new SKColor(255, 72, 72), IsAntialias = true };
    private static readonly SKFont MeterFont = new(Monospace, 22f);
    private static readonly SKPaint MixPaint = new()
    {
        Color = MixBarColor,
        IsAntialias = false,
        Style = SKPaintStyle.Stroke,
        StrokeCap = SKStrokeCap.Butt,
    };
    private static readonly SKPaint ChannelPaint = new()
    {
        Color = ChannelBarColor,
        IsAntialias = false,
        Style = SKPaintStyle.Stroke,
        StrokeCap = SKStrokeCap.Butt,
    };


    public void ApplyTheme(bool light)
    {
        BgColor = light ? new SKColor(252, 251, 254) : new SKColor(13,13,15);
        SepColor = light ? new SKColor(207,199,219) : new SKColor(42,42,58);
        LabelColor = light ? new SKColor(91,66,154) : new SKColor(194,156,255);
        IdleTextColor = light ? new SKColor(112,102,126) : new SKColor(90,86,110);
        MixBarColor = light ? new SKColor(112,76,190) : new SKColor(174,112,255);
        ChannelBarColor = light ? new SKColor(91,61,158) : new SKColor(137,82,214);
        SeparatorPaint.Color=SepColor; LabelPaint.Color=LabelColor; IdlePaint.Color=IdleTextColor; MeterPaint.Color=MixBarColor; MixPaint.Color=MixBarColor; ChannelPaint.Color=ChannelBarColor;
        InvalidateSurface();
    }

    /// <summary>
    /// Analizador visual independiente del bloque de síntesis. MIX y canal
    /// enfocado usan 8192 muestras (≈5.38 Hz/bin a 44.1 kHz); las miniaturas
    /// multicanal conservan 2048 para no multiplicar brutalmente el costo.
    /// Cada banda integra un intervalo logarítmico mediante muestreo cúbico
    /// de los bins, en lugar de copiar o tomar el máximo de un bin entero.
    /// </summary>
    private sealed class Analyzer
    {
        private readonly int _fftSize;
        private readonly int _fftHalf;
        private readonly int _fftLog2;
        private readonly Complex[] _fft;
        private readonly float[] _window;
        private readonly float[] _spectrum;
        private readonly BandRange[] _mixRanges;
        private readonly BandRange[] _channelRanges;

        private readonly struct BandRange
        {
            public readonly float LoBin;
            public readonly float HiBin;

            public BandRange(float loBin, float hiBin)
            {
                LoBin = loBin;
                HiBin = hiBin;
            }
        }

        public int FftSize => _fftSize;

        public Analyzer(int fftSize)
        {
            _fftSize = fftSize;
            _fftHalf = fftSize / 2;
            _fftLog2 = (int)Math.Log2(fftSize);
            _fft = new Complex[fftSize];
            _window = new float[fftSize];
            _spectrum = new float[_fftHalf];

            for (int i = 0; i < fftSize; i++)
                _window[i] = 0.5f - 0.5f * MathF.Cos(2f * MathF.PI * i / (fftSize - 1));

            _mixRanges = BuildRanges(MixBins);
            _channelRanges = BuildRanges(ChannelBins);
        }

        private static float FastLog10(float x)
        {
            if (!(x > 0f)) return -20f;
            int bits = BitConverter.SingleToInt32Bits(x);
            int exponent = ((bits >> 23) & 0xFF) - 127;
            int mantissaBits = (bits & 0x7FFFFF) | 0x3F800000;
            float m = BitConverter.Int32BitsToSingle(mantissaBits);
            float y = m - 1f;
            float y2 = y * y;
            float ln = y - 0.5f * y2 + y2 * y * (0.3333333333f + y * (-0.25f + y * 0.2f));
            float log2 = exponent + ln * 1.44269504089f;
            return log2 * 0.30102999566f;
        }

        public void Transform(float[] samples, float[] output)
        {
            if (samples.Length < _fftSize)
                throw new ArgumentException($"Se requieren al menos {_fftSize} muestras.", nameof(samples));

            for (int i = 0; i < _fftSize; i++)
            {
                _fft[i].X = samples[i] * _window[i];
                _fft[i].Y = 0f;
            }

            FastFourierTransform.FFT(true, _fftLog2, _fft);

            // NAudio ya entrega una escala coherente para su implementación
            // de FFT. Aplicar 2/N aquí volvía a escalar el resultado una segunda
            // vez en algunas versiones del paquete y podía hundir todo por debajo
            // del piso visual de -80 dB, dejando el panel completamente vacío.
            // Conservamos la misma convención que usaba el FFT de 2048 que sí
            // funcionaba: potencia nativa -> dB -> rango visual.
            const float invDbRange = 1f / -MinDb;
            for (int i = 0; i < _fftHalf; i++)
            {
                float re = _fft[i].X;
                float im = _fft[i].Y;
                float power = re * re + im * im;
                float db = power > 1e-20f && float.IsFinite(power)
                    ? 10f * FastLog10(power)
                    : MinDb;
                float normalized = (db - MinDb) * invDbRange;
                _spectrum[i] = float.IsFinite(normalized)
                    ? Math.Clamp(normalized, 0f, 1f)
                    : 0f;
            }

            BandRange[] ranges = output.Length == MixBins ? _mixRanges : _channelRanges;
            for (int band = 0; band < output.Length; band++)
                output[band] = IntegrateBand(ranges[band]);
        }

        private BandRange[] BuildRanges(int count)
        {
            var ranges = new BandRange[count];
            float minLog = MathF.Log(MinFrequency);
            float logRange = MathF.Log(MaxFrequency) - minLog;
            float binHz = SampleRate / _fftSize;

            for (int i = 0; i < count; i++)
            {
                float t0 = i / (float)count;
                float t1 = (i + 1) / (float)count;
                float lowHz = MathF.Exp(minLog + t0 * logRange);
                float highHz = MathF.Exp(minLog + t1 * logRange);
                float lo = Math.Clamp(lowHz / binHz, 0f, _fftHalf - 1.001f);
                float hi = Math.Clamp(highHz / binHz, lo + 0.001f, _fftHalf - 1.001f);
                ranges[i] = new BandRange(lo, hi);
            }

            return ranges;
        }

        private float IntegrateBand(BandRange range)
        {
            float width = MathF.Max(0.001f, range.HiBin - range.LoBin);

            // Varias muestras por bin cubierto. En las bandas sub-bin de graves
            // mantenemos al menos 4 muestras interpoladas; en bandas anchas
            // integramos más puntos, con un límite para proteger el frame time.
            int sampleCount = Math.Clamp((int)MathF.Ceiling(width * 2f), 4, 32);
            float sum = 0f;
            float weightSum = 0f;

            for (int i = 0; i < sampleCount; i++)
            {
                float u = (i + 0.5f) / sampleCount;
                float bin = range.LoBin + width * u;
                // Ventana triangular dentro de la propia banda para reducir
                // discontinuidades duras entre intervalos vecinos.
                float weight = 1f - MathF.Abs(2f * u - 1f) * 0.35f;
                sum += SampleCubic(bin) * weight;
                weightSum += weight;
            }

            return weightSum > 0f ? sum / weightSum : 0f;
        }

        private float SampleCubic(float position)
        {
            int i1 = Math.Clamp((int)MathF.Floor(position), 0, _fftHalf - 1);
            int i0 = Math.Max(0, i1 - 1);
            int i2 = Math.Min(_fftHalf - 1, i1 + 1);
            int i3 = Math.Min(_fftHalf - 1, i1 + 2);
            float t = position - i1;

            float p0 = _spectrum[i0];
            float p1 = _spectrum[i1];
            float p2 = _spectrum[i2];
            float p3 = _spectrum[i3];

            // Catmull-Rom. Clamp final porque una spline puede sobrepasar
            // levemente el rango de sus puntos de control.
            float a = 2f * p1;
            float b = -p0 + p2;
            float c = 2f * p0 - 5f * p1 + 4f * p2 - p3;
            float d = -p0 + 3f * p1 - 3f * p2 + p3;
            return Math.Clamp(0.5f * (a + b * t + c * t * t + d * t * t * t), 0f, 1f);
        }
    }

    private readonly Analyzer _visualAnalyzer = new(VisualFftSize);
    private readonly Analyzer _channelAnalyzer = new(ChannelFftSize);
    private readonly float[] _visualSampleWindow = new float[VisualFftSize];
    private readonly float[] _channelSampleWindow = new float[ChannelFftSize];
    private readonly float[] _mixTarget = new float[MixBins];
    private readonly float[] _mixDisplay = new float[MixBins];
    private readonly SKPoint[] _mixPoints = new SKPoint[MixBins * 2];

    // La vista general mantiene sólo 24 bandas por canal para ser barata. La
    // vista enfocada necesita su propio espectro de 241 bandas; reutilizar los
    // arrays de miniatura hacía que esas 24 columnas se estiraran a pantalla
    // completa y produjeran los bloques enormes observados al enfocar.
    private readonly float[] _focusedTarget = new float[MixBins];
    private readonly float[] _focusedDisplay = new float[MixBins];
    private readonly SKPoint[] _focusedPoints = new SKPoint[MixBins * 2];

    private IPanelAudioSource? _source;
    private int _channelCount;
    private int _nextChannel;
    private int _focusedChannel = -1;
    private float[][] _channelTarget = Array.Empty<float[]>();
    private float[][] _channelDisplay = Array.Empty<float[]>();
    private SKPoint[] _channelPoints = Array.Empty<SKPoint>();

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _lastPaintTicks;
    private long _lastAnalysisTicks;
    private float _metricsElapsed;
    private SignalMetrics _metrics;
    private long _clipHoldUntilMs;
    private float _clipHeldPeak;

    public event Action? VisualTapped;

    public SpectrumPanel()
    {
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;
        EnableTouchEvents = true;
        HasRenderLoop = false;
        PaintSurface += OnPaintSurface;
        Touch += OnTouch;
    }

    public void Attach(IPanelAudioSource? source)
    {
        _source = source;
        _focusedChannel = -1;
        _nextChannel = 0;
        _metricsElapsed = 0f;
        _metrics = SignalMetrics.Silence;
        _clipHoldUntilMs = 0;
        _clipHeldPeak = 0f;
        Array.Clear(_mixTarget, 0, _mixTarget.Length);
        Array.Clear(_mixDisplay, 0, _mixDisplay.Length);
        ClearFocusedSpectrum();

        if (source == null)
        {
            _channelCount = 0;
            _channelTarget = Array.Empty<float[]>();
            _channelDisplay = Array.Empty<float[]>();
            _channelPoints = Array.Empty<SKPoint>();
            HasRenderLoop = false;
            InvalidateSurface();
            return;
        }

        _channelCount = Math.Max(0, source.ChannelCount);
        _channelTarget = new float[_channelCount][];
        _channelDisplay = new float[_channelCount][];
        _channelPoints = new SKPoint[Math.Max(1, _channelCount * ChannelBins * 2)];
        for (int i = 0; i < _channelCount; i++)
        {
            _channelTarget[i] = new float[ChannelBins];
            _channelDisplay[i] = new float[ChannelBins];
        }

        _lastPaintTicks = _clock.ElapsedTicks;
        HasRenderLoop = true;
        InvalidateSurface();
    }

    public void Detach()
    {
        HasRenderLoop = false;
        _source = null;
        _channelCount = 0;
        _focusedChannel = -1;
        InvalidateSurface();
    }

    // Compatibilidad con MainPage: este panel ya se agenda mediante VSync.
    public void Tick() { }

    private void OnPaintSurface(object? sender, SKPaintGLSurfaceEventArgs e)
    {
        SKCanvas canvas = e.Surface.Canvas;
        canvas.Clear(BgColor);

        float width = CanvasSize.Width;
        float height = CanvasSize.Height;
        IPanelAudioSource? source = _source;
        if (width <= 0 || height <= 0) return;
        if (source == null)
        {
            canvas.DrawText("Espectro — sin señal de audio", width / 2f, height / 2f,
                SKTextAlign.Center, IdleFont, IdlePaint);
            return;
        }

        long now = _clock.ElapsedTicks;
        float dt = _lastPaintTicks == 0
            ? 1f / 60f
            : Math.Clamp((now - _lastPaintTicks) / (float)Stopwatch.Frequency, 1f / 240f, 0.05f);
        _lastPaintTicks = now;
        bool doAnalysis = _lastAnalysisTicks == 0 || now - _lastAnalysisTicks >= AnalysisIntervalTicks;
        if (doAnalysis) _lastAnalysisTicks = now;

        try
        {
            int focused = _focusedChannel;
            if (focused == FocusedMix)
            {
                if (doAnalysis)
                {
                    source.CopyMixSamples(_visualSampleWindow);
                    _visualAnalyzer.Transform(_visualSampleWindow, _mixTarget);
                }
                Smooth(_mixDisplay, _mixTarget, dt);

                float zoneTop = height * (1f - FocusedSpectrumHeightFraction);
                DrawBars(canvas, new SKRect(0, zoneTop, width, height),
                    _mixDisplay, _mixPoints, MixPaint);
                canvas.DrawText("MIX — toca para volver", width * 0.5f, 28,
                    SKTextAlign.Center, MixLabelFont, LabelPaint);

                UpdateMetricsIfNeeded(dt, _visualSampleWindow);
                DrawSignalMetrics(canvas, width);
                return;
            }

            if ((uint)focused < (uint)_channelCount)
            {
                // Una ventana PCM nueva por cada VSync. Como el audio avanza unos
                // cientos de samples entre frames, las ventanas se solapan y el
                // espectro evoluciona continuamente.
                if (doAnalysis)
                {
                    source.CopyChannelSamples(focused, _visualSampleWindow);
                    _visualAnalyzer.Transform(_visualSampleWindow, _focusedTarget);
                }
                Smooth(_focusedDisplay, _focusedTarget, dt);

                float zoneTop = height * (1f - FocusedSpectrumHeightFraction);
                DrawBars(canvas, new SKRect(0, zoneTop, width, height),
                    _focusedDisplay, _focusedPoints, ChannelPaint);
                canvas.DrawText($"CANAL {focused:D2} — toca para volver", width * 0.5f, 28,
                    SKTextAlign.Center, LabelFont, LabelPaint);

                UpdateMetricsIfNeeded(dt, _visualSampleWindow);
                DrawSignalMetrics(canvas, width);
                return;
            }

            // MIX siempre se analiza en cada frame visual.
            if (doAnalysis)
            {
                source.CopyMixSamples(_visualSampleWindow);
                _visualAnalyzer.Transform(_visualSampleWindow, _mixTarget);
                AnalyzeChannelSlice(source);
            }
            Smooth(_mixDisplay, _mixTarget, dt);
            UpdateMetricsIfNeeded(dt, _visualSampleWindow);

            float mixHeight = _channelCount > 0 ? height * MixZoneFraction : height;
            DrawBars(canvas, new SKRect(0, 0, width, mixHeight), _mixDisplay, _mixPoints, MixPaint);
            canvas.DrawText("MIX", width * 0.5f, 28, SKTextAlign.Center, MixLabelFont, LabelPaint);

            if (_channelCount > 0)
            {
                canvas.DrawRect(new SKRect(0, mixHeight, width, mixHeight + 1), SeparatorPaint);
                DrawChannels(canvas, width, height, mixHeight + 1, dt);
            }

            DrawSignalMetrics(canvas, width);
        }
        catch (Exception ex)
        {
            // Nunca permitir que una lectura concurrente temporal del motor mate el
            // render loop. A diferencia de la versión anterior, el error también se
            // muestra en el panel: un catch silencioso convertía cualquier excepción
            // de FFT en una pantalla negra imposible de diagnosticar.
            Debug.WriteLine($"[SpectrumVSync] Frame error: {ex}");
            canvas.DrawText($"FFT ERROR: {ex.GetType().Name}", 8, 22,
                SKTextAlign.Left, IdleFont, IdlePaint);
        }
    }

    private void AnalyzeChannelSlice(IPanelAudioSource source)
    {
        int budget = Math.Min(ChannelsPerFrame, _channelCount);
        for (int n = 0; n < budget; n++)
        {
            int channel = _nextChannel++;
            if (_nextChannel >= _channelCount) _nextChannel = 0;
            source.CopyChannelSamples(channel, _channelSampleWindow);
            _channelAnalyzer.Transform(_channelSampleWindow, _channelTarget[channel]);
        }
    }

    private void UpdateMetricsIfNeeded(float dt, float[] samples)
    {
        if (!MeasurementLabelsSettings.Enabled) return;
        _metricsElapsed += dt;
        if (_metricsElapsed < MetricsIntervalSeconds) return;
        _metricsElapsed = 0f;
        _metrics = SignalMetricsCalculator.Calculate(samples);

        long now = Environment.TickCount64;
        if (_metrics.Clipped)
        {
            _clipHeldPeak = Math.Max(_clipHeldPeak, _metrics.Peak);
            _clipHoldUntilMs = now + 1500;
        }
        else if (now >= _clipHoldUntilMs)
        {
            _clipHeldPeak = 0f;
        }
    }

    private void DrawSignalMetrics(SKCanvas canvas, float width)
    {
        if (!MeasurementLabelsSettings.Enabled) return;

        SignalMetrics m = _metrics;
        const float x = 8f;
        const float firstY = 30f;
        const float lineH = 27f;
        canvas.DrawText($"RMS     {m.Rms:F3}", x, firstY, SKTextAlign.Left, MeterFont, MeterPaint);
        canvas.DrawText($"RMS dB  {m.RmsDb:F1} dB", x, firstY + lineH, SKTextAlign.Left, MeterFont, MeterPaint);
        canvas.DrawText($"PEAK    {m.Peak:F3} ({m.PeakDb:+0.0;-0.0;0.0} dB)", x, firstY + lineH * 2, SKTextAlign.Left, MeterFont, MeterPaint);
        canvas.DrawText($"CREST   {m.CrestDb:F1} dB", x, firstY + lineH * 3, SKTextAlign.Left, MeterFont, MeterPaint);

        bool clipVisible = Environment.TickCount64 < _clipHoldUntilMs;
        canvas.DrawText(clipVisible ? $"CLIP {_clipHeldPeak:F3}" : "PEAK OK",
            width - 8f, firstY, SKTextAlign.Right, MeterFont, clipVisible ? ClipPaint : MeterPaint);
    }

    private static void Smooth(float[] display, float[] target, float dt)
    {
        float attack = 1f - MathF.Exp(-dt / AttackSeconds);
        float release = 1f - MathF.Exp(-dt / ReleaseSeconds);
        int count = Math.Min(display.Length, target.Length);
        for (int i = 0; i < count; i++)
        {
            float alpha = target[i] >= display[i] ? attack : release;
            display[i] += (target[i] - display[i]) * alpha;
        }
    }

    private static void DrawBars(SKCanvas canvas, SKRect zone, float[] values, SKPoint[] points, SKPaint paint)
    {
        int count = Math.Min(values.Length, points.Length / 2);
        if (count <= 0) return;
        float step = zone.Width / count;
        paint.StrokeWidth = Math.Max(1f, step * 0.72f);
        for (int i = 0; i < count; i++)
        {
            float x = zone.Left + (i + 0.5f) * step;
            float y = zone.Bottom - Math.Clamp(values[i], 0f, 1f) * zone.Height;
            int p = i * 2;
            points[p] = new SKPoint(x, zone.Bottom);
            points[p + 1] = new SKPoint(x, y);
        }
        canvas.DrawPoints(SKPointMode.Lines, points, paint);
    }

    private void DrawChannels(SKCanvas canvas, float width, float height, float top, float dt)
    {
        float channelWidth = width / _channelCount;
        float step = channelWidth / ChannelBins;
        int cursor = 0;

        for (int channel = 0; channel < _channelCount; channel++)
        {
            Smooth(_channelDisplay[channel], _channelTarget[channel], dt);
            float left = channel * channelWidth;
            for (int i = 0; i < ChannelBins; i++)
            {
                float x = left + (i + 0.5f) * step;
                float y = height - Math.Clamp(_channelDisplay[channel][i], 0f, 1f) * (height - top);
                _channelPoints[cursor++] = new SKPoint(x, height);
                _channelPoints[cursor++] = new SKPoint(x, y);
            }
        }

        ChannelPaint.StrokeWidth = Math.Max(1f, step * 0.70f);
        canvas.DrawPoints(SKPointMode.Lines, _channelPoints, ChannelPaint);

        for (int channel = 0; channel < _channelCount; channel++)
        {
            float x = channel * channelWidth;
            if (channel > 0)
                canvas.DrawRect(new SKRect(x, top, x + 1, height), SeparatorPaint);
            canvas.DrawText($"{channel:D2}", x + 5, top + 24,
                SKTextAlign.Left, LabelFont, LabelPaint);
        }
    }

    private void OnTouch(object? sender, SKTouchEventArgs e)
    {
        e.Handled = true;
        if (e.ActionType == SKTouchAction.Released)
        {
            VisualTapped?.Invoke();
            return;
        }
        if (e.ActionType != SKTouchAction.Pressed) return;

        if (_focusedChannel != -1)
        {
            _focusedChannel = -1;
            ClearFocusedSpectrum();
            return;
        }

        float width = CanvasSize.Width;
        float height = CanvasSize.Height;
        if (width <= 0 || height <= 0) return;

        if (e.Location.Y <= height * MixZoneFraction)
        {
            ClearFocusedSpectrum();
            _focusedChannel = FocusedMix;
            return;
        }

        if (_channelCount <= 0) return;
        int channel = (int)(e.Location.X / (width / _channelCount));
        if ((uint)channel < (uint)_channelCount)
        {
            ClearFocusedSpectrum();
            _focusedChannel = channel;
        }
    }

    private void ClearFocusedSpectrum()
    {
        Array.Clear(_focusedTarget, 0, _focusedTarget.Length);
        Array.Clear(_focusedDisplay, 0, _focusedDisplay.Length);
    }
}
