using System.Runtime.CompilerServices;

namespace MIDIRift.Synth.DsnLike;

internal static class DsnFastMath
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Clamp(float x, float min, float max)
        => x < min ? min : (x > max ? max : x);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Wrap01(float phase)
        => phase >= 1f ? phase - 1f : (phase < 0f ? phase + 1f : phase);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float FastAbs(float x) => x < 0f ? -x : x;

    /// <summary>Fast sine for normalized phase [0,1). Parabolic approximation with a small correction.
    /// No transcendental operation in the audio sample loop.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Sin01(float phase)
    {
        float x = phase < 0.5f ? phase * 4f : phase * 4f - 4f; // [-2,2]
        float y = x * (2f - FastAbs(x));
        return 0.225f * (y * FastAbs(y) - y) + y;
    }

    /// <summary>
    /// Cheap soft clip. No tanh/exp in the sample loop.
    /// x / (1 + |x|), normalized to keep low-level gain intuitive.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SoftClip(float x)
        => x / (1f + FastAbs(x));

    /// <summary>
    /// Division-free drive saturator for the audio hot path. The cubic section
    /// is smooth around zero and clamps only at the outer rails.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float FastDrive(float x)
    {
        if (x >= 1.5f) return 1f;
        if (x <= -1.5f) return -1f;
        float y = x * (1f - (4f / 27f) * x * x);
        return Clamp(y, -1f, 1f);
    }

    /// <summary>
    /// Polynomial sine approximation for x in [0, pi/2].
    /// Used only at control-rate for the SVF coefficient.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SinHalfPi(float x)
    {
        float x2 = x * x;
        return x * (1f - x2 * (1f / 6f) + x2 * x2 * (1f / 120f));
    }
}
