using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace MIDIRift;

public class TrackerPlayer
{
    private readonly TrackerModel _model;
    private readonly IChiptunePlayer _engine;

    // ── Grid visual ───────────────────────────────────────────────────────
    // Cambiado de List<List<...>> a array jagged para evitar resizing y boxing.
    // GridRow es struct: cero heap allocation por celda.
    public GridRow[][] Grid { get; private set; } = Array.Empty<GridRow[]>();

    public int CurrentRow { get; private set; } = 0;
    public bool IsFinished => _engine.IsFinished;

    private readonly Stopwatch _clock = new();
    public TimeSpan Elapsed => _clock.Elapsed;

    private long[] _rowStartSamplesArr = Array.Empty<long>();

    // ── Eventos ───────────────────────────────────────────────────────────
    public event Action<float>? OnFrameTick;
    public event Action? OnFinished;

    // ── FPS ───────────────────────────────────────────────────────────────
    public static int TargetFps { get; set; } = 30;
    private int _renderIntervalMs;

    // ── Estado atómico inter-hilo ─────────────────────────────────────────
    private volatile float _latestContinuousRow = 0f;
    private volatile bool _finished = false;
    private int _pendingRenderTick = 0;
    private int _stepDirty = 0;
    // EOF must not depend on the visual render loop staying alive.
    // 0 = pending, 1 = OnFinished already queued/dispatched.
    private int _finishNotificationSent = 0;

    // Posición visual suavizada. VirtualSample avanza por bloques de audio
    // (2048 frames), así que usarlo directamente produce movimiento a saltos.
    private double _displaySample = 0;
    private long _lastDisplayTimestamp = 0;

    private CancellationTokenSource? _cts;

    // ── Constructor ───────────────────────────────────────────────────────

    public TrackerPlayer(TrackerModel model, IChiptunePlayer engine)
    {
        _model = model;
        _engine = engine;
        BuildGrid();
    }

    // ── Grid ──────────────────────────────────────────────────────────────

    private void BuildGrid()
    {
        int chanCount = _model.ChannelCount;
        if (chanCount == 0) return;

        // ── 1. Pre-calcular startMs[] por canal con TrackerModel.BuildStartTimesMs
        //      Evita recalcular tiempos dentro del loop de filas.
        var startTimes = new int[chanCount][];
        var endTimes = new int[chanCount][];

        for (int ci = 0; ci < chanCount; ci++)
        {
            var starts = _model.BuildStartTimesMs(ci);
            startTimes[ci] = starts;

            int stepCount = _model.StepCount(ci);
            var ends = new int[stepCount];
            for (int si = 0; si < stepCount; si++)
                ends[si] = starts[si] + (int)_model.GetStep(ci, si).DurationMs;
            endTimes[ci] = ends;
        }

        // ── 2. Recolectar time-points únicos con array+sort (sin SortedSet) ─
        // SortedSet alloca un árbol rojo-negro; array+sort es O(n log n) pero
        // con constante mucho menor y sin GC pressure.
        int totalPoints = 0;
        for (int ci = 0; ci < chanCount; ci++)
            totalPoints += startTimes[ci].Length + endTimes[ci].Length;

        var allTimes = new int[totalPoints];
        int writeIdx = 0;
        for (int ci = 0; ci < chanCount; ci++)
        {
            Array.Copy(startTimes[ci], 0, allTimes, writeIdx, startTimes[ci].Length);
            writeIdx += startTimes[ci].Length;
            Array.Copy(endTimes[ci], 0, allTimes, writeIdx, endTimes[ci].Length);
            writeIdx += endTimes[ci].Length;
        }

        Array.Sort(allTimes);

        // Deduplicate in-place — sin allocations adicionales
        int uniqueCount = 0;
        for (int i = 0; i < allTimes.Length; i++)
            if (i == 0 || allTimes[i] != allTimes[i - 1])
                allTimes[uniqueCount++] = allTimes[i];

        int rowCount = uniqueCount - 1;
        if (rowCount <= 0) return;

        // ── 3. Build sweep-line state ──────────────────────────────────────
        // activeStepIdx[ci] = índice del step activo en el canal ci.
        // -1 = ninguno activo.
        var activeStepIdx = new int[chanCount];
        for (int ci = 0; ci < chanCount; ci++) activeStepIdx[ci] = -1;

        // Punteros de búsqueda por canal — avanza monotónicamente (sweep)
        var searchPtr = new int[chanCount];

        // ── 4. Construir el grid como array jagged ─────────────────────────
        var grid = new GridRow[rowCount][];
        var samplesList = new long[rowCount];

        for (int t = 0; t < rowCount; t++)
        {
            int tStart = allTimes[t];
            int tEnd = allTimes[t + 1];
            int dur = tEnd - tStart;

            samplesList[t] = (long)(tStart / 1000.0 * 44100.0);

            var row = new GridRow[chanCount];

            for (int ci = 0; ci < chanCount; ci++)
            {
                int stepCount = _model.StepCount(ci);
                var starts = startTimes[ci];
                var ends = endTimes[ci];

                // Avanzar el puntero de búsqueda hasta el step que cubre tStart
                // (sweep monotónico — O(total steps) en total, no O(n) por fila)
                int ptr = searchPtr[ci];
                while (ptr < stepCount - 1 && starts[ptr + 1] <= tStart)
                    ptr++;
                searchPtr[ci] = ptr;

                int stepIdx = ptr;
                bool hasActive = stepIdx >= 0
                               && stepIdx < stepCount
                               && starts[stepIdx] <= tStart
                               && ends[stepIdx] > tStart;

                if (!hasActive)
                {
                    row[ci] = GridRow.Empty(dur);
                }
                else
                {
                    ref readonly var step = ref _model.GetStep(ci, stepIdx);
                    bool isHold = starts[stepIdx] < tStart;
                    row[ci] = GridRow.FromStep(in step, dur, isHold);
                }
            }

            grid[t] = row;
        }

        Grid = grid;
        _rowStartSamplesArr = samplesList;
    }

