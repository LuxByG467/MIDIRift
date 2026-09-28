using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace MIDIRift.Synth.DsnLike;

public sealed record DsnLikeBenchmarkResult(
    int Voices,
    int FramesPerBlock,
    int Blocks,
    double MeanMillisecondsPerBlock,
    double P95Milliseconds,
    double P99Milliseconds,
    double WorstMilliseconds,
    double DeadlineMilliseconds,
    double DeadlinePercent,
    double P99DeadlinePercent,
    double MillionVoiceSamplesPerSecond,
    long AllocatedBytes);

public static class DsnLikeBenchmark
{
    public static DsnLikeBenchmarkResult Run(
        int voices = 32, int framesPerBlock = 512, int blocks = 400,
        float sampleRate = 44100f, DsnLikePatch? patch = null)
    {
        if (voices <= 0 || framesPerBlock <= 0 || blocks <= 0)
            throw new ArgumentOutOfRangeException();

        var synth = new DsnLikeSynth(voices, sampleRate, patch);
        for (int i = 0; i < voices; i++) synth.NoteOn(36 + (i % 36), 0.8f);
        var buffer = new float[framesPerBlock];
        for (int i = 0; i < 32; i++) synth.Render(buffer);

        var times = new double[blocks];
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        for (int i = 0; i < blocks; i++)
        {
            long start = Stopwatch.GetTimestamp();
            synth.Render(buffer);
            times[i] = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        }
        total.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Array.Sort(times);

        double meanMs = total.Elapsed.TotalMilliseconds / blocks;
        double deadlineMs = framesPerBlock * 1000.0 / sampleRate;
        double renderedVoiceSamples = (double)voices * framesPerBlock * blocks;
        return new(
            voices, framesPerBlock, blocks, meanMs,
            Percentile(times, .95), Percentile(times, .99), times[^1],
            deadlineMs, meanMs / deadlineMs * 100.0,
            Percentile(times, .99) / deadlineMs * 100.0,
            renderedVoiceSamples / total.Elapsed.TotalSeconds / 1_000_000.0,
            allocated);
    }


    public static DsnMacroProfile RunMacroProfile(
        int voices, int framesPerBlock, DsnLikePatch patch,
        int blocks = 220, float sampleRate = 44100f)
    {
        var actual = Run(voices, framesPerBlock, blocks, sampleRate, patch);
        double directVoices = MeasureDirectVoices(voices, framesPerBlock, blocks, sampleRate, patch);
        double diagnostic = MeasureStage(DsnBenchmarkStage.Full, voices, framesPerBlock, blocks, sampleRate);
        double orchestration = Math.Max(0.0, actual.MeanMillisecondsPerBlock - directVoices);
        double voiceVsDiagnostic = Math.Max(0.0, directVoices - diagnostic);
        double gap = Math.Max(0.0, actual.MeanMillisecondsPerBlock - diagnostic);
        return new DsnMacroProfile(actual.MeanMillisecondsPerBlock, directVoices, diagnostic,
            orchestration, voiceVsDiagnostic, gap,
            actual.MeanMillisecondsPerBlock <= 0 ? 0 : gap / actual.MeanMillisecondsPerBlock * 100.0);
    }

    private static double MeasureDirectVoices(
        int voices, int frames, int blocks, float sampleRate, DsnLikePatch patch)
    {
        var voiceArray = new DsnLikeVoice[voices];
        for (int i = 0; i < voices; i++)
        {
            var voice = new DsnLikeVoice(sampleRate, (uint)(i + 1));
            voice.ApplyPatch(patch);
            voice.NoteOn(36 + (i % 36), 0.8f);
            voiceArray[i] = voice;
        }

        var buffer = new float[frames];
        for (int b = 0; b < 32; b++)
        {
            buffer.AsSpan().Clear();
            for (int v = 0; v < voices; v++) voiceArray[v].Render(buffer);
        }

        long start = Stopwatch.GetTimestamp();
        for (int b = 0; b < blocks; b++)
        {
            buffer.AsSpan().Clear();
            for (int v = 0; v < voices; v++) voiceArray[v].Render(buffer);
        }
        long end = Stopwatch.GetTimestamp();
        GC.KeepAlive(buffer);
        return (end - start) * 1000.0 / Stopwatch.Frequency / blocks;
    }

