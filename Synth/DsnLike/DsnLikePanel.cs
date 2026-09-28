using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace MIDIRift.Synth.DsnLike;

/// <summary>
/// Development-facing DSN-like laboratory. It benchmarks the isolated DSP core;
/// it does not replace or interrupt the current Lyra/Legacy playback engine.
/// </summary>
public sealed class DsnLikePanel : ContentView
{
    private readonly Picker _preset = new() { Title = "Carga DSP" };
    private readonly Picker _voices = new() { Title = "Voces" };
    private readonly Picker _block = new() { Title = "Bloque" };
    private readonly Button _run = new() { Text = "RUN BENCHMARK", FontAttributes = FontAttributes.Bold };
    private readonly Button _profile = new() { Text = "PROFILE DSP", FontAttributes = FontAttributes.Bold };
    private readonly Label _status = new() { Text = "Listo para torturar silicio.", FontSize = 12 };
    private readonly Label _result = new() { FontFamily = "monospace", FontSize = 12 };
    private readonly ProgressBar _budget = new() { Progress = 0 };
    private readonly Label _macro = new() { FontFamily = "monospace", FontSize = 11 };
    private readonly Label _liveStatus = new() { Text = "Sin DSN activo", FontSize = 12, FontAttributes = FontAttributes.Bold };
    private readonly Label _live = new() { FontFamily = "monospace", FontSize = 12 };
    private readonly ProgressBar _liveBudget = new() { Progress = 0 };
    private readonly GraphicsView _history = new()
    {
        HeightRequest = 118,
        HorizontalOptions = LayoutOptions.Fill
    };
    private readonly DsnRenderBudgetDrawable _historyDrawable = new();
    private Func<DsnRealtimeTelemetrySnapshot?>? _liveSource;
    private readonly Queue<double> _renderHistory = new();
    private long _lastAllocated;
    private int _lastG0, _lastG1, _lastG2;
    private readonly Grid _breakdown = new()
    {
        ColumnDefinitions =
        {
            new ColumnDefinition(new GridLength(1.65, GridUnitType.Star)),
            new ColumnDefinition(GridLength.Star),
            new ColumnDefinition(GridLength.Star),
            new ColumnDefinition(GridLength.Star)
        },
        RowSpacing = 4,
        ColumnSpacing = 8
    };

    public DsnLikePanel()
    {
        _history.Drawable = _historyDrawable;
        _preset.ItemsSource = new[] { "Default", "Dual VCO + Filter", "FM", "Hard Sync", "Resonant + LFO", "Drive", "EVERYTHING ☠" };
        _preset.SelectedIndex = 0;
        _voices.ItemsSource = new[] { "16", "32", "64", "96", "128" };
        _voices.SelectedIndex = 2;
        _block.ItemsSource = new[] { "256", "512", "1024" };
        _block.SelectedIndex = 1;
        _run.Clicked += RunBenchmark;
        _profile.Clicked += RunProfiler;
        BuildBreakdownHeader();

        var title = new Label
        {
            Text = "DSN PERFORMANCE MONITOR · LAB",
            FontSize = 20,
            FontAttributes = FontAttributes.Bold
        };
        var subtitle = new Label
        {
            Text = "Dual VCO · FM · Sync · SVF · ADSR · LFO · Drive",
            FontSize = 11
        };

        var selectors = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 8
        };
        selectors.Add(_preset, 0);
        selectors.Add(_voices, 1);
        selectors.Add(_block, 2);

