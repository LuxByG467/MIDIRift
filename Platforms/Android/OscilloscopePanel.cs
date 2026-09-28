using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
using System;

namespace MIDIRift;

/// <summary>
/// Port simplificado de OscilloscopePanel.cs (escritorio/Avalonia) a
/// MAUI/SkiaSharp para Android.
///
/// Qué se portó: la idea central — forma de onda mix + por canal, con
/// trigger simple (sincroniza en el primer cruce ascendente por cero para
/// que la onda no "tiemble" entre frames) y el look neón con glow.
///
/// Qué se dejó afuera a propósito (para mantener esto viable en el tiempo
/// disponible): el modo XY, el efecto de quemado tipo CRT (burn-in) con
/// ring buffer de snapshots, y el monitor de uso de CPU embebido en el
/// panel. Nada de eso afecta la utilidad práctica de "ver la forma de onda
/// mientras suena"; si más adelante los querés agregar, la superficie ya
/// está montada sobre SkiaSharp así que encajarían del mismo modo que en
/// SpectrumPanel.
/// </summary>
public class OscilloscopePanel : SKGLView
{
    private const int WindowSamples = 512;
    private const bool ShowPerformanceOverlay = false;
    private const float MixZoneFraction = 0.55f;
    private const string TriggerPreferenceKey = "oscilloscope.triggered";
    private const float TriggerButtonWidth = 132f;
    private const float TriggerButtonHeight = 46f;
    private const float TriggerButtonMargin = 12f;

    private static SKColor BgColor = new(5, 12, 5);
    private static SKColor GridColor = new(25, 53, 25);
    private static SKColor GridCenterColor = new(42, 92, 42);
    private static SKColor SepColor = new(58, 58, 58);
    private static SKColor LabelColor = new(120, 190, 120);
    private static SKColor IdleTextColor = new(90, 86, 110);

    private static SKColor MixCoreColor = new(58, 224, 90);
    private static SKColor MixGlowColor = new(32, 192, 64, 90);
    private static SKColor ChCoreColor = new(40, 184, 72);
    private static SKColor ChGlowColor = new(24, 148, 58, 70);

    private static readonly SKPaint PaintBg = new() { Color = BgColor, IsAntialias = false };
    private static readonly SKPaint PaintGrid = new() { Color = GridColor, IsAntialias = false, StrokeWidth = 1 };
    private static readonly SKPaint PaintGridCenter = new() { Color = GridCenterColor, IsAntialias = false, StrokeWidth = 1 };
    private static readonly SKPaint PaintSep = new() { Color = SepColor, IsAntialias = false };
    private static readonly SKTypeface MonospaceTypeface = SKTypeface.FromFamilyName("monospace");

    private static readonly SKPaint PaintLabel = new()
    {
        Color = LabelColor,
        IsAntialias = true,
    };
    private static readonly SKFont FontLabel = new(MonospaceTypeface, 12f);
    private static readonly SKPaint PaintMeter = new() { Color = MixCoreColor, IsAntialias = true };
    private static readonly SKPaint PaintClip = new() { Color = new SKColor(255, 72, 72), IsAntialias = true };
    private static readonly SKFont FontMeter = new(MonospaceTypeface, 22f);

    private static readonly SKPaint PaintIdle = new()
    {
        Color = IdleTextColor,
        IsAntialias = true,
    };
    private static readonly SKFont FontIdle = new(MonospaceTypeface, 14f);

    private static readonly SKPaint PaintMixCore = MakeStroke(MixCoreColor, 1.75f);
    private static readonly SKPaint PaintMixGlow = MakeGlowStroke(MixGlowColor, 3.5f, 5f);
    private static readonly SKPaint PaintChCore = MakeStroke(ChCoreColor, 1.25f);
    private static readonly SKPaint PaintChGlow = MakeGlowStroke(ChGlowColor, 2.5f, 3.5f);

