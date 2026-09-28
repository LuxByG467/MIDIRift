namespace MIDIRift.CleanRoom.Features.Midi.Synthesis;

/// <summary>
/// Aproximaciones RT sin Sin/Exp/Pow/Log/Sqrt para el hot path de Lyra.
/// La prioridad aquí es continuidad y coste determinista en audio real-time.
/// </summary>
internal static class FastAudioMath
{
    private const float InvLn2 = 1.4426950408889634f;

    // phase normalizada [0,1). Aproximación parabólica con corrección cúbica.
    public static float Sin01(float phase)
    {
        phase -= MathF.Floor(phase);
        float u = phase * 2f - 1f; // [-1,1)
        float y = -(4f * u - 4f * u * MathF.Abs(u));
        return y + 0.225f * (y * MathF.Abs(y) - y);
    }

    // 2^x sin MathF.Pow. Polinomio de grado 4 para la fracción y ScaleB para exponente.
    public static float Exp2(float x)
    {
        if (x <= -126f) return 0f;
        if (x >= 127f) return float.MaxValue;
        int n = (int)MathF.Floor(x);
        float f = x - n;
        float p = 1f + f * (0.69314718056f + f * (0.24022650695f + f * (0.05550410866f + f * 0.00961812911f)));
        return MathF.ScaleB(p, n);
    }

    public static float Log2(float x)
    {
        if (!(x > 0f)) return -126f;
        int bits = BitConverter.SingleToInt32Bits(x);
        int exponent = ((bits >> 23) & 0xFF) - 127;
        int mantissaBits = (bits & 0x7FFFFF) | 0x3F800000;
        float m = BitConverter.Int32BitsToSingle(mantissaBits); // [1,2)
        float y = m - 1f;
        // ln(1+y), alternante grado 5. Setup/control-rate, no per-sample.
        float y2 = y * y;
        float ln = y - 0.5f * y2 + y2 * y * (0.3333333333f + y * (-0.25f + y * 0.2f));
        return exponent + ln * InvLn2;
    }

    public static float Pow(float value, float exponent)
        => value <= 0f ? 0f : Exp2(Log2(value) * exponent);

    // e^-x = 2^(-x/log(2)).
    public static float ExpNeg(float x)
        => x <= 0f ? 1f : Exp2(-x * InvLn2);

    // Newton-Raphson, sin MathF.Sqrt. Suficiente para equal-power pan.
    public static float Sqrt(float x)
    {
        if (x <= 0f) return 0f;
        int bits = BitConverter.SingleToInt32Bits(x);
        float g = BitConverter.Int32BitsToSingle((bits >> 1) + 0x1FC00000);
        g = 0.5f * (g + x / g);
        g = 0.5f * (g + x / g);
        return g;
    }
}