    /// <summary>
    /// Cumulative micro-profiler. It deliberately uses a tiny diagnostic kernel
    /// instead of Stopwatch calls inside the audio sample loop, because timing
    /// every operation would distort the result more than it measured.
    /// </summary>
    public static DsnStageBreakdown RunBreakdown(
        int voices = 64, int framesPerBlock = 512, int blocks = 180,
        float sampleRate = 44100f)
    {
        var stages = new[]
        {
            DsnBenchmarkStage.LoopBaseline,
            DsnBenchmarkStage.Vco1,
            DsnBenchmarkStage.DualVco,
            DsnBenchmarkStage.EnvelopeVca,
            DsnBenchmarkStage.Filter,
            DsnBenchmarkStage.ControlMod,
            DsnBenchmarkStage.Fm,
            DsnBenchmarkStage.HardSync,
            DsnBenchmarkStage.Drive,
            DsnBenchmarkStage.OutputGain,
            DsnBenchmarkStage.MixAccumulation,
            DsnBenchmarkStage.Full
        };

        var totals = new double[stages.Length];
        for (int i = 0; i < stages.Length; i++)
            totals[i] = MeasureStage(stages[i], voices, framesPerBlock, blocks, sampleRate);

        double full = totals[^1];
        var rows = new List<DsnStageTiming>(stages.Length);
        double previous = 0;
        double voiceSamples = (double)voices * framesPerBlock;
        for (int i = 0; i < stages.Length; i++)
        {
            double delta = Math.Max(0, totals[i] - previous);
            rows.Add(new(
                StageName(stages[i]),
                totals[i],
                delta,
                full <= 0 ? 0 : delta / full * 100.0,
                delta * 1_000_000.0 / voiceSamples));
            previous = totals[i];
        }

        return new(voices, framesPerBlock, blocks,
            framesPerBlock * 1000.0 / sampleRate, rows);
    }

    private static double MeasureStage(
        DsnBenchmarkStage stage, int voices, int frames, int blocks, float sampleRate)
    {
        var states = new DiagnosticVoice[voices];
        for (int i = 0; i < voices; i++)
            states[i] = new DiagnosticVoice(i, sampleRate);

        // Warm-up.
        float sink = 0;
        for (int b = 0; b < 24; b++)
            for (int v = 0; v < voices; v++)
                sink += RenderDiagnostic(ref states[v], stage, frames, sampleRate);

        long start = Stopwatch.GetTimestamp();
        for (int b = 0; b < blocks; b++)
            for (int v = 0; v < voices; v++)
                sink += RenderDiagnostic(ref states[v], stage, frames, sampleRate);
        long end = Stopwatch.GetTimestamp();

        GC.KeepAlive(sink);
        return (end - start) * 1000.0 / Stopwatch.Frequency / blocks;
    }

    private struct DiagnosticVoice
    {
        public float P1, P2, Env, Low, Band, Lfo, Hold;
        public float Inc1, Inc2;
        public uint Noise;
        public int Control;

