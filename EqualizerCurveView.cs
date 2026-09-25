using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using System;
#if ANDROID
using Android.Views;
#endif

namespace MIDIRift;

/// <summary>
/// Port a MAUI/GraphicsView del EqualizerPanel.cs de Desktop (Avalonia):
/// mismos 10 sliders verticales + curva de respuesta interpolada por
/// spline (Catmull-Rom → Bézier cúbica) y el mismo "movimiento de bandas
/// vecinas" al arrastrar. Usa GraphicsView en vez de SKGLView (que ya usan
/// SpectrumPanel/OscilloscopePanel) porque GraphicsView expone
/// Start/Drag/EndInteraction con coordenadas de touch listas para usar —
/// exactamente lo que hace falta acá (arrastrar sliders) — sin tener que
/// resolver touch a mano sobre una superficie Skia.
/// </summary>
public class EqualizerCurveView : GraphicsView, IDrawable
{
    private const int BandCount = EqualizerBands.Count;
    private const float MinDb = EqualizerBands.MinDb;
    private const float MaxDb = EqualizerBands.MaxDb;

    private const float TopPad = 22f;
    private const float BottomLabelH = 20f;
    private const float ThumbW = 22f;
    private const float ThumbH = 10f;

    // Paleta propia porque GraphicsView dibuja fuera del sistema de estilos XAML.
    private Color BgColor = Colors.Transparent;
    private Color TrackColor = Colors.Transparent;
    private Color FillBelowColor = Colors.Transparent;
    private Color ThumbColor = Colors.Transparent;
    private Color ThumbActiveColor = Colors.Transparent;
    private Color ZeroLineColor = Colors.Transparent;
    private Color GridLineColor = Colors.Transparent;
    private Color LabelColor = Colors.Transparent;
    private Color LabelColorActive = Colors.Transparent;
    private Color CurveColor = Colors.Transparent;
    private Color FillTop = Colors.Transparent;
    private Color FillBot = Colors.Transparent;
    private Color PointColor = Colors.Transparent;

    private readonly float[] _gainsDb = new float[BandCount];
    private int _dragBand = -1;

    /// <summary>Se dispara cada vez que una banda cambia (índice, dB nuevo).</summary>
    public Action<int, float>? OnBandGainChanged;
    /// <summary>Se dispara además de OnBandGainChanged, sin detalle de qué banda.</summary>
    public Action? OnAnyGainChanged;
    /// <summary>Se dispara una sola vez al soltar la banda; útil para persistencia.</summary>
    public Action? OnBandDragCompleted;

    /// <summary>Mismo comportamiento que Desktop: arrastrar una banda desplaza parcialmente a sus vecinas.</summary>
    public bool NeighborSpreadEnabled { get; set; } = true;
    public int NeighborSpreadRadius { get; set; } = 2;
    public float NeighborSpreadStrength { get; set; } = 0.5f;

    public EqualizerCurveView()
    {
        Drawable = this;
        ApplyTheme(ThemePalette.IsLight);
        ThemePalette.Changed += OnThemeChanged;
        Unloaded += (_, _) => ThemePalette.Changed -= OnThemeChanged;
        HeightRequest = 180;

        StartInteraction += (_, e) =>
        {
            SetParentScrollInterceptionBlocked(true);
            HandleTouch(e.Touches, isPress: true);
        };
        DragInteraction += (_, e) => HandleTouch(e.Touches, isPress: false);
        EndInteraction += (_, __) => EndBandDrag();

#if ANDROID
        HandlerChanged += OnHandlerChanged;
        HandlerChanging += OnHandlerChanging;
#endif
    }

    private void OnThemeChanged(AppTheme theme) => ApplyTheme(theme == AppTheme.Light);

    public void ApplyTheme(bool light)
    {
        if (light)
        {
            BgColor = Color.FromArgb("#F0EDF4");
            TrackColor = Color.FromArgb("#C8C0D2");
            FillBelowColor = Color.FromArgb("#8C72C8");
            ThumbColor = Color.FromArgb("#8068D8");
            ThumbActiveColor = Color.FromArgb("#654BBE");
            ZeroLineColor = Color.FromRgba(110, 85, 197, 120);
            GridLineColor = Color.FromRgba(110, 85, 197, 45);
            LabelColor = Color.FromArgb("#675A76");
            LabelColorActive = Color.FromArgb("#453756");
            CurveColor = Color.FromArgb("#6E55C5");
            FillTop = Color.FromRgba(128, 104, 216, 80);
            FillBot = Color.FromRgba(128, 104, 216, 0);
            PointColor = Color.FromArgb("#4F389F");
        }
        else
        {
            BgColor = Color.FromRgb(14, 14, 14); TrackColor = Color.FromRgb(50, 50, 50);
            FillBelowColor = Color.FromRgb(90, 60, 115); ThumbColor = Color.FromRgb(199, 155, 230);
            ThumbActiveColor = Color.FromRgb(215, 180, 240); ZeroLineColor = Color.FromRgba(199, 155, 230, 90);
            GridLineColor = Color.FromRgba(199, 155, 230, 35); LabelColor = Color.FromRgb(150, 130, 170);
            LabelColorActive = Color.FromRgb(210, 180, 235); CurveColor = Color.FromRgb(199, 155, 230);
            FillTop = Color.FromRgba(142, 90, 180, 90); FillBot = Color.FromRgba(142, 90, 180, 0);
            PointColor = Color.FromRgb(232, 216, 245);
        }
        BackgroundColor = BgColor;
        Invalidate();
    }

    private void EndBandDrag()
    {
        bool hadDrag = _dragBand >= 0;
        _dragBand = -1;
        SetParentScrollInterceptionBlocked(false);
        Invalidate();
        if (hadDrag) OnBandDragCompleted?.Invoke();
    }

    private void SetParentScrollInterceptionBlocked(bool blocked)
    {
#if ANDROID
        if (Handler?.PlatformView is Android.Views.View nativeView)
            nativeView.Parent?.RequestDisallowInterceptTouchEvent(blocked);
#endif
    }

#if ANDROID
    private Android.Views.View? _nativeView;

    private void OnHandlerChanged(object? sender, EventArgs e)
    {
        DetachNativeTouchHandler();
        _nativeView = Handler?.PlatformView as Android.Views.View;
        if (_nativeView != null)
            _nativeView.Touch += OnNativeTouch;
    }

    private void OnHandlerChanging(object? sender, HandlerChangingEventArgs e)
    {
        DetachNativeTouchHandler();
        SetParentScrollInterceptionBlocked(false);
    }

    private void DetachNativeTouchHandler()
    {
        if (_nativeView == null) return;
        _nativeView.Touch -= OnNativeTouch;
        _nativeView = null;
    }

    private void OnNativeTouch(object? sender, Android.Views.View.TouchEventArgs e)
    {
        switch (e.Event?.ActionMasked)
        {
            case MotionEventActions.Down:
            case MotionEventActions.Move:
                SetParentScrollInterceptionBlocked(true);
                break;

            case MotionEventActions.Up:
            case MotionEventActions.Cancel:
                EndBandDrag();
                break;
        }

        // GraphicsView debe seguir recibiendo el gesto para actualizar la banda.
        e.Handled = false;
    }
#endif

    // ── API pública (mismos nombres/firmas que EqualizerPanel de Desktop) ──

    public float[] GetGains() => (float[])_gainsDb.Clone();

    public void SetGains(ReadOnlySpan<float> gainsDb)
    {
        int n = Math.Min(BandCount, gainsDb.Length);
        for (int i = 0; i < n; i++)
            _gainsDb[i] = Math.Clamp(gainsDb[i], MinDb, MaxDb);
        Invalidate();
    }

    public void ResetAll()
    {
        Array.Clear(_gainsDb, 0, BandCount);
        Invalidate();
        OnAnyGainChanged?.Invoke();
    }

