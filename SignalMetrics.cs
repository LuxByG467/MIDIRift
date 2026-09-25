using System;

namespace MIDIRift;

public readonly struct SignalMetrics
{
    public readonly float Rms;
    public readonly float RmsDb;
    public readonly float Peak;
    public readonly float PeakDb;
    public readonly float CrestDb;
    public readonly bool Clipped;

    public SignalMetrics(float rms, float rmsDb, float peak, float peakDb, float crestDb, bool clipped)
    {
        Rms = rms;
        RmsDb = rmsDb;
        Peak = peak;
        PeakDb = peakDb;
        CrestDb = crestDb;
        Clipped = clipped;
    }

    public static SignalMetrics Silence => new(0f, -120f, 0f, -120f, 0f, false);
}

public static class SignalMetricsCalculator
{
    public static SignalMetrics Calculate(float[] samples, int count = -1)
    {
        if (samples == null || samples.Length == 0) return SignalMetrics.Silence;
        if (count < 0 || count > samples.Length) count = samples.Length;
        if (count <= 0) return SignalMetrics.Silence;

        double sumSq = 0.0;
        float peak = 0f;
        for (int i = 0; i < count; i++)
        {
            float v = samples[i];
            float a = MathF.Abs(v);
            if (a > peak) peak = a;
            sumSq += (double)v * v;
        }

        float rms = (float)Math.Sqrt(sumSq / count);
        float rmsDb = LinearToDb(rms);
        float peakDb = LinearToDb(peak);
        float crestDb = peak > 0f && rms > 0f ? peakDb - rmsDb : 0f;
        return new SignalMetrics(rms, rmsDb, peak, peakDb, crestDb, peak > 1f);
    }

    private static float LinearToDb(float value)
        => value <= 0.000001f ? -120f : 20f * MathF.Log10(value);
}
