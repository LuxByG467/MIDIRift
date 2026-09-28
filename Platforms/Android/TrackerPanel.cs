using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
using System;
using System.Collections.Generic;

namespace MIDIRift;

public class TrackerPanel : SKGLView
{
    // ── Layout ────────────────────────────────────────────────────────────
    private const float RowHeight = 22f;
    public const float ChannelColumnWidth = 90f;
    private const float ColWidth = ChannelColumnWidth;
    private const float HeaderH = 32f;

    // ── Colores ───────────────────────────────────────────────────────────
    private static SKColor BgColor = new(18, 18, 18);
    private static SKColor AltRowColor = new(24, 24, 24);
    private static SKColor HoldCellBg = new(30, 30, 60);
    private static SKColor TextColor = new(200, 220, 200);
    private static SKColor HoldTextColor = new(130, 130, 180);
    private static SKColor EmptyTextColor = new(60, 60, 60);
    private static SKColor HeaderBg = new(24, 24, 24);
    private static SKColor HeaderTextColor = new(160, 160, 160);
    private static SKColor WaveLabelColor = new(80, 160, 80);
    private static SKColor PlayheadColor = new(80, 160, 80);
    private static SKColor PlayheadGlow = new(40, 255, 255, 120);
    private static SKColor HeaderSepColor = new(35, 35, 35);

    // ── Paints ───────────────────────────────────────────────────────────
    private static readonly SKPaint PaintBg = MakeFill(BgColor);
    private static readonly SKPaint PaintAlt = MakeFill(AltRowColor);
    private static readonly SKPaint PaintHoldBg = MakeFill(HoldCellBg);
    private static readonly SKPaint PaintHeader = MakeFill(HeaderBg);
    private static readonly SKPaint PaintHeaderSep = MakeFill(HeaderSepColor);
    private static readonly SKPaint PaintPlayhead = MakeFill(PlayheadColor);
    private static readonly SKPaint PaintGlow = MakeFill(PlayheadGlow);
    internal static readonly SKPaint PaintText = MakeText(TextColor);
    internal static readonly SKPaint PaintHoldText = MakeText(HoldTextColor);
    internal static readonly SKPaint PaintEmptyText = MakeText(EmptyTextColor);
    private static readonly SKPaint PaintHeaderTxt = MakeText(HeaderTextColor);
    private static readonly SKPaint PaintWaveLabel = MakeText(WaveLabelColor);

    // Fuentes: en SkiaSharp 4.151.x, SKPaint.TextSize/Typeface/TextAlign se
    // eliminaron — el tamaño y la tipografía ahora viven en SKFont, que se
    // pasa aparte en cada DrawText(). PaintText/PaintHoldText/PaintEmptyText/
    // PaintHeaderTxt comparten los mismos 12f, así que reusan FontGrid.
    private static readonly SKTypeface MonospaceTypeface = SKTypeface.FromFamilyName("monospace");
    internal static readonly SKFont FontGrid = new(MonospaceTypeface, 12f);
    private static readonly SKFont FontWaveLabel = new(MonospaceTypeface, 10f);

    // Sampling para los blits de bitmap cacheado. Se usa vía DrawImage (no
    // DrawBitmap): en SkiaSharp 3.x, SKPaint.FilterQuality está obsoleto y
    // DrawBitmap no tiene overload con SKSamplingOptions — DrawImage sí.
    // Linear liviano: solo importa para el offset vertical fraccional del
    // scroll (offsetY con decimales); horizontalmente siempre es 1:1.
    private static readonly SKSamplingOptions BlitSampling =
        new(SKFilterMode.Linear, SKMipmapMode.None);

    private static SKPaint MakeFill(SKColor c) =>
        new() { Color = c, IsAntialias = false };

    private static SKPaint MakeText(SKColor c) =>
        new()
        {
            Color = c,
            IsAntialias = true,
        };

    // ── Grid pre-renderizado ──────────────────────────────────────────────
    private RenderCell[][]? _grid;
    private int _columnCount = 0;
    private List<WaveType> _waveTypes = new();

    private string[] _headerChLabels = Array.Empty<string>();
    private string[] _headerWaveLabels = Array.Empty<string>();
    private bool[] _percussionChannels = Array.Empty<bool>();

    // ── Geometría cacheada ────────────────────────────────────────────────
    private float _scale = 1f;
    private float _viewW = 0f;
    private float _viewH = 0f;
    private float _playheadY = 0f;
    private int _visibleRows = 0;
    private int _halfVisible = 0;
    private SKRect _rowClipRect = SKRect.Empty;

    // ── Scroll horizontal ────────────────────────────────────────────────
    private float _scrollX = 0f;
    private float _contentW = 0f;
    private float _maxScrollX = 0f;

    private void RecomputeScrollBounds()
    {
        _contentW = _columnCount * ColWidth;
        _maxScrollX = Math.Max(0, _contentW - _viewW);
        _scrollX = Math.Clamp(_scrollX, 0, _maxScrollX);
    }

    // ── ContinuousRow ─────────────────────────────────────────────────────
    private double _lastContinuousRow = -1;
    private double _continuousRow = 0;
    private const double RedrawThreshold = 0.05;

    public static readonly BindableProperty ContinuousRowProperty =
        BindableProperty.Create(
            nameof(ContinuousRow),
            typeof(double),
            typeof(TrackerPanel),
            0.0,
            propertyChanged: (b, _, n) => ((TrackerPanel)b).SetContinuousRow((double)n));