        public DiagnosticVoice(int index, float sampleRate)
        {
            P1 = P2 = Low = Band = Lfo = Hold = 0;
            Env = 0.8f;
            float hz = 110f + (index % 36) * 7.5f;
            Inc1 = hz / sampleRate;
            Inc2 = hz * 2f / sampleRate;
            Noise = (uint)(index + 1) * 747796405u + 2891336453u;
            Control = 1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static float RenderDiagnostic(
        ref DiagnosticVoice s, DsnBenchmarkStage stage, int frames, float sampleRate)
    {
        bool vco1 = stage >= DsnBenchmarkStage.Vco1;
        bool dual = stage >= DsnBenchmarkStage.DualVco;
        bool env = stage >= DsnBenchmarkStage.EnvelopeVca;
        bool filter = stage >= DsnBenchmarkStage.Filter;
        bool control = stage >= DsnBenchmarkStage.ControlMod;
        bool fm = stage >= DsnBenchmarkStage.Fm;
        bool sync = stage >= DsnBenchmarkStage.HardSync;
        bool drive = stage >= DsnBenchmarkStage.Drive;
        bool outputGain = stage >= DsnBenchmarkStage.OutputGain;
        bool mixAccumulation = stage >= DsnBenchmarkStage.MixAccumulation;
        bool full = stage == DsnBenchmarkStage.Full;

        float sum = 0;
        float p1=s.P1, p2=s.P2, e=s.Env, low=s.Low, band=s.Band, lfo=s.Lfo;
        int ctl=s.Control;
        float inc1=s.Inc1, inc2=s.Inc2;
        float f = 0.62f, damping = 0.75f;

        for (int i = 0; i < frames; i++)
        {
            if (control && --ctl <= 0)
            {
                lfo += 32f * 5f / sampleRate;
                if (lfo >= 1f) lfo -= 1f;
                float lv = 1f - 4f * DsnFastMath.FastAbs(lfo - .5f);
                f = DsnFastMath.Clamp(.62f * (1f + lv * .15f), .001f, .99f);
                ctl = 32;
            }

            // Baseline intentionally retains the loop + accumulation only.
            if (!vco1)
            {
                sum += 0.000001f;
                continue;
            }

            float o2 = dual ? (p2 + p2 - 1f) : 0f;
            float fmScale = fm ? Math.Max(.05f, 1f + o2 * .45f) : 1f;
            float o1 = p1 < .5f ? 1f : -1f;

            p1 += inc1 * fmScale;
            bool wrapped = p1 >= 1f;
            if (wrapped) p1 -= 1f;
            if (dual)
            {
                p2 += inc2;
                if (p2 >= 1f) p2 -= 1f;
                if (sync && wrapped) p2 = 0f;
            }

            float x = dual ? o1 * .7f + o2 * .55f : o1;
            if (filter)
            {
                float high = x - low - damping * band;
                band += f * high;
                low += f * band;
                x = low;
            }

            if (env) x *= e;
            if (drive) x = DsnFastMath.FastDrive(x * 3.5f);
            if (outputGain) x *= 0.8f;
            if (mixAccumulation) sum += x;
            else sum += x * 0.999999f;
            if (full)
            {
                // Residual control/bookkeeping representative of the generic route.
                s.Hold = x * (.92f + .08f * (1f - 4f * DsnFastMath.FastAbs(lfo - .5f)));
            }
        }

        s.P1=p1; s.P2=p2; s.Env=e; s.Low=low; s.Band=band; s.Lfo=lfo; s.Control=ctl;
        return sum;
    }

    private static string StageName(DsnBenchmarkStage stage) => stage switch
    {
        DsnBenchmarkStage.LoopBaseline => "Loop baseline",
        DsnBenchmarkStage.Vco1 => "+ VCO1 / Phase",
        DsnBenchmarkStage.DualVco => "+ VCO2 / Mix",
        DsnBenchmarkStage.EnvelopeVca => "+ ADSR / VCA",
        DsnBenchmarkStage.Filter => "+ SVF",
        DsnBenchmarkStage.ControlMod => "+ LFO / Control",
        DsnBenchmarkStage.Fm => "+ FM",
        DsnBenchmarkStage.HardSync => "+ Hard Sync",
        DsnBenchmarkStage.Drive => "+ Drive",
        DsnBenchmarkStage.OutputGain => "+ Output gain",
        DsnBenchmarkStage.MixAccumulation => "+ Mix accumulation",
        _ => "+ Bookkeeping / residual"
    };

    private static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0) return 0;
        int index = (int)Math.Ceiling(p * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }
}

public sealed record DsnMacroProfile(
    double EngineMeanMs,
    double DirectVoicesMeanMs,
    double DiagnosticFullMs,
    double EngineOrchestrationMs,
    double VoiceKernelGapMs,
    double ArchitectureGapMs,
    double ArchitectureGapPercent);