    // ── Playback control ──────────────────────────────────────────────────

    public void Start()
    {
        _clock.Restart();
        _engine.Play();
        StartLoops();
    }

    public void Stop()
    {
        StopLoops();
        _engine.Stop();
        _clock.Stop();
    }

    public void Reset()
    {
        StopLoops();
        _engine.Reset();
        _engine.Stop();
        CurrentRow = 0;
        _latestContinuousRow = 0f;
        _finished = false;
        _pendingRenderTick = 0;
        _stepDirty = 0;
        Interlocked.Exchange(ref _finishNotificationSent, 0);
        _displaySample = 0;
        _lastDisplayTimestamp = 0;
        _clock.Reset();
    }

    // ── Loops ─────────────────────────────────────────────────────────────

    private void StartLoops()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _finished = false;
        _pendingRenderTick = 0;
        _stepDirty = 0;
        Interlocked.Exchange(ref _finishNotificationSent, 0);
        _displaySample = _engine.VirtualSample;
        _lastDisplayTimestamp = Stopwatch.GetTimestamp();

        int fps = Math.Clamp(TargetFps, 15, 120);
        _renderIntervalMs = 1000 / fps;

        _engine.OnStepAdvanced += OnEngineStep;

        var token = _cts.Token;

        Task.Run(async () =>
        {
            var sw = Stopwatch.StartNew();

            while (!token.IsCancellationRequested)
            {
                long elapsed = sw.ElapsedMilliseconds;
                int wait = _renderIntervalMs - (int)(elapsed % _renderIntervalMs);
                if (wait > 0 && wait < _renderIntervalMs)
                    await Task.Delay(wait, token).ContinueWith(_ => { });

                if (token.IsCancellationRequested) break;

                if (!_finished && _engine.IsFinished)
                    _finished = true;

                if (_finished)
                {
                    NotifyFinishedOnce();
                    break;
                }

                // La posición continua debe recalcularse en CADA tick visual.
                // VirtualSample avanza por bloques completos; SmoothDisplaySample
                // interpola a velocidad real para que el tracker no se congele
                // entre escrituras al AudioTrack.
                Interlocked.Exchange(ref _stepDirty, 0);
                long displaySample = SmoothDisplaySample();
                _latestContinuousRow = GetContinuousRow(displaySample);

                if (Interlocked.CompareExchange(ref _pendingRenderTick, 1, 0) == 0)
                {
                    float row = _latestContinuousRow;
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        OnFrameTick?.Invoke(row);
                        Interlocked.Exchange(ref _pendingRenderTick, 0);
                    });
                }
            }
        }, token);
    }

    private void StopLoops()
    {
        _engine.OnStepAdvanced -= OnEngineStep;
        _cts?.Cancel();
        _cts = null;
    }

    private void OnEngineStep()
    {
        if (_engine.IsFinished)
        {
            _finished = true;

            // The audio engine is the authority for EOF. Previously we only
            // flipped _finished here and waited for the visual polling loop to
            // notice it on its next tick. If that Task had faulted/stopped, the
            // MIDI ended audibly but OnFinished never reached MainPage, so
            // NextAuto() was never called and the queue appeared to pause.
            NotifyFinishedOnce();
        }
        else
        {
            Interlocked.Exchange(ref _stepDirty, 1);
        }
    }

    private void NotifyFinishedOnce()
    {
        if (Interlocked.Exchange(ref _finishNotificationSent, 1) != 0)
            return;

        MainThread.BeginInvokeOnMainThread(() => OnFinished?.Invoke());
    }

    // ── Row mapping (búsqueda binaria O(log n)) ───────────────────────────

    public int GetRowFromSample() => GetRowFromSample(_engine.VirtualSample);

    private int GetRowFromSample(long sample)
    {
        if (_rowStartSamplesArr.Length == 0) return 0;

        int idx = Array.BinarySearch(_rowStartSamplesArr, sample);
        if (idx >= 0) return idx;

        int ip = ~idx;
        return Math.Clamp(ip - 1, 0, _rowStartSamplesArr.Length - 1);
    }

    public float GetContinuousRow() => GetContinuousRow(_engine.VirtualSample);

    private float GetContinuousRow(long sample)
    {
        int len = _rowStartSamplesArr.Length;
        if (len == 0) return 0f;

        int row = GetRowFromSample(sample);

        if (row >= len - 1) return row;

        long rowStart = _rowStartSamplesArr[row];
        long rowEnd = _rowStartSamplesArr[row + 1];
        long rowDuration = rowEnd - rowStart;

        if (rowDuration <= 0) return row;
        return row + (float)(sample - rowStart) / rowDuration;
    }

    private long SmoothDisplaySample()
    {
        long now = Stopwatch.GetTimestamp();
        long raw = _engine.VirtualSample;

        if (_lastDisplayTimestamp == 0)
        {
            _lastDisplayTimestamp = now;
            _displaySample = raw;
            return raw;
        }

        double dt = (now - _lastDisplayTimestamp) / (double)Stopwatch.Frequency;
        _lastDisplayTimestamp = now;

        // Seek/reset: si la fuente queda detrás o salta más de dos segundos,
        // sincronizar inmediatamente en lugar de intentar recorrer la distancia.
        double delta = raw - _displaySample;
        if (delta < 0 || delta > 44100.0 * 2.0)
        {
            _displaySample = raw;
        }
        else
        {
            _displaySample += dt * 44100.0 * Math.Max(0.01f, _engine.Speed);
            if (_displaySample > raw) _displaySample = raw;
        }

        return (long)_displaySample;
    }

    public float GetSubRowOffset()
    {
        int len = _rowStartSamplesArr.Length;
        if (len == 0) return 0f;

        long sample = _engine.VirtualSample;
        int row = GetRowFromSample();

        if (row >= len - 1) return 0f;

        long rowStart = _rowStartSamplesArr[row];
        long rowEnd = _rowStartSamplesArr[row + 1];
        long rowDuration = rowEnd - rowStart;

        if (rowDuration <= 0) return 0f;
        return Math.Clamp((float)(sample - rowStart) / rowDuration, 0f, 1f);
    }

    public bool AdvanceStep()
    {
        if (CurrentRow >= Grid.Length - 1) return false;
        CurrentRow++;
        return true;
    }
}

// ── GridRow — struct para el grid visual ──────────────────────────────────
// Reemplaza (List<float> freqs, int dur, bool isHold).
// Sin heap allocation, sin GC pressure.
// Las frecuencias se guardan inline igual que MidiStep.

public struct GridRow
{
    public const int MaxFreqs = MidiStep.MaxNotes;

    public int FreqCount;
    public float F0, F1, F2, F3, F4, F5;
    public int DurMs;
    public bool IsHold;

    public static GridRow Empty(int dur) => new() { FreqCount = 0, DurMs = dur, IsHold = false };

    public static GridRow FromStep(in MidiStep step, int dur, bool isHold)
    {
        var r = new GridRow
        {
            FreqCount = step.NoteCount,
            DurMs = dur,
            IsHold = isHold,
        };
        // Copiar frecuencias inline — sin loop, sin array
        if (step.NoteCount > 0) r.F0 = step.Freq0;
        if (step.NoteCount > 1) r.F1 = step.Freq1;
        if (step.NoteCount > 2) r.F2 = step.Freq2;
        if (step.NoteCount > 3) r.F3 = step.Freq3;
        if (step.NoteCount > 4) r.F4 = step.Freq4;
        if (step.NoteCount > 5) r.F5 = step.Freq5;
        return r;
    }

    public readonly float GetFreq(int i) => i switch
    {
        0 => F0,
        1 => F1,
        2 => F2,
        3 => F3,
        4 => F4,
        _ => F5,
    };
}