    // Trigger UI: keep every Skia object persistent. This method runs on every
    // oscilloscope frame; allocating paints/typefaces/fonts here creates GC
    // pressure that can steal enough time from Lyra to cause underruns.
    private static readonly SKPaint PaintTriggerFill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private static readonly SKPaint PaintTriggerStroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };
    private static readonly SKPaint PaintTriggerText = new() { IsAntialias = true };
    private static readonly SKTypeface TriggerTypeface = SKTypeface.FromFamilyName(null, SKFontStyle.Bold);
    private static readonly SKFont TriggerFont = new(TriggerTypeface, 16f);


    public void ApplyTheme(bool light)
    {
        BgColor = light ? new SKColor(247,252,248) : new SKColor(5,12,5);
        GridColor = light ? new SKColor(205,225,209) : new SKColor(25,53,25);
        GridCenterColor = light ? new SKColor(155,195,164) : new SKColor(42,92,42);
        SepColor = light ? new SKColor(196,205,198) : new SKColor(58,58,58);
        LabelColor = light ? new SKColor(48,120,66) : new SKColor(120,190,120);
        IdleTextColor = light ? new SKColor(105,112,107) : new SKColor(90,86,110);
        MixCoreColor = light ? new SKColor(39,151,68) : new SKColor(58,224,90);
        MixGlowColor = light ? new SKColor(72,173,96,55) : new SKColor(32,192,64,90);
        ChCoreColor = light ? new SKColor(43,132,64) : new SKColor(40,184,72);
        ChGlowColor = light ? new SKColor(64,150,83,45) : new SKColor(24,148,58,70);
        PaintBg.Color=BgColor; PaintGrid.Color=GridColor; PaintGridCenter.Color=GridCenterColor; PaintSep.Color=SepColor; PaintLabel.Color=LabelColor; PaintIdle.Color=IdleTextColor; PaintMeter.Color=MixCoreColor;
        PaintMixCore.Color=MixCoreColor; PaintMixGlow.Color=MixGlowColor; PaintChCore.Color=ChCoreColor; PaintChGlow.Color=ChGlowColor;
        InvalidateSurface();
    }

    private static SKPaint MakeStroke(SKColor c, float width) => new()
    {
        Color = c,
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = width,
        StrokeCap = SKStrokeCap.Round,
        StrokeJoin = SKStrokeJoin.Round,
    };

    private static SKPaint MakeGlowStroke(SKColor c, float width, float blur) => new()
    {
        Color = c,
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = width,
        StrokeCap = SKStrokeCap.Round,
        StrokeJoin = SKStrokeJoin.Round,
        MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blur),
    };

    // ── Fuente + buffers ──────────────────────────────────────────────────
    // Ver el comentario detallado en SpectrumPanel.PanelBuffers: mismo bug
    // (IndexOutOfRangeException intermitente por lectura partida de
    // _channelCount + arrays entre el hilo de UI y el hilo de render GL de
    // SKGLView) y mismo fix (snapshot inmutable publicado con una sola
    // asignación de referencia).
    private sealed class WaveFrame
    {
        public readonly float[] Samples = new float[WindowSamples];
        public int TriggerOffset;
    }

    private sealed class PanelBuffers
    {
        public readonly int ChannelCount;
        public WaveFrame MixFront = new();
        public WaveFrame MixBack = new();
        public readonly float[] MeterBuf = new float[WindowSamples];
        public readonly WaveFrame[] ChFront;
        public readonly WaveFrame[] ChBack;
        public readonly string[] ChLabels;
        public SignalMetrics Metrics;
        public long ClipHoldUntilMs;
        public float ClipHeldPeak;

        public PanelBuffers(int channelCount)
        {
            ChannelCount = channelCount;
            ChFront = new WaveFrame[channelCount];
            ChBack = new WaveFrame[channelCount];
            ChLabels = new string[channelCount];
            for (int i = 0; i < channelCount; i++)
            {
                ChFront[i] = new WaveFrame();
                ChBack[i] = new WaveFrame();
                ChLabels[i] = $"{i:D2}";
            }
        }
    }

    private IPanelAudioSource? _source;
    private volatile PanelBuffers? _buffers;

    // ── Medidor de FPS (temporal, para el experimento de perf del glow) ────
    // Ventana móvil de 500ms: se acumulan frames y cada medio segundo se
    // recalcula el promedio. No usa GC (solo Stopwatch + campos primitivos).
    private static readonly SKPaint PaintFps = new() { Color = new SKColor(255, 220, 60), IsAntialias = true };
    private static readonly SKFont FontFps = new(MonospaceTypeface, 13f);
    private readonly System.Diagnostics.Stopwatch _fpsClock = System.Diagnostics.Stopwatch.StartNew();
    private int _fpsFrames = 0;
    private double _fps = 0;
    private long _fpsWindowStartMs = 0;
    private long _fpsLastFrameMs = 0;
    private double _fpsMinMs = double.MaxValue;
    private double _fpsMaxMs = 0;
    private double _fpsMinMsDisplay = 0;
    private double _fpsMaxMsDisplay = 0;

    private void TickFpsCounter()
    {
        long nowMs = _fpsClock.ElapsedMilliseconds;

        if (_fpsLastFrameMs != 0)
        {
            double delta = nowMs - _fpsLastFrameMs;
            if (delta < _fpsMinMs) _fpsMinMs = delta;
            if (delta > _fpsMaxMs) _fpsMaxMs = delta;
        }
        _fpsLastFrameMs = nowMs;

        _fpsFrames++;
        long elapsed = nowMs - _fpsWindowStartMs;
        if (elapsed >= 500)
        {
            _fps = _fpsFrames * 1000.0 / elapsed;
            _fpsFrames = 0;
            _fpsWindowStartMs = nowMs;
            _fpsMinMsDisplay = _fpsMinMs == double.MaxValue ? 0 : _fpsMinMs;
            _fpsMaxMsDisplay = _fpsMaxMs;
            _fpsMinMs = double.MaxValue;
            _fpsMaxMs = 0;
        }
    }

    private void DrawFpsOverlay(SKCanvas canvas, float w)
    {
        canvas.DrawText($"Paint: {_fps:F1}fps ({_fpsMinMsDisplay:F0}-{_fpsMaxMsDisplay:F0}ms)", w - 8, 16, SKTextAlign.Right, FontFps, PaintFps);
        canvas.DrawText($"Tick:  {_tickHz:F1}hz ({_tickMinMsDisplay:F0}-{_tickMaxMsDisplay:F0}ms)", w - 8, 32, SKTextAlign.Right, FontFps, PaintFps);
    }

    // ── Medidor de cadencia de Tick() (temporal) ────────────────────────────
    // Mide el intervalo real entre llamadas a Tick() desde MainPage —
    // separado a propósito del medidor de paint de arriba. Si el jitter
    // está acá y no en OnPaintSurface, el problema es el IDispatcherTimer /
    // la lectura del buffer de audio, no el render.
    private readonly System.Diagnostics.Stopwatch _tickClock = System.Diagnostics.Stopwatch.StartNew();
    private long _tickLastMs = 0;
    private int _tickCount = 0;
    private double _tickHz = 0;
    private long _tickWindowStartMs = 0;
    private double _tickMinMs = double.MaxValue;
    private double _tickMaxMs = 0;
    private double _tickMinMsDisplay = 0;
    private double _tickMaxMsDisplay = 0;

    private void MeasureTickCadence()
    {
        long nowMs = _tickClock.ElapsedMilliseconds;
        if (_tickLastMs != 0)
        {
            double delta = nowMs - _tickLastMs;
            if (delta < _tickMinMs) _tickMinMs = delta;
            if (delta > _tickMaxMs) _tickMaxMs = delta;
        }
        _tickLastMs = nowMs;

        _tickCount++;
        long elapsed = nowMs - _tickWindowStartMs;
        if (elapsed >= 500)
        {
            _tickHz = _tickCount * 1000.0 / elapsed;
            _tickCount = 0;
            _tickWindowStartMs = nowMs;
            _tickMinMsDisplay = _tickMinMs == double.MaxValue ? 0 : _tickMinMs;
            _tickMaxMsDisplay = _tickMaxMs;
            _tickMinMs = double.MaxValue;
            _tickMaxMs = 0;
        }
    }

    // ── Foco de canal (misma lógica que SpectrumPanel) ─────────────────────
    // volatile: se escribe desde el hilo de UI (OnTouch) y se lee desde el
    // hilo de render GL (OnPaintSurface).
    private volatile int _focusedChannel = -1;
    private int _nextChannelToCopy;
    private volatile bool _triggered = Preferences.Default.Get(TriggerPreferenceKey, true);
    private bool _triggerTouchConsumed;

    // Paths persistentes y agrupados, portados de Desktop: el MIX, el canal
    // enfocado y todos los canales normales se dibujan con sólo tres paths
    // reutilizables. La vista multicanal usa una única llamada DrawPath.
    private readonly SKPath _mixPath = new();
    private readonly SKPath _focusPath = new();
    private readonly SKPath _channelsPath = new();

    public event Action? VisualTapped;

    public OscilloscopePanel()
    {
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;
        EnableTouchEvents = true;
        PaintSurface += OnPaintSurface;
        Touch += OnTouch;
    }

    private void OnTouch(object? sender, SKTouchEventArgs e)
    {
        if (e.ActionType == SKTouchAction.Released)
        {
            if (!_triggerTouchConsumed)
                VisualTapped?.Invoke();
            _triggerTouchConsumed = false;
            e.Handled = true;
            return;
        }
        if (e.ActionType != SKTouchAction.Pressed)
        {
            e.Handled = true;
            return;
        }
        e.Handled = true;

        if (_source == null) return;
        var buffers = _buffers;
        if (buffers == null || buffers.ChannelCount == 0) return;

        float w = CanvasSize.Width, h = CanvasSize.Height;
        if (w <= 0 || h <= 0) return;

        // IMPORTANTE: probar el botón ANTES de interpretar un toque en la
        // vista enfocada como "volver a MIX". En 0.12.1 el early-return de
        // foco estaba antes del hit-test, así que tocar TRIGGER en un canal
        // individual inevitablemente cerraba el foco. Maravillosa ergonomía.
        // El hitbox sigue siendo exactamente el mismo rectángulo dibujado.
        var triggerRect = GetTriggerButtonRect(w, h, buffers.ChannelCount, _focusedChannel);
        if (triggerRect.Contains(e.Location.X, e.Location.Y))
        {
            _triggered = !_triggered;
            Preferences.Default.Set(TriggerPreferenceKey, _triggered);
            _triggerTouchConsumed = true;
            InvalidateSurface();
            return;
        }

        if (_focusedChannel >= 0)
        {
            _focusedChannel = -1;
            InvalidateSurface();
            return;
        }

        // Botón grande y apartado de las métricas. Antes vivía arriba a la
        // derecha, justo donde PEAK/CLIP podía tapar el área táctil. Ahora se
        // coloca en la esquina inferior derecha de la zona MIX (o del panel
        // completo cuando hay un canal enfocado), con un hitbox idéntico a
        // su representación visual.
        float mixZoneH = h * MixZoneFraction;
        if (e.Location.Y <= mixZoneH) return; // toque en la zona MIX: sin acción

        float chW = w / buffers.ChannelCount;
        int idx = (int)(e.Location.X / chW);
        if (idx < 0 || idx >= buffers.ChannelCount) return;

        _focusedChannel = idx;
        InvalidateSurface();
    }

    // ── Ciclo de vida ────────────────────────────────────────────────────

    public void Attach(IPanelAudioSource? source)
    {
        _focusedChannel = -1;
        _nextChannelToCopy = 0;
        _source = source;
        _buffers = source != null ? new PanelBuffers(source.ChannelCount) : null;
        InvalidateSurface();
    }

    public void Detach()
    {
        _source = null;
        _nextChannelToCopy = 0;
        _buffers = null;
        _focusedChannel = -1;
        InvalidateSurface();
    }

    /// <summary>Llamado periódicamente por MainPage mientras este panel está visible.</summary>
    public void Tick()
    {
        if (ShowPerformanceOverlay) MeasureTickCadence();

        var source = _source;
        var buffers = _buffers;
        if (source == null || buffers == null) return;

        var mixBack = buffers.MixBack;
        source.CopyMixSamples(mixBack.Samples);
        mixBack.TriggerOffset = _triggered ? FindTrigger(mixBack.Samples) : 0;
        buffers.MixBack = System.Threading.Interlocked.Exchange(ref buffers.MixFront, mixBack);
        if (MeasurementLabelsSettings.Enabled)
        {
            source.CopyMeterSamples(buffers.MeterBuf);
            buffers.Metrics = SignalMetricsCalculator.Calculate(buffers.MeterBuf);
        }
        if (buffers.Metrics.Clipped)
            {
                long now = Environment.TickCount64;
                if (now >= buffers.ClipHoldUntilMs) buffers.ClipHeldPeak = 0f;
                buffers.ClipHeldPeak = Math.Max(buffers.ClipHeldPeak, buffers.Metrics.Peak);
                buffers.ClipHoldUntilMs = now + 1500;
            }
        int focused = _focusedChannel;
        if (focused >= 0 && focused < buffers.ChannelCount)
        {
            var chBack = buffers.ChBack[focused];
            source.CopyChannelSamples(focused, chBack.Samples);
            chBack.TriggerOffset = _triggered ? FindTrigger(chBack.Samples) : 0;
            buffers.ChBack[focused] = System.Threading.Interlocked.Exchange(ref buffers.ChFront[focused], chBack);
        }
        else if (buffers.ChannelCount > 0)
        {
            // Copiar todos los canales en cada frame era el mayor costo del
            // osciloscopio en MIDIs grandes. Se actualizan dos por tick y el
            // MIX conserva la cadencia completa; visualmente es suficiente.
            int budget = Math.Min(2, buffers.ChannelCount);
            for (int n = 0; n < budget; n++)
            {
                int i = _nextChannelToCopy++;
                if (_nextChannelToCopy >= buffers.ChannelCount) _nextChannelToCopy = 0;
                var chBack = buffers.ChBack[i];
                source.CopyChannelSamples(i, chBack.Samples);
                chBack.TriggerOffset = _triggered ? FindTrigger(chBack.Samples) : 0;
                buffers.ChBack[i] = System.Threading.Interlocked.Exchange(ref buffers.ChFront[i], chBack);
            }
        }

        // Tick() ahora corre en un hilo de background (ver StartPanelTimer
        // en MainPage) — la copia de muestras de arriba se hace ahí mismo,
        // fuera del hilo de UI. Lo único que necesita el hilo de UI es
        // enterarse de que hay un frame nuevo para pintar; por eso solo se
        // marshalea esta llamada puntual y liviana, no todo Tick().
        MainThread.BeginInvokeOnMainThread(InvalidateSurface);
    }

    // ── Render ───────────────────────────────────────────────────────────

    private void OnPaintSurface(object? sender, SKPaintGLSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(BgColor);
        if (ShowPerformanceOverlay) TickFpsCounter();

        // Ver el comentario equivalente en SpectrumPanel.OnPaintSurface:
        // CanvasSize (píxeles reales) solo es válido leerlo acá adentro,
        // no desde SizeChanged.
        float w = CanvasSize.Width, h = CanvasSize.Height;
        if (w <= 0 || h <= 0) return;

        // Snapshot local — ver comentario equivalente en SpectrumPanel.
        var buffers = _buffers;

        if (_source == null || buffers == null)
        {
            canvas.DrawText("Osciloscopio — sin señal de audio", w / 2f, h / 2f, SKTextAlign.Center, FontIdle, PaintIdle);
            if (ShowPerformanceOverlay) DrawFpsOverlay(canvas, w);
            return;
        }

        int channelCount = buffers.ChannelCount;
        int focused = _focusedChannel;

        // Canal en primer plano: ocupa todo el panel, igual que en SpectrumPanel.
        if (focused >= 0 && focused < channelCount)
        {
            var fullZone = new SKRect(0, 0, w, h);
            DrawCenterLine(canvas, fullZone);
            var focusedFrame = System.Threading.Volatile.Read(ref buffers.ChFront[focused]);
            BuildWavePath(_focusPath, fullZone, focusedFrame.Samples, focusedFrame.TriggerOffset, reset: true);
            canvas.DrawPath(_focusPath, PaintChCore);
            canvas.DrawText($"{buffers.ChLabels[focused]} — toca para volver a MIX", 6, 16, SKTextAlign.Left, FontLabel, PaintLabel);
            DrawTriggerToggle(canvas, w, h, channelCount, focused);
            DrawSignalMetrics(canvas, w, buffers);
            if (ShowPerformanceOverlay) DrawFpsOverlay(canvas, w);
            return;
        }

        float mixZoneH = channelCount > 0 ? h * MixZoneFraction : h;
        var mixZone = new SKRect(0, 0, w, mixZoneH);
        DrawGrid(canvas, mixZone);
        var mixFrame = System.Threading.Volatile.Read(ref buffers.MixFront);
        BuildWavePath(_mixPath, mixZone, mixFrame.Samples, mixFrame.TriggerOffset, reset: true);
        canvas.DrawPath(_mixPath, PaintMixCore);
        canvas.DrawText("MIX", 6, 16, SKTextAlign.Left, FontLabel, PaintLabel);
        DrawTriggerToggle(canvas, w, h, channelCount, focused);

        if (channelCount > 0)
        {
            canvas.DrawRect(new SKRect(0, mixZoneH, w, mixZoneH + 1), PaintSep);

            float chTop = mixZoneH + 1;
            float chH = h - chTop;
            float chW = w / channelCount;

            _channelsPath.Reset();
            for (int i = 0; i < channelCount; i++)
            {
                float x = i * chW;
                var zone = new SKRect(x, chTop, x + chW, chTop + chH);
                DrawCenterLine(canvas, zone);
                var channelFrame = System.Threading.Volatile.Read(ref buffers.ChFront[i]);
                BuildWavePath(_channelsPath, zone, channelFrame.Samples, channelFrame.TriggerOffset, reset: false);

                if (i > 0)
                    canvas.DrawRect(new SKRect(x, chTop, x + 1, h), PaintSep);

                canvas.DrawText(buffers.ChLabels[i], x + 4, chTop + 14, SKTextAlign.Left, FontLabel, PaintLabel);
            }
            canvas.DrawPath(_channelsPath, PaintChCore);
        }

        DrawSignalMetrics(canvas, w, buffers);
        if (ShowPerformanceOverlay) DrawFpsOverlay(canvas, w);
    }

    private static SKRect GetTriggerButtonRect(float w, float h, int channelCount, int focusedChannel)
    {
        // En MIX queda pegado a la parte baja de su propia zona, lejos de las
        // métricas de la esquina superior. En foco usa la esquina inferior
        // derecha del panel completo.
        float zoneBottom = focusedChannel >= 0 || channelCount == 0
            ? h
            : h * MixZoneFraction;
        float right = w - TriggerButtonMargin;
        float bottom = zoneBottom - TriggerButtonMargin;
        return new SKRect(
            right - TriggerButtonWidth,
            bottom - TriggerButtonHeight,
            right,
            bottom);
    }

    private void DrawTriggerToggle(SKCanvas canvas, float w, float h, int channelCount, int focusedChannel)
    {
        var bounds = GetTriggerButtonRect(w, h, channelCount, focusedChannel);

        // Zero allocations in the per-frame path. Colors are cheap value-type
        // updates and preserve live Light/Dark theme changes.
        PaintTriggerFill.Color = _triggered ? MixCoreColor.WithAlpha(58) : GridColor.WithAlpha(175);
        PaintTriggerStroke.Color = _triggered ? MixCoreColor : GridCenterColor;
        PaintTriggerText.Color = LabelColor;
        var metrics = TriggerFont.Metrics;

        canvas.DrawRoundRect(bounds, 12f, 12f, PaintTriggerFill);
        canvas.DrawRoundRect(bounds, 12f, 12f, PaintTriggerStroke);
        canvas.DrawText(
            _triggered ? "TRIGGER ON" : "TRIGGER OFF",
            bounds.MidX,
            bounds.MidY - (metrics.Ascent + metrics.Descent) / 2f,
            SKTextAlign.Center,
            TriggerFont,
            PaintTriggerText);
    }

    private static void DrawSignalMetrics(SKCanvas canvas, float w, PanelBuffers buffers)
    {
        if (!MeasurementLabelsSettings.Enabled) return;

        var m = buffers.Metrics;
        const float x = 8f;
        const float firstY = 30f;
        const float lineH = 27f;
        canvas.DrawText($"RMS     {m.Rms:F3}", x, firstY, SKTextAlign.Left, FontMeter, PaintMeter);
        canvas.DrawText($"RMS dB  {m.RmsDb:F1} dB", x, firstY + lineH, SKTextAlign.Left, FontMeter, PaintMeter);
        canvas.DrawText($"PEAK    {m.Peak:F3} ({m.PeakDb:+0.0;-0.0;0.0} dB)", x, firstY + lineH * 2, SKTextAlign.Left, FontMeter, PaintMeter);
        canvas.DrawText($"CREST   {m.CrestDb:F1} dB", x, firstY + lineH * 3, SKTextAlign.Left, FontMeter, PaintMeter);
        bool clipVisible = Environment.TickCount64 < buffers.ClipHoldUntilMs;
        canvas.DrawText(clipVisible ? $"CLIP {buffers.ClipHeldPeak:F3}" : "PEAK OK", w - 8, firstY, SKTextAlign.Right, FontMeter, clipVisible ? PaintClip : PaintMeter);
    }

    private static void DrawGrid(SKCanvas canvas, SKRect zone)
    {
        const int vLines = 8;
        const int hLines = 4;

        for (int i = 1; i < vLines; i++)
        {
            float x = zone.Left + zone.Width * i / vLines;
            canvas.DrawLine(x, zone.Top, x, zone.Bottom, PaintGrid);
        }
        for (int i = 1; i < hLines; i++)
        {
            float y = zone.Top + zone.Height * i / hLines;
            canvas.DrawLine(zone.Left, y, zone.Right, y, PaintGrid);
        }

        float midY = zone.Top + zone.Height / 2f;
        canvas.DrawLine(zone.Left, midY, zone.Right, midY, PaintGridCenter);
    }

    private static void DrawCenterLine(SKCanvas canvas, SKRect zone)
    {
        float midY = zone.Top + zone.Height / 2f;
        canvas.DrawLine(zone.Left, midY, zone.Right, midY, PaintGrid);
    }

    /// <summary>
    /// Trigger ascendente con histéresis (tipo Schmitt). No basta con que dos
    /// muestras diminutas cambien de signo: la señal primero debe bajar por
    /// debajo de -threshold y después superar +threshold. Así el ruido, DC
    /// residual y los cruces microscópicos alrededor de cero no pueden elegir
    /// alegremente un punto distinto en cada frame.
    ///
    /// El threshold es adaptativo: 8% del pico de la ventana, con un suelo
    /// pequeño para rechazar ruido y un techo para no volver imposible el
    /// trigger en señales muy fuertes. Una vez validado el flanco, se devuelve
    /// el cruce por cero real dentro de esa transición, no el punto +threshold.
    /// </summary>
    private static int FindTrigger(float[] samples)
    {
        int half = samples.Length / 2;
        if (half < 4) return 0;

        float peak = 0f;
        for (int i = 0; i < half; i++)
        {
            float a = MathF.Abs(samples[i]);
            if (a > peak) peak = a;
        }

        // Señal prácticamente silenciosa: no perseguir ruido.
        if (peak < 0.006f) return 0;

        float threshold = Math.Clamp(peak * 0.08f, 0.006f, 0.08f);
        bool armed = false;
        int zeroCross = -1;

        for (int i = 1; i < half; i++)
        {
            float prev = samples[i - 1];
            float cur = samples[i];

            if (!armed)
            {
                if (cur <= -threshold)
                {
                    armed = true;
                    zeroCross = -1;
                }
                continue;
            }

            if (zeroCross < 0 && prev <= 0f && cur > 0f)
                zeroCross = i;

            if (cur >= threshold)
                return zeroCross >= 0 ? zeroCross : i;

            // Si vuelve a caer claramente, seguimos armados pero descartamos
            // un cruce parcial anterior: aún no fue un flanco válido.
            if (cur <= -threshold)
                zeroCross = -1;
        }

        return 0;
    }

    private static void BuildWavePath(SKPath path, SKRect zone, float[] samples, int start, bool reset)
    {
        if (reset) path.Reset();
        if (zone.Width <= 0 || zone.Height <= 0) return;

        int available = samples.Length - start;
        int sourceCount = Math.Min(available, samples.Length * 3 / 4);
        if (sourceCount < 2) return;

        // No tiene sentido dibujar más segmentos que píxeles horizontales.
        // En la vista multicanal cada zona puede medir apenas decenas de px;
        // antes se enviaban ~384 segmentos por canal aunque casi todos
        // terminaran en el mismo píxel.
        int count = Math.Min(sourceCount, Math.Max(2, (int)zone.Width));
        float sourceStep = (sourceCount - 1f) / (count - 1f);
        float stepX = zone.Width / (count - 1f);
        float halfH = zone.Height * 0.5f;
        float midY = zone.Top + halfH;

        for (int i = 0; i < count; i++)
        {
            int sampleIndex = start + (int)(i * sourceStep);
            float sample = samples[sampleIndex];
            if (sample > 1f) sample = 1f;
            else if (sample < -1f) sample = -1f;

            float x = zone.Left + i * stepX;
            float y = midY - sample * halfH * 0.92f;
            if (i == 0) path.MoveTo(x, y);
            else path.LineTo(x, y);
        }

    }
}