        var card = new Border
        {
            Padding = 14,
            StrokeThickness = 1,
            Content = new VerticalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    new Label { Text = "LIVE PLAYBACK", FontAttributes = FontAttributes.Bold, FontSize = 13 },
                    _liveStatus,
                    _liveBudget,
                    _live,
                    _history,
                    new Label { Text = "BENCHMARK", FontAttributes = FontAttributes.Bold, FontSize = 13 },
                    selectors,
                    _run,
                    _profile,
                    _status,
                    _budget,
                    _result,
                    new Label { Text = "MACRO ENGINE", FontAttributes = FontAttributes.Bold, FontSize = 13 },
                    _macro,
                    new Label { Text = "DSP BREAKDOWN", FontAttributes = FontAttributes.Bold, FontSize = 13 },
                    _breakdown
                }
            }
        };
        card.SetDynamicResource(Border.BackgroundColorProperty, "CardBackground");
        card.SetDynamicResource(Border.StrokeProperty, "BorderColor");

        var info = new Border
        {
            Padding = 14,
            StrokeThickness = 1,
            Content = new Label
            {
                Text = "Este panel mide el DSN-like aislado. No cambia el engine de la canción actual. " +
                       "La meta inicial es mantener el promedio <40% del deadline y vigilar especialmente P99.",
                FontSize = 12
            }
        };
        info.SetDynamicResource(Border.BackgroundColorProperty, "SurfaceBackground");
        info.SetDynamicResource(Border.StrokeProperty, "BorderColor");

        Dispatcher.StartTimer(TimeSpan.FromMilliseconds(250), () => { UpdateLiveMonitor(); return true; });

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(16, 14),
                Spacing = 12,
                Children = { title, subtitle, card, info }
            }
        };
    }

    public void SetRealtimeSource(Func<DsnRealtimeTelemetrySnapshot?>? source)
    {
        _liveSource = source;
        _renderHistory.Clear();
        _lastAllocated = 0; _lastG0 = _lastG1 = _lastG2 = 0;
    }

    private void UpdateLiveMonitor()
    {
        var t = _liveSource?.Invoke();
        if (t is null) { _liveStatus.Text = "Sin DSN activo · reproduce un MIDI con DSN-like"; _live.Text = ""; _renderHistory.Clear(); _historyDrawable.SetSamples(Array.Empty<double>()); _history.Invalidate(); _liveBudget.Progress = 0; return; }
        double pct = t.DeadlineMs <= 0 ? 0 : t.MeanRenderMs / t.DeadlineMs * 100.0;
        _liveBudget.Progress = Math.Clamp(pct / 100.0, 0, 1);
        _liveStatus.Text = pct < 70 ? "✓ HOLGADO" : pct < 100 ? "⚠ PRESUPUESTO APRETADO" : "☠ THROUGHPUT INSUFICIENTE";
        long allocDelta = _lastAllocated == 0 ? 0 : Math.Max(0, t.AllocatedBytes - _lastAllocated);
        int dg0 = _lastAllocated == 0 ? 0 : Math.Max(0, t.Gen0 - _lastG0);
        int dg1 = _lastAllocated == 0 ? 0 : Math.Max(0, t.Gen1 - _lastG1);
        int dg2 = _lastAllocated == 0 ? 0 : Math.Max(0, t.Gen2 - _lastG2);
        _lastAllocated=t.AllocatedBytes; _lastG0=t.Gen0; _lastG1=t.Gen1; _lastG2=t.Gen2;
        _live.Text =
            $"Render      {t.MeanRenderMs,7:F2} ms  ({pct,5:F1}%)\n" +
            $"Deadline    {t.DeadlineMs,7:F2} ms   Worst {t.WorstRenderMs:F2}\n" +
            $"Late        {t.LateBlocks,7}/{t.Blocks,-7} Underruns {t.Underruns}\n" +
            $"Voices      {t.ActiveVoices,7}   Peak {t.PeakVoices} · Drums {t.DrumVoices}\n" +
            $"Dual VCO    {t.DualVcoVoices,7}   FM {t.FmVoices} · Sync {t.SyncVoices}\n" +
            $"Filter      {t.FilterVoices,7}   Drive {t.DriveVoices}\n" +
            $"PCM ring    {t.ManagedRingReadyBlocks,7}/{t.ManagedRingCapacity} blocks\n" +
            $"Backend     {t.BackendBufferedFrames,7}/{t.BackendBufferFrames} fr · burst {t.BackendBurstFrames}\n" +
            $"Adapt +     {t.BackendAdaptiveIncreases,7}\n" +
            $"Alloc/250ms {allocDelta/1024.0,7:F1} KiB · GC +{dg0}/+{dg1}/+{dg2}";
        _renderHistory.Enqueue(Math.Min(2.0, t.DeadlineMs <= 0 ? 0 : t.MeanRenderMs/t.DeadlineMs));
        while (_renderHistory.Count > 120) _renderHistory.Dequeue();
        _historyDrawable.SetSamples(_renderHistory);
        _history.Invalidate();
    }

    private sealed class DsnRenderBudgetDrawable : IDrawable
    {
        private double[] _samples = Array.Empty<double>();

        public void SetSamples(IEnumerable<double> samples)
            => _samples = samples.TakeLast(120).ToArray();

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            float left = 34f, right = 8f, top = 18f, bottom = 18f;
            float width = Math.Max(1f, dirtyRect.Width - left - right);
            float height = Math.Max(1f, dirtyRect.Height - top - bottom);
            float y100 = top + height * 0.5f;

            // Labels + fixed 100% deadline guide. No control grows with history.
            canvas.FontSize = 10f;
            canvas.DrawString("200%", 0, top - 6f, left - 4f, 14f, HorizontalAlignment.Right, VerticalAlignment.Center);
            canvas.DrawString("100%", 0, y100 - 7f, left - 4f, 14f, HorizontalAlignment.Right, VerticalAlignment.Center);
            canvas.DrawString("0%", 0, top + height - 7f, left - 4f, 14f, HorizontalAlignment.Right, VerticalAlignment.Center);
            canvas.DrawString("-30s", left, dirtyRect.Height - bottom + 2f, 40f, 14f, HorizontalAlignment.Left, VerticalAlignment.Center);
            canvas.DrawString("NOW", dirtyRect.Width - right - 32f, dirtyRect.Height - bottom + 2f, 32f, 14f, HorizontalAlignment.Right, VerticalAlignment.Center);

            canvas.StrokeSize = 1f;
            canvas.DrawLine(left, y100, left + width, y100);
            canvas.DrawString("DEADLINE", left + 4f, y100 - 16f, 62f, 14f, HorizontalAlignment.Left, VerticalAlignment.Center);

            if (_samples.Length < 2)
                return;

            canvas.StrokeSize = 2f;
            float step = width / 119f; // fixed 30 s window at 4 Hz
            int offset = 120 - _samples.Length;

            float X(int i) => left + (offset + i) * step;
            float Y(double ratio)
            {
                double clamped = Math.Clamp(ratio, 0.0, 2.0);
                return top + height * (float)(1.0 - clamped / 2.0);
            }

            for (int i = 1; i < _samples.Length; i++)
                canvas.DrawLine(X(i - 1), Y(_samples[i - 1]), X(i), Y(_samples[i]));
        }
    }

    private async void RunBenchmark(object? sender, EventArgs e)
    {
        if (!_run.IsEnabled) return;

        int voices = int.Parse((string)_voices.SelectedItem);
        int frames = int.Parse((string)_block.SelectedItem);
        string presetName = (string)_preset.SelectedItem;

        _run.IsEnabled = false;
        _status.Text = $"Midiendo {voices} voces × {frames} frames · {presetName}…";
        _result.Text = "";
        _budget.Progress = 0;

        try
        {
            DsnLikePatch patch = BuildPatch(presetName);
            var r = await Task.Run(() => DsnLikeBenchmark.Run(
                voices: voices,
                framesPerBlock: frames,
                blocks: 500,
                patch: patch));

            double realtimeFactor = r.MeanMillisecondsPerBlock <= 0
                ? 0 : r.DeadlineMilliseconds / r.MeanMillisecondsPerBlock;

            string verdict = r.P99DeadlinePercent < 40 ? "✓ HOLGADO"
                : r.P99DeadlinePercent < 70 ? "△ UTILIZABLE"
                : r.P99DeadlinePercent < 100 ? "⚠ CERCA DEL LÍMITE"
                : "☠ NO REAL-TIME";

            _status.Text = verdict;
            _budget.Progress = Math.Clamp(r.P99DeadlinePercent / 100.0, 0.0, 1.0);
            _result.Text =
                $"Mean       {r.MeanMillisecondsPerBlock,8:F3} ms\n" +
                $"P95        {r.P95Milliseconds,8:F3} ms\n" +
                $"P99        {r.P99Milliseconds,8:F3} ms\n" +
                $"Worst      {r.WorstMilliseconds,8:F3} ms\n" +
                $"Deadline   {r.DeadlineMilliseconds,8:F3} ms\n" +
                $"Mean DSP   {r.DeadlinePercent,8:F1} %\n" +
                $"P99 DSP    {r.P99DeadlinePercent,8:F1} %\n" +
                $"RT factor  {realtimeFactor,8:F2} x\n" +
                $"Throughput {r.MillionVoiceSamplesPerSecond,8:F2} Mvoice-s/s\n" +
                $"Allocated  {r.AllocatedBytes,8} B";
        }
        catch (Exception ex)
        {
            _status.Text = "Benchmark falló";
            _result.Text = ex.ToString();
        }
        finally
        {
            _run.IsEnabled = true;
        }
    }


    private void BuildBreakdownHeader()
    {
        _breakdown.Clear();
        AddCell("Etapa", 0, 0, true);
        AddCell("Total", 1, 0, true);
        AddCell("Δ ms", 2, 0, true);
        AddCell("%", 3, 0, true);
    }

    private void ShowBreakdown(DsnStageBreakdown result)
    {
        BuildBreakdownHeader();
        int row = 1;
        foreach (var timing in result.Timings)
        {
            AddCell(timing.Stage, 0, row);
            AddCell($"{timing.TotalMilliseconds:F3}", 1, row);
            AddCell($"{timing.IncrementalMilliseconds:F3}", 2, row);
            AddCell($"{timing.PercentOfFull:F1}", 3, row);
            row++;
        }
    }

    private void AddCell(string text, int column, int row, bool bold = false)
    {
        var label = new Label
        {
            Text = text,
            FontSize = 11,
            FontFamily = column == 0 ? null : "monospace",
            FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None
        };
        _breakdown.Add(label, column, row);
    }

    private async void RunProfiler(object? sender, EventArgs e)
    {
        if (!_profile.IsEnabled || !_run.IsEnabled) return;

        int voices = int.Parse((string)_voices.SelectedItem);
        int frames = int.Parse((string)_block.SelectedItem);
        string presetName = (string)_preset.SelectedItem;

        _profile.IsEnabled = false;
        _run.IsEnabled = false;
        _status.Text = $"Perfilando {voices} voces × {frames} frames…";
        BuildBreakdownHeader();

        try
        {
            DsnLikePatch patch = BuildPatch(presetName);
            var data = await Task.Run(() => (
                Breakdown: DsnLikeBenchmark.RunBreakdown(voices, frames, blocks: 180),
                Macro: DsnLikeBenchmark.RunMacroProfile(voices, frames, patch, blocks: 180)));
            ShowBreakdown(data.Breakdown);
            _macro.Text =
                $"Engine mean        {data.Macro.EngineMeanMs,8:F3} ms\n" +
                $"Direct voices      {data.Macro.DirectVoicesMeanMs,8:F3} ms\n" +
                $"Diagnostic full    {data.Macro.DiagnosticFullMs,8:F3} ms\n" +
                $"Engine orchestrat. {data.Macro.EngineOrchestrationMs,8:F3} ms\n" +
                $"Voice kernel gap   {data.Macro.VoiceKernelGapMs,8:F3} ms\n" +
                $"Architecture gap   {data.Macro.ArchitectureGapMs,8:F3} ms  ({data.Macro.ArchitectureGapPercent:F1}%)";
            _status.Text = "Perfil DSP completo";
        }
        catch (Exception ex)
        {
            _status.Text = "Profiler falló";
            _result.Text = ex.ToString();
        }
        finally
        {
            _profile.IsEnabled = true;
            _run.IsEnabled = true;
        }
    }

    private static DsnLikePatch BuildPatch(string name) => name switch
    {
        "Dual VCO + Filter" => DsnLikePatch.Default with
        {
            Osc1Level = 0.65f, Osc2Level = 0.55f, CutoffHz = 7000f
        },
        "FM" => DsnLikePatch.Default with
        {
            FmAmount = 0.55f, Osc2Semitones = 12f, CutoffHz = 9000f
        },
        "Hard Sync" => DsnLikePatch.Default with
        {
            HardSync = true, Osc2Semitones = 19f, Osc1Level = 0.8f, Osc2Level = 0.35f
        },
        "Resonant + LFO" => DsnLikePatch.Default with
        {
            CutoffHz = 4500f, Resonance = 0.82f, LfoHz = 7f,
            LfoToCutoff = 0.65f, LfoToPulseWidth = 0.5f
        },
        "Drive" => DsnLikePatch.Default with
        {
            Drive = 0.85f, OutputGain = 0.7f, CutoffHz = 8000f
        },
        "EVERYTHING ☠" => DsnLikePatch.Default with
        {
            Osc1Wave = DsnOscillatorWave.Pulse,
            Osc2Wave = DsnOscillatorWave.Saw,
            Osc1Level = 0.7f, Osc2Level = 0.55f,
            Osc2Semitones = 12f,
            HardSync = true, FmAmount = 0.45f,
            CutoffHz = 5200f, Resonance = 0.78f,
            EnvelopeToCutoff = 0.35f,
            LfoHz = 8f, LfoToPitch = 0.08f,
            LfoToCutoff = 0.55f, LfoToPulseWidth = 0.45f,
            Drive = 0.7f
        },
        _ => DsnLikePatch.Default
    };
}