    public double ContinuousRow
    {
        get => (double)GetValue(ContinuousRowProperty);
        set => SetValue(ContinuousRowProperty, value);
    }

    private void SetContinuousRow(double value)
    {
        _continuousRow = value;
        if (Math.Abs(value - _lastContinuousRow) >= RedrawThreshold)
        {
            _lastContinuousRow = value;
            InvalidateSurface();
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // ── Bitmap cache de filas (port del esquema de TrackerRowsPanel/desktop)
    // ════════════════════════════════════════════════════════════════════
    // Las filas son estáticas mientras suena la canción — lo único que cambia
    // en cada frame es la posición fraccional del playhead (_continuousRow).
    // En vez de re-dibujar texto de cada celda visible en CADA frame (el
    // problema original: SKCanvasView es rasterizado por CPU y DrawText es
    // caro en Android), renderizamos una VENTANA de filas a un SKBitmap
    // offscreen una sola vez, y en OnPaintSurface normalmente solo hacemos
    // un DrawBitmap (blit) con desplazamiento vertical continuo.
    private SKBitmap? _rowsCacheFront;  // el que se está mostrando
    private SKBitmap? _rowsCacheBack;   // el que se está (re)construyendo
    private SKCanvas? _rebuildCanvas;   // canvas sobre _rowsCacheBack, vivo durante todo el rebuild

    // Snapshot inmutable del front para blitear con DrawImage (ver nota de
    // BlitSampling). Se recrea SOLO cuando el front cambia (swap), nunca
    // por frame — recrearlo por frame anularía el propósito del cache.
    private SKImage? _rowsCacheFrontImage;

    // Dimensiones/ancla del FRONT — solo se actualizan en el swap.
    private int _cacheW, _cacheH;
    private int _cacheAnchorRow = -1;
    private int _cacheWindowRows = 0;
    private int _cacheAnchorCol = -1;
    private int _cacheWindowCols = 0;

    // Dimensiones del BACK — separadas a propósito (ver nota en desktop:
    // si se comparte con el front, un rebuild a mitad de camino hace que
    // Render() calcule el sourceRect contra el tamaño equivocado).
    private int _backCacheW, _backCacheH;

    private bool _cacheDirty = true;

    private bool _rebuildInProgress = false;
    private int _rebuildAnchorRow = 0;
    private int _rebuildWindowRows = 0;
    private int _rebuildAnchorCol = 0;
    private int _rebuildWindowCols = 0;
    private int _rebuildNextRow = 0;
    private int _rebuildBmpW = 0;

    // Presupuesto por frame más chico que en desktop: DrawText de SkiaSharp
    // sobre SKCanvasView (CPU) en un dispositivo Android promedio es más
    // caro por llamada que el DrawText de Avalonia/Skia en desktop, y hay
    // MaxRowsPerFrame*columnCount llamadas por chunk. Ajustable si hace
    // falta más margen (ver PerformanceProfiler si lo migran a Android).
    //
    // MaxBufferRows se redujo de 300 a 150 al agregar el buffer de
    // columnas: el bitmap ahora es 2D (filas × columnas cacheadas), y si
    // ambos buffers fueran grandes a la vez la memoria del cache (front +
    // back) se dispara. 150 filas de buffer siguen siendo varios segundos
    // de reproducción antes de necesitar rebuild — el scroll horizontal es
    // un gesto discreto del usuario, no una animación continua de 30fps
    // como el playhead, así que su buffer puede ser bastante más chico.
    private const int BufferMultiplier = 3;
    private const int SafetyMargin = 8;
    private const int MaxBufferRows = 150;
    private const int MaxRowsPerFrame = 40;

    private const int ColBufferMultiplier = 2;
    private const int ColSafetyMarginCols = 2;
    private const int MaxBufferCols = 6;

    private int _deferFirstBuildFrames = 0;

    // ── Header cacheado ───────────────────────────────────────────────────
    // El header en esta versión no scrollea horizontalmente, así que no hace
    // falta redibujarlo cada frame — solo cuando cambian labels/columnas.
    private SKBitmap? _headerCache;
    private SKImage? _headerCacheImage;
    private bool _headerDirty = true;

    private void DrawSignalMetrics(SKCanvas canvas)
    {
        if (!MeasurementLabelsSettings.Enabled) return;

        var m = _signalMetrics;
        const float x = 8f;
        const float lineH = 18f;
        float firstY = HeaderH + 18f;
        canvas.DrawText($"RMS     {m.Rms:F3}", x, firstY, SKTextAlign.Left, FontMeter, PaintMeter);
        canvas.DrawText($"RMS dB  {m.RmsDb:F1} dB", x, firstY + lineH, SKTextAlign.Left, FontMeter, PaintMeter);
        canvas.DrawText($"PEAK    {m.Peak:F3} ({m.PeakDb:+0.0;-0.0;0.0} dB)", x, firstY + lineH * 2, SKTextAlign.Left, FontMeter, PaintMeter);
        canvas.DrawText($"CREST   {m.CrestDb:F1} dB", x, firstY + lineH * 3, SKTextAlign.Left, FontMeter, PaintMeter);
        bool clipVisible = Environment.TickCount64 < _clipHoldUntilMs;
        canvas.DrawText(clipVisible ? $"CLIP {_clipHeldPeak:F3}" : "PEAK OK", _viewW - 8, firstY, SKTextAlign.Right, FontMeter, clipVisible ? PaintClip : PaintMeter);
    }

    // ── Dropdown de hot-swap de WaveType ─────────────────────────────────
    // Se abre tocando el label de wave type en el header de una columna.
    // No usa cache (se dibuja poco tiempo, solo mientras está abierto).
    private static readonly WaveType[] AllWaveTypes = (WaveType[])Enum.GetValues(typeof(WaveType));
    private const float DropdownItemH = 22f;
    private const float DropdownW = 60f;

    private int _openDropdownCol = -1;
    private SKRect[] _dropdownItemRects = Array.Empty<SKRect>();
    private int _dropdownHoverItem = -1;

    // ── Drag horizontal (scroll) ──────────────────────────────────────────
    private bool _dragCandidate = false;
    private bool _isDragging = false;
    private float _touchStartX, _touchStartY;
    private float _dragStartScrollX;
    private const float DragThresholdPx = 12f;

    private static readonly SKPaint PaintDropdownBg = MakeFill(new SKColor(20, 20, 28, 245));
    private static readonly SKPaint PaintDropdownBorder = MakeFill(new SKColor(80, 160, 80));
    private static readonly SKPaint PaintDropdownHover = MakeFill(new SKColor(50, 80, 50));
    private static readonly SKPaint PaintDropdownText = MakeText(new SKColor(210, 220, 210));
    private static readonly SKPaint PaintDropdownTextActive = MakeText(new SKColor(120, 220, 120));
    private static readonly SKFont FontDropdown = new(MonospaceTypeface, 11f);
    private static readonly SKPaint PaintMeter = MakeText(new SKColor(118, 156, 128));
    private static readonly SKPaint PaintClip = MakeText(new SKColor(255, 72, 72));
    private static readonly SKFont FontMeter = new(MonospaceTypeface, 14f);
    private SignalMetrics _signalMetrics = SignalMetrics.Silence;
    private long _clipHoldUntilMs;
    private float _clipHeldPeak;

    /// <summary>Se dispara cuando el usuario elige un WaveType nuevo desde el dropdown del header.</summary>
    public event Action<int, WaveType>? OnWaveTypeChanged;
    public event Action<int>? PatchHeaderTapped;

    private bool _waveTypeEditingEnabled = true;
    public bool WaveTypeEditingEnabled
    {
        get => _waveTypeEditingEnabled;
        set
        {
            if (_waveTypeEditingEnabled == value) return;
            _waveTypeEditingEnabled = value;
            RebuildHeaderLabels();
            InvalidateSurface();
        }
    }

    /// <summary>Se dispara cuando cambia el desplazamiento horizontal del tracker.</summary>
    public event Action<float>? HorizontalScrollChanged;
    public event Action? VisualTapped;

    public float HorizontalScrollOffset => _scrollX;

    public void SetSignalMetrics(SignalMetrics metrics)
    {
        _signalMetrics = metrics;
        if (metrics.Clipped)
        {
            long now = Environment.TickCount64;
            if (now >= _clipHoldUntilMs) _clipHeldPeak = 0f;
            _clipHeldPeak = Math.Max(_clipHeldPeak, metrics.Peak);
            _clipHoldUntilMs = now + 1500;
        }
        InvalidateSurface();
    }


    public void ApplyTheme(bool light)
    {
        BgColor = light ? new SKColor(250, 249, 252) : new SKColor(18, 18, 18);
        AltRowColor = light ? new SKColor(241, 238, 247) : new SKColor(24, 24, 24);
        HoldCellBg = light ? new SKColor(226, 220, 241) : new SKColor(30, 30, 60);
        TextColor = light ? new SKColor(43, 55, 45) : new SKColor(200, 220, 200);
        HoldTextColor = light ? new SKColor(92, 82, 128) : new SKColor(130, 130, 180);
        EmptyTextColor = light ? new SKColor(174, 168, 184) : new SKColor(60, 60, 60);
        HeaderBg = light ? new SKColor(232, 228, 239) : new SKColor(24, 24, 24);
        HeaderTextColor = light ? new SKColor(74, 67, 84) : new SKColor(160, 160, 160);
        WaveLabelColor = light ? new SKColor(45, 126, 67) : new SKColor(80, 160, 80);
        PlayheadColor = light ? new SKColor(45, 139, 76) : new SKColor(80, 160, 80);
        PlayheadGlow = light ? new SKColor(91, 189, 122, 80) : new SKColor(40, 255, 255, 120);
        HeaderSepColor = light ? new SKColor(201, 194, 211) : new SKColor(35, 35, 35);
        PaintBg.Color=BgColor; PaintAlt.Color=AltRowColor; PaintHoldBg.Color=HoldCellBg; PaintHeader.Color=HeaderBg; PaintHeaderSep.Color=HeaderSepColor;
        PaintPlayhead.Color=PlayheadColor; PaintGlow.Color=PlayheadGlow; PaintText.Color=TextColor; PaintHoldText.Color=HoldTextColor; PaintEmptyText.Color=EmptyTextColor;
        PaintHeaderTxt.Color=HeaderTextColor; PaintWaveLabel.Color=WaveLabelColor;
        PaintDropdownBg.Color = light ? new SKColor(250,249,252,248) : new SKColor(20,20,28,245);
        PaintDropdownBorder.Color = light ? new SKColor(72,145,88) : new SKColor(80,160,80);
        PaintDropdownHover.Color = light ? new SKColor(218,235,221) : new SKColor(50,80,50);
        PaintDropdownText.Color = light ? new SKColor(45,52,47) : new SKColor(210,220,210);
        PaintDropdownTextActive.Color = light ? new SKColor(42,126,62) : new SKColor(120,220,120);
        PaintMeter.Color = light ? new SKColor(56,112,70) : new SKColor(118,156,128);
        ClearRowsCacheForNewGrid();
        _cacheDirty = true;
        _headerDirty = true;
        InvalidateSurface();
    }

    // ── Constructor ───────────────────────────────────────────────────────

    public TrackerPanel()
    {
        PaintSurface += OnPaintSurface;
        SizeChanged += OnSizeChanged;
        // OJO: NO setear BackgroundColor acá. SKGLView usa un TextureView
        // por debajo en Android, y TextureView no soporta pintar un
        // background drawable — tirar UnsupportedOperationException.
        // canvas.Clear(BgColor) en OnPaintSurface ya pinta el fondo real;
        // como mucho hay un frame en blanco antes del primer paint.

        EnableTouchEvents = true;
        Touch += OnTouch;
    }

    private void OnSizeChanged(object? sender, EventArgs e)
    {
        if (Width <= 0 || Height <= 0) return;
        _scale = CanvasSize.Width > 0 ? (float)(CanvasSize.Width / Width) : 1f;
        _viewW = (float)Width;
        _viewH = (float)Height;
        _playheadY = (_viewH - HeaderH) / 2f + HeaderH;
        _visibleRows = (int)((_viewH - HeaderH) / RowHeight) + 2;
        _halfVisible = _visibleRows / 2;
        _rowClipRect = new SKRect(0, HeaderH, _viewW, _viewH);

        RecomputeScrollBounds();

        _cacheDirty = true;
        _headerDirty = true;
        InvalidateSurface();
    }

    // ── Public API ────────────────────────────────────────────────────────

    public void LoadGrid(
        GridRow[][] gridData,
        int channelCount,
        Func<float, string> freqToNote,
        List<WaveType>? waveTypes = null)
    {
        _columnCount = channelCount;
        _waveTypes = waveTypes != null ? new List<WaveType>(waveTypes) : new();
        _percussionChannels = new bool[channelCount];

        int rowCount = gridData.Length;
        _grid = new RenderCell[rowCount][];

        var noteBuffer = new System.Text.StringBuilder(40);

        for (int r = 0; r < rowCount; r++)
        {
            var srcRow = gridData[r];
            int cols = Math.Min(channelCount, srcRow.Length);
            var dstRow = new RenderCell[cols];

            for (int ch = 0; ch < cols; ch++)
            {
                ref readonly var cell = ref srcRow[ch];
                string text;
                byte paintIdx;

                if (cell.FreqCount == 0)
                {
                    text = "---";
                    paintIdx = RenderCell.PaintEmpty;
                }
                else if (cell.IsHold)
                {
                    text = "...";
                    paintIdx = RenderCell.PaintHold;
                }
                else
                {
                    noteBuffer.Clear();
                    for (int ni = 0; ni < cell.FreqCount; ni++)
                    {
                        if (ni > 0) noteBuffer.Append(',');
                        noteBuffer.Append(freqToNote(cell.GetFreq(ni)));
                    }
                    text = noteBuffer.ToString();
                    paintIdx = RenderCell.PaintNormal;
                }

                dstRow[ch] = new RenderCell(text, paintIdx, cell.IsHold);
            }

            _grid[r] = dstRow;
        }

        RebuildHeaderLabels();

        // Nuevo grid: cancelar cualquier rebuild en curso del grid anterior
        // (mismo motivo que en desktop: _rebuildNextRow/_rebuildAnchorRow
        // quedarían indexando sobre el _grid viejo y podrían tirar un
        // IndexOutOfRangeException contra el nuevo).
        _rebuildInProgress = false;
        _rebuildCanvas?.Dispose();
        _rebuildCanvas = null;

        // MUY IMPORTANTE: el front cache pertenece al MIDI anterior. Si lo
        // dejamos vivo mientras el nuevo grid espera sus frames diferidos o
        // termina el rebuild incremental, OnPaintSurface continúa bliteando
        // notas viejas en las columnas/filas que ahora deberían estar vacías.
        // Se descarta de inmediato para que el canvas.Clear(BgColor) sea lo
        // único visible hasta que el nuevo cache esté completo.
        ClearRowsCacheForNewGrid();

        // El nuevo MIDI empieza visualmente desde el principio. TrackerPlayer
        // actualizará ContinuousRow enseguida, pero resetearlo aquí evita que
        // el primer rebuild se ancle a una posición heredada de la canción
        // anterior si el tick todavía no llegó.
        _continuousRow = 0;
        _lastContinuousRow = -1;

        _openDropdownCol = -1;
        _dropdownItemRects = Array.Empty<SKRect>();

        _scrollX = 0f;
        RecomputeScrollBounds();
        HorizontalScrollChanged?.Invoke(_scrollX);

        _cacheDirty = true;
        _deferFirstBuildFrames = 2;
        InvalidateSurface();
    }


    /// <summary>
    /// Descarta cualquier bitmap visible construido para el MIDI anterior.
    /// El back cache también se elimina porque puede contener un rebuild
    /// parcial con dimensiones y columnas que ya no corresponden al grid
    /// nuevo. Esta operación solo ocurre al cambiar de canción.
    /// </summary>
    private void ClearRowsCacheForNewGrid()
    {
        _rowsCacheFrontImage?.Dispose();
        _rowsCacheFrontImage = null;

        _rowsCacheFront?.Dispose();
        _rowsCacheFront = null;

        _rowsCacheBack?.Dispose();
        _rowsCacheBack = null;

        _cacheW = 0;
        _cacheH = 0;
        _backCacheW = 0;
        _backCacheH = 0;
        _cacheAnchorRow = -1;
        _cacheWindowRows = 0;
        _cacheAnchorCol = -1;
        _cacheWindowCols = 0;

        _rebuildAnchorRow = 0;
        _rebuildWindowRows = 0;
        _rebuildAnchorCol = 0;
        _rebuildWindowCols = 0;
        _rebuildNextRow = 0;
        _rebuildBmpW = 0;
    }

    public void SetPercussionChannels(IEnumerable<int> channels)
    {
        _percussionChannels = new bool[_columnCount];
        foreach (int ch in channels)
            if ((uint)ch < (uint)_percussionChannels.Length)
                _percussionChannels[ch] = true;
        RebuildHeaderLabels();
        InvalidateSurface();
    }

    public bool IsPercussionHeader(int channel)
        => (uint)channel < (uint)_percussionChannels.Length && _percussionChannels[channel];

    public void UpdateWaveTypes(List<WaveType> waveTypes)
    {
        // Guard: esto se llama desde el tick de render (hasta 30×/seg) como
        // polling defensivo por si el wave type cambió por fuera del dropdown.
        // Sin este chequeo, cada tick alocaría una copia de la lista y
        // dispararía un rebuild completo del header cacheado — 100% inútil
        // en el 99.9% de los frames donde nada cambió.
        if (WaveTypesEqual(waveTypes, _waveTypes)) return;

        _waveTypes = new List<WaveType>(waveTypes);
        RebuildHeaderLabels();
        InvalidateSurface();
    }

    private static bool WaveTypesEqual(List<WaveType> a, List<WaveType> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    public void SetWaveType(int channel, WaveType wave)
    {
        if (channel < 0) return;
        while (_waveTypes.Count <= channel)
            _waveTypes.Add(WaveType.Square);
        _waveTypes[channel] = wave;
        RebuildHeaderLabels();
        InvalidateSurface();
    }

    private void RebuildHeaderLabels()
    {
        _headerChLabels = new string[_columnCount];
        _headerWaveLabels = new string[_columnCount];
        for (int i = 0; i < _columnCount; i++)
        {
            _headerChLabels[i] = $"CH{i:D2}";
            _headerWaveLabels[i] = !WaveTypeEditingEnabled
                ? (IsPercussionHeader(i) ? "DRM" : "PATCH")
                : (i < _waveTypes.Count ? WaveTypeLabel(_waveTypes[i]) : "---");
        }
        _headerDirty = true;
    }

    // ── Render ────────────────────────────────────────────────────────────

    private void OnPaintSurface(object? sender, SKPaintGLSurfaceEventArgs e)
    {
        if (_grid == null || _viewW <= 0 || _viewH <= 0) return;

        var canvas = e.Surface.Canvas;
        canvas.Clear(BgColor);

        int totalRows = _grid.Length;
        double crow = _continuousRow;

        int startRow = Math.Max(0, (int)crow - _halfVisible);
        int endRow = Math.Min(totalRows - 1, startRow + _visibleRows);

        // ── Filas: asegurar cache + blit ────────────────────────────────
        canvas.Save();
        if (_scale > 0f && _scale != 1f) canvas.Scale(_scale);
        canvas.ClipRect(_rowClipRect);

        EnsureRowsCache(startRow, endRow, totalRows);

        if (_rowsCacheFront != null && _rowsCacheFrontImage != null)
        {
            double rowsAboveViewportTop = crow - _cacheAnchorRow - _halfVisible;
            float offsetY = (float)(rowsAboveViewportTop * RowHeight);

            int usedH = _cacheWindowRows * (int)RowHeight;
            float contentH = _viewH - HeaderH;
            offsetY = Math.Clamp(offsetY, 0, Math.Max(0, usedH - contentH));

            float offsetX = _scrollX - _cacheAnchorCol * ColWidth;
            int usedW = _cacheWindowCols * (int)ColWidth;
            offsetX = Math.Clamp(offsetX, 0, Math.Max(0, usedW - _viewW));

            var src = new SKRect(offsetX, offsetY, offsetX + _viewW, offsetY + contentH);
            var dst = new SKRect(0, HeaderH, _viewW, HeaderH + contentH);
            canvas.DrawImage(_rowsCacheFrontImage, src, dst, BlitSampling);
        }

        canvas.Restore();

        // ── Playhead + Header (blit del header cacheado) ────────────────
        canvas.Save();
        if (_scale > 0f && _scale != 1f) canvas.Scale(_scale);

        canvas.DrawRect(0, _playheadY, _viewW, RowHeight, PaintGlow);
        canvas.DrawRect(0, _playheadY, _viewW, 1, PaintPlayhead);
        canvas.DrawRect(0, _playheadY + RowHeight, _viewW, 1, PaintPlayhead);

        EnsureHeaderCache();
        if (_headerCacheImage != null)
        {
            var hSrc = new SKRect(_scrollX, 0, _scrollX + _viewW, HeaderH);
            var hDst = new SKRect(0, 0, _viewW, HeaderH);
            canvas.DrawImage(_headerCacheImage, hSrc, hDst, BlitSampling);
        }

        DrawDropdown(canvas);
        DrawSignalMetrics(canvas);

        canvas.Restore();
    }

    // ── Dropdown de hot-swap de WaveType ─────────────────────────────────

    private void DrawDropdown(SKCanvas canvas)
    {
        if (_openDropdownCol < 0 || _dropdownItemRects.Length == 0) return;

        var first = _dropdownItemRects[0];
        var last = _dropdownItemRects[^1];
        var bgRect = new SKRect(first.Left, first.Top, first.Right, last.Bottom);

        canvas.DrawRect(bgRect, PaintDropdownBg);
        canvas.DrawRect(bgRect.Left, bgRect.Top, bgRect.Width, 1, PaintDropdownBorder);
        canvas.DrawRect(bgRect.Left, bgRect.Bottom - 1, bgRect.Width, 1, PaintDropdownBorder);

        var currentWave = _openDropdownCol < _waveTypes.Count ? _waveTypes[_openDropdownCol] : (WaveType?)null;

        for (int i = 0; i < AllWaveTypes.Length; i++)
        {
            var rect = _dropdownItemRects[i];
            bool isCurrent = AllWaveTypes[i] == currentWave;

            if (i == _dropdownHoverItem)
                canvas.DrawRect(rect, PaintDropdownHover);

            canvas.DrawText(
                WaveTypeLabel(AllWaveTypes[i]),
                rect.Left + 6,
                rect.Top + DropdownItemH - 6f,
                SKTextAlign.Left,
                FontDropdown,
                isCurrent ? PaintDropdownTextActive : PaintDropdownText);
        }
    }

    private void OpenDropdown(int col)
    {
        _openDropdownCol = col;
        _dropdownHoverItem = -1;

        // Posición en PANTALLA (no en contenido) — hay que restar el scroll.
        float x = col * ColWidth - _scrollX;
        if (x + DropdownW > _viewW) x = Math.Max(0, _viewW - DropdownW);
        if (x < 0) x = 0;

        var rects = new SKRect[AllWaveTypes.Length];
        for (int i = 0; i < AllWaveTypes.Length; i++)
        {
            float y = HeaderH + i * DropdownItemH;
            rects[i] = new SKRect(x, y, x + DropdownW, y + DropdownItemH);
        }
        _dropdownItemRects = rects;

        InvalidateSurface();
    }

    private void CloseDropdown()
    {
        if (_openDropdownCol < 0) return;
        _openDropdownCol = -1;
        _dropdownItemRects = Array.Empty<SKRect>();
        InvalidateSurface();
    }

    private void OnTouch(object? sender, SKTouchEventArgs e)
    {
        float s = _scale > 0f ? _scale : 1f;
        float x = e.Location.X / s;
        float y = e.Location.Y / s;

        switch (e.ActionType)
        {
            case SKTouchAction.Pressed:
                _dragCandidate = true;
                _isDragging = false;
                _touchStartX = x;
                _touchStartY = y;
                _dragStartScrollX = _scrollX;
                e.Handled = true;
                return;

            case SKTouchAction.Moved:
                if (_dragCandidate)
                {
                    float dx = x - _touchStartX;
                    float dy = y - _touchStartY;

                    if (!_isDragging && Math.Abs(dx) > DragThresholdPx && Math.Abs(dx) > Math.Abs(dy))
                    {
                        _isDragging = true;
                        // Si había un dropdown abierto y el usuario arranca a
                        // arrastrar, lo cerramos — no tiene sentido dejarlo
                        // "pegado" a una columna que ya no está en esa posición.
                        if (_openDropdownCol >= 0) CloseDropdown();
                    }

                    if (_isDragging)
                    {
                        _scrollX = Math.Clamp(_dragStartScrollX - dx, 0, _maxScrollX);
                        HorizontalScrollChanged?.Invoke(_scrollX);
                        InvalidateSurface();
                    }
                }
                e.Handled = true;
                return;

            case SKTouchAction.Released:
            case SKTouchAction.Cancelled:
                if (_dragCandidate && !_isDragging)
                {
                    VisualTapped?.Invoke();
                    HandleTap(x, y);
                }
                _dragCandidate = false;
                _isDragging = false;
                e.Handled = true;
                return;

            default:
                e.Handled = true;
                return;
        }
    }

    private void HandleTap(float x, float y)
    {
        // 1) Dropdown ya abierto: ¿tocó un ítem, o afuera para cerrarlo?
        if (_openDropdownCol >= 0)
        {
            for (int i = 0; i < _dropdownItemRects.Length; i++)
            {
                if (_dropdownItemRects[i].Contains(x, y))
                {
                    int col = _openDropdownCol;
                    var wave = AllWaveTypes[i];

                    SetWaveType(col, wave);
                    CloseDropdown();
                    OnWaveTypeChanged?.Invoke(col, wave);
                    return;
                }
            }

            CloseDropdown();
            return;
        }

        // 2) ¿Tocó el header de alguna columna? Ahí abrimos el dropdown.
        //    x está en coordenadas de PANTALLA — sumamos el scroll para
        //    saber a qué columna de CONTENIDO corresponde.
        if (y >= 0 && y < HeaderH && _columnCount > 0)
        {
            int col = (int)((x + _scrollX) / ColWidth);
            if (col >= 0 && col < _columnCount)
            {
                if (WaveTypeEditingEnabled)
                    OpenDropdown(col);
                else
                    PatchHeaderTapped?.Invoke(col);
            }
        }
    }

    // ── Header cache ─────────────────────────────────────────────────────

    private void EnsureHeaderCache()
    {
        if (!_headerDirty && _headerCache != null) return;

        // El header cachea TODO el ancho de contenido (no solo el viewport):
        // es una superficie chica (32px de alto), así que no hace falta
        // ventanearlo como a las filas — el costo de memoria es trivial
        // incluso con muchos canales.
        int w = Math.Max(1, (int)Math.Max(_viewW, _contentW));
        int h = (int)HeaderH;

        if (_headerCache == null || _headerCache.Width != w || _headerCache.Height != h)
        {
            _headerCache?.Dispose();
            _headerCache = new SKBitmap(w, h);
        }

        using (var c = new SKCanvas(_headerCache))
        {
            c.Clear(HeaderBg);
            c.DrawRect(0, h - 1, w, 1, PaintHeaderSep);

            int cols = Math.Min(_columnCount, _headerChLabels.Length);
            for (int ch = 0; ch < cols; ch++)
            {
                float x = ch * ColWidth + 4;
                if (x > w) break;
                c.DrawText(_headerChLabels[ch], x, 14f, SKTextAlign.Left, FontGrid, PaintHeaderTxt);
                c.DrawText(_headerWaveLabels[ch], x, 26f, SKTextAlign.Left, FontWaveLabel, PaintWaveLabel);
            }
        }

        _headerCacheImage?.Dispose();
        _headerCacheImage = SKImage.FromBitmap(_headerCache);

        _headerDirty = false;
    }

    // ── Rows cache (incremental, repartido entre frames) ────────────────

    private void EnsureRowsCache(int startRow, int endRow, int totalRows)
    {
        if (_viewW <= 0 || _grid == null || totalRows == 0) return;

        if (_deferFirstBuildFrames > 0)
        {
            _deferFirstBuildFrames--;
            Dispatcher.Dispatch(InvalidateSurface);
            return;
        }

        if (_rebuildInProgress)
        {
            ContinueIncrementalRebuild();
            return;
        }

        // ── Ventana vertical (filas) ─────────────────────────────────────
        int desiredRowBuffer = Math.Max(30, _visibleRows * BufferMultiplier);
        desiredRowBuffer = Math.Min(desiredRowBuffer, MaxBufferRows);

        // ── Ventana horizontal (columnas) ────────────────────────────────
        int visibleCols = (int)Math.Ceiling(_viewW / ColWidth) + 1;
        int colStart = Math.Max(0, (int)(_scrollX / ColWidth));
        int colEnd = Math.Min(_columnCount - 1, (int)((_scrollX + _viewW) / ColWidth));
        int desiredColBuffer = Math.Min(MaxBufferCols, visibleCols * ColBufferMultiplier);

        bool needsRebuild = _cacheDirty || _rowsCacheFront == null;

        if (!needsRebuild)
        {
            int cacheTopRow = _cacheAnchorRow;
            int cacheBottomRow = _cacheAnchorRow + _cacheWindowRows - 1;
            bool nearTopEdge = startRow < cacheTopRow + SafetyMargin && cacheTopRow > 0;
            bool nearBottomEdge = endRow > cacheBottomRow - SafetyMargin && cacheBottomRow < totalRows - 1;

            int cacheLeftCol = _cacheAnchorCol;
            int cacheRightCol = _cacheAnchorCol + _cacheWindowCols - 1;
            bool nearLeftEdge = colStart < cacheLeftCol + ColSafetyMarginCols && cacheLeftCol > 0;
            bool nearRightEdge = colEnd > cacheRightCol - ColSafetyMarginCols && cacheRightCol < _columnCount - 1;

            if (nearTopEdge || nearBottomEdge || nearLeftEdge || nearRightEdge) needsRebuild = true;
        }

        if (!needsRebuild) return;

        // Recalculamos AMBOS ejes de la ventana desde cero cuando hace falta
        // rebuild — es más simple que tratar de preservar el eje que no
        // disparó el trigger, y el bitmap es una sola superficie 2D así que
        // de todas formas hay que regenerar todo el contenido cacheado.
        int anchorRow = startRow - desiredRowBuffer;
        int windowRows = _visibleRows + desiredRowBuffer * 2;
        windowRows = Math.Max(windowRows, 1);
        windowRows = Math.Min(windowRows, _visibleRows + MaxBufferRows * 2);

        int anchorCol = Math.Max(0, colStart - desiredColBuffer);
        int windowCols = (colEnd - anchorCol + 1) + desiredColBuffer;
        windowCols = Math.Max(windowCols, Math.Max(1, visibleCols));
        windowCols = Math.Min(windowCols, Math.Max(1, _columnCount - anchorCol));
        windowCols = Math.Min(windowCols, visibleCols + MaxBufferCols * 2);

        int bmpW = Math.Max(1, windowCols * (int)ColWidth);
        int bmpH = windowRows * (int)RowHeight;
        int maxBmpH = (_visibleRows + MaxBufferRows) * (int)RowHeight;
        if (bmpH > maxBmpH)
        {
            bmpH = maxBmpH;
            windowRows = bmpH / (int)RowHeight;
        }

        if (_rowsCacheBack == null || _backCacheW < bmpW || _backCacheH < bmpH)
        {
            _rowsCacheBack?.Dispose();
            _rowsCacheBack = new SKBitmap(bmpW, bmpH);
            _backCacheW = bmpW;
            _backCacheH = bmpH;
        }

        _rebuildAnchorRow = anchorRow;
        _rebuildWindowRows = windowRows;
        _rebuildAnchorCol = anchorCol;
        _rebuildWindowCols = windowCols;
        _rebuildBmpW = bmpW;
        _rebuildNextRow = anchorRow;
        _rebuildInProgress = true;

        _rebuildCanvas?.Dispose();
        _rebuildCanvas = new SKCanvas(_rowsCacheBack);

        ContinueIncrementalRebuild();
    }

    private void ContinueIncrementalRebuild()
    {
        try
        {
            ContinueIncrementalRebuildCore();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TrackerPanel] REBUILD CRASH: {ex}");
            _rebuildCanvas?.Dispose();
            _rebuildCanvas = null;
            _rebuildInProgress = false;
        }
    }

    private void ContinueIncrementalRebuildCore()
    {
        if (_rowsCacheBack == null || _grid == null || _rebuildCanvas == null)
        {
            _rebuildInProgress = false;
            return;
        }

        var c = _rebuildCanvas;
        int endRowOfWindow = _rebuildAnchorRow + _rebuildWindowRows - 1;
        int colStart = _rebuildAnchorCol;
        int colEnd = Math.Min(_columnCount - 1, _rebuildAnchorCol + _rebuildWindowCols - 1);

        // Presupuesto adaptado a la cantidad de columnas CACHEADAS (no al
        // total de canales de la canción): más columnas en la ventana = más
        // DrawText por fila = hay que dibujar menos filas por chunk.
        int colsInWindow = Math.Max(1, colEnd - colStart + 1);
        int perFrame = Math.Max(5, MaxRowsPerFrame / Math.Max(1, colsInWindow / 6));
        int chunkEnd = Math.Min(endRowOfWindow, _rebuildNextRow + perFrame - 1);

        if (_rebuildNextRow == _rebuildAnchorRow)
            c.DrawRect(0, 0, _rebuildBmpW, _rowsCacheBack.Height, PaintBg);

        for (int r = _rebuildNextRow; r <= chunkEnd; r++)
        {
            if (r < 0 || r >= _grid.Length) continue; // fila fantasma de margen

            float rowY = (r - _rebuildAnchorRow) * RowHeight;

            c.DrawRect(0, rowY, _rebuildBmpW, RowHeight, r % 2 == 0 ? PaintBg : PaintAlt);

            var row = _grid[r];

            for (int ch = colStart; ch <= colEnd; ch++)
            {
                if (ch < 0 || ch >= row.Length) continue;

                ref readonly var cell = ref row[ch];
                float x = (ch - _rebuildAnchorCol) * ColWidth;
                if (x > _rebuildBmpW) break;

                if (cell.IsHold)
                    c.DrawRect(x + 1, rowY + 1, ColWidth - 2, RowHeight - 2, PaintHoldBg);

                c.DrawText(cell.Text, x + 4, rowY + RowHeight - 6f, SKTextAlign.Left, FontGrid, cell.GetPaint());
            }
        }

        _rebuildNextRow = chunkEnd + 1;

        if (_rebuildNextRow > endRowOfWindow)
        {
            _rebuildCanvas.Dispose();
            _rebuildCanvas = null;

            (_rowsCacheFront, _rowsCacheBack) = (_rowsCacheBack, _rowsCacheFront);
            (_cacheW, _backCacheW) = (_backCacheW, _cacheW);
            (_cacheH, _backCacheH) = (_backCacheH, _cacheH);
            _cacheAnchorRow = _rebuildAnchorRow;
            _cacheWindowRows = _rebuildWindowRows;
            _cacheAnchorCol = _rebuildAnchorCol;
            _cacheWindowCols = _rebuildWindowCols;
            _cacheDirty = false;
            _rebuildInProgress = false;

            // Snapshot inmutable del nuevo front — se crea UNA vez por
            // rebuild completo, no por frame (ver nota en el campo).
            _rowsCacheFrontImage?.Dispose();
            _rowsCacheFrontImage = _rowsCacheFront != null ? SKImage.FromBitmap(_rowsCacheFront) : null;

            var gridLen = _grid?.Length ?? 0;
            _rebuildNextRow = _rebuildAnchorRow + _rebuildWindowRows;
            if (_rebuildNextRow > gridLen) _rebuildNextRow = gridLen;
        }
        else
        {
            Dispatcher.Dispatch(InvalidateSurface);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string WaveTypeLabel(WaveType wt) => wt switch
    {
        WaveType.Square => "SQ",
        WaveType.Triangle => "TR",
        WaveType.Saw => "SAW",
        WaveType.Sine => "SIN",
        WaveType.Pulse25 => "P25",
        WaveType.Pulse12 => "P12",
        WaveType.Noise => "NS",
        WaveType.WhiteNoise => "WN",
        WaveType.ChipDrums => "DRM",
        _ => "???"
    };
}

// ── RenderCell — celda pre-renderizada del grid visual ────────────────────

public readonly struct RenderCell
{
    public const byte PaintNormal = 0;
    public const byte PaintHold = 1;
    public const byte PaintEmpty = 2;

    public readonly string Text;
    public readonly byte PaintIdx;
    public readonly bool IsHold;

    public RenderCell(string text, byte paintIdx, bool isHold)
    {
        Text = text;
        PaintIdx = paintIdx;
        IsHold = isHold;
    }

    public SKPaint GetPaint() => PaintIdx switch
    {
        PaintHold => TrackerPanel.PaintHoldText,
        PaintEmpty => TrackerPanel.PaintEmptyText,
        _ => TrackerPanel.PaintText,
    };
}