    private void SetGain(int band, float db, bool raiseEvent = true)
    {
        if ((uint)band >= BandCount) return;
        db = Math.Clamp(db, MinDb, MaxDb);
        if (MathF.Abs(_gainsDb[band] - db) < 0.001f) return;
        _gainsDb[band] = db;
        if (raiseEvent)
        {
            OnBandGainChanged?.Invoke(band, db);
            OnAnyGainChanged?.Invoke();
        }
    }

    // ── Movimiento de bandas vecinas — idéntico a EqualizerPanel (Desktop) ──
    private void SetGainInteractive(int band, float db)
    {
        if ((uint)band >= BandCount) return;
        float old = _gainsDb[band];
        float clamped = Math.Clamp(db, MinDb, MaxDb);
        float delta = clamped - old;

        SetGain(band, clamped);

        if (!NeighborSpreadEnabled || MathF.Abs(delta) < 0.001f) { Invalidate(); return; }

        int radius = Math.Max(0, NeighborSpreadRadius);
        for (int offset = 1; offset <= radius; offset++)
        {
            float falloff = 0.5f * (1f + MathF.Cos(MathF.PI * offset / (radius + 1)));
            float neighborDelta = delta * NeighborSpreadStrength * falloff;

            int left = band - offset;
            if (left >= 0) SetGain(left, _gainsDb[left] + neighborDelta);

            int right = band + offset;
            if (right < BandCount) SetGain(right, _gainsDb[right] + neighborDelta);
        }
        Invalidate();
    }

    // ── Layout compartido curva ↔ sliders ───────────────────────────────
    private double BandCenterX(int i, double w) => (i + 0.5) * (w / BandCount);
    private float GainToY(float db, float y0, float y1) => y1 - (db - MinDb) / (MaxDb - MinDb) * (y1 - y0);
    private float YToGain(float y, float y0, float y1)
    {
        float t = Math.Clamp((y1 - y) / (y1 - y0), 0f, 1f);
        return MinDb + t * (MaxDb - MinDb);
    }

    private int HitTestBand(double x, double w)
    {
        if (w <= 0) return -1;
        int band = (int)(x / (w / BandCount));
        return Math.Clamp(band, 0, BandCount - 1);
    }

    private void HandleTouch(PointF[] touches, bool isPress)
    {
        if (touches.Length == 0) return;
        var p = touches[0];
        double w = Width;
        if (w <= 0) return;

        if (isPress)
        {
            int band = HitTestBand(p.X, w);
            if (band < 0) return;
            _dragBand = band;
        }

        if (_dragBand < 0) return;
        float y0 = TopPad, y1 = (float)Height - BottomLabelH;
        SetGainInteractive(_dragBand, YToGain(p.Y, y0, y1));
    }

    // ── Dibujo ───────────────────────────────────────────────────────────

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        float w = dirtyRect.Width, h = dirtyRect.Height;
        if (w <= 0 || h <= 0) return;

        canvas.FillColor = BgColor;
        canvas.FillRectangle(0, 0, w, h);

        float y0 = TopPad, y1 = h - BottomLabelH;

        DrawCurve(canvas, w, y0, y1);

        // Grid de referencia (0dB + líneas cada 6dB)
        for (float db = MinDb; db <= MaxDb; db += 6f)
        {
            canvas.StrokeColor = db == 0f ? ZeroLineColor : GridLineColor;
            canvas.StrokeSize = 1;
            float gy = GainToY(db, y0, y1);
            canvas.DrawLine(0, gy, w, gy);
        }

        canvas.FontSize = 10;
        for (int i = 0; i < BandCount; i++)
        {
            float cx = (float)BandCenterX(i, w);
            float thumbY = GainToY(_gainsDb[i], y0, y1);

            canvas.StrokeColor = TrackColor;
            canvas.StrokeSize = 4;
            canvas.DrawLine(cx, y0, cx, y1);

            float zeroY = GainToY(0f, y0, y1);
            float fillTop = Math.Min(zeroY, thumbY);
            float fillH = Math.Abs(zeroY - thumbY);
            if (fillH > 0.5f)
            {
                canvas.FillColor = FillBelowColor;
                canvas.FillRectangle(cx - 2, fillTop, 4, fillH);
            }

            bool active = i == _dragBand;
            canvas.FillColor = active ? ThumbActiveColor : ThumbColor;
            canvas.FillRoundedRectangle(cx - ThumbW / 2, thumbY - ThumbH / 2, ThumbW, ThumbH, 3);

            canvas.FontColor = LabelColor;
            canvas.Font = Microsoft.Maui.Graphics.Font.Default;
            canvas.DrawString(EqualizerBands.All[i].Label, cx - 14, y1 + 2, 28, BottomLabelH - 2,
                HorizontalAlignment.Center, VerticalAlignment.Top);

            if (active)
            {
                string txt = $"{(_gainsDb[i] >= 0 ? "+" : "")}{_gainsDb[i]:0.0} dB";
                canvas.FontColor = LabelColorActive;
                canvas.FontSize = 11;
                canvas.DrawString(txt, cx - 30, 2, 60, 16, HorizontalAlignment.Center, VerticalAlignment.Top);
                canvas.FontSize = 10;
            }
        }
    }

    private void DrawCurve(ICanvas canvas, float w, float y0, float y1)
    {
        var pts = new PointF[BandCount];
        for (int i = 0; i < BandCount; i++)
            pts[i] = new PointF((float)BandCenterX(i, w), GainToY(_gainsDb[i], y0, y1));

        // Puntos fantasma en los extremos (extrapolación lineal), igual que Desktop.
        var ext = new PointF[BandCount + 2];
        ext[0] = new PointF(pts[0].X - (pts[1].X - pts[0].X), pts[0].Y - (pts[1].Y - pts[0].Y));
        for (int i = 0; i < BandCount; i++) ext[i + 1] = pts[i];
        ext[BandCount + 1] = new PointF(
            pts[BandCount - 1].X - (pts[BandCount - 2].X - pts[BandCount - 1].X),
            pts[BandCount - 1].Y - (pts[BandCount - 2].Y - pts[BandCount - 1].Y));

        var path = new PathF();
        path.MoveTo(ext[1]);
        var fillPath = new PathF();
        fillPath.MoveTo(ext[1]);
        for (int i = 1; i < BandCount; i++)
        {
            PointF p0 = ext[i - 1], p1 = ext[i], p2 = ext[i + 1], p3 = ext[i + 2];
            var c1 = new PointF(p1.X + (p2.X - p0.X) / 6f, p1.Y + (p2.Y - p0.Y) / 6f);
            var c2 = new PointF(p2.X - (p3.X - p1.X) / 6f, p2.Y - (p3.Y - p1.Y) / 6f);
            path.CurveTo(c1, c2, p2);
            fillPath.CurveTo(c1, c2, p2);
        }

        // Relleno bajo la curva, estilo "skyline" (mismo criterio que Desktop).
        fillPath.LineTo(ext[BandCount].X, y1);
        fillPath.LineTo(ext[1].X, y1);
        fillPath.Close();

        var gradient = new LinearGradientPaint
        {
            GradientStops = new[]
            {
                new PaintGradientStop(0f, FillTop),
                new PaintGradientStop(1f, FillBot),
            },
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
        };
        canvas.SetFillPaint(gradient, fillPath.Bounds);
        canvas.FillPath(fillPath);

        canvas.StrokeColor = CurveColor;
        canvas.StrokeSize = 2;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeLineJoin = LineJoin.Round;
        canvas.SetShadow(new SizeF(0, 0), 6, CurveColor.WithAlpha(0.5f));
        canvas.DrawPath(path);
        canvas.SetShadow(new SizeF(0, 0), 0, Colors.Transparent);

        canvas.FillColor = PointColor;
        foreach (var p in pts)
            canvas.FillCircle(p.X, p.Y, 2.6f);
    }
}
