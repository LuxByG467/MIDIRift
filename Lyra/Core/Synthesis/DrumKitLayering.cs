namespace MIDIRift.CleanRoom.Features.Midi.Synthesis;

public enum DrumVoiceKind : byte
{
    None = 0,
    Kick,
    Snare,
    Clap,
    ClosedHiHat,
    OpenHiHat,
    Tom,
    Cymbal,
    Percussion
}

public static class DrumKitLayering
{
    public static DrumVoiceKind Classify(int note)
    {
        return note switch
        {
            35 or 36 => DrumVoiceKind.Kick,
            38 or 40 => DrumVoiceKind.Snare,
            39 => DrumVoiceKind.Clap,
            42 or 44 => DrumVoiceKind.ClosedHiHat,
            46 => DrumVoiceKind.OpenHiHat,
            41 or 43 or 45 or 47 or 48 or 50 => DrumVoiceKind.Tom,
            49 or 51 or 52 or 53 or 55 or 57 or 59 => DrumVoiceKind.Cymbal,
            _ => DrumVoiceKind.Percussion
        };
    }

    public static EnvelopeProfile EnvelopeFor(int note)
    {
        return Classify(note) switch
        {
            DrumVoiceKind.Kick => new EnvelopeProfile(0.0005f, 0.22f, 0f, 0.035f, 12),
            DrumVoiceKind.Snare => new EnvelopeProfile(0.0005f, 0.16f, 0f, 0.035f, 12),
            DrumVoiceKind.Clap => new EnvelopeProfile(0.0005f, 0.12f, 0f, 0.025f, 12),
            DrumVoiceKind.ClosedHiHat => new EnvelopeProfile(0.0002f, 0.055f, 0f, 0.012f, 8),
            DrumVoiceKind.OpenHiHat => new EnvelopeProfile(0.0002f, 0.34f, 0f, 0.055f, 8),
            DrumVoiceKind.Tom => new EnvelopeProfile(0.0005f, 0.20f, 0f, 0.035f, 12),
            DrumVoiceKind.Cymbal => new EnvelopeProfile(0.0002f, 0.62f, 0f, 0.09f, 8),
            _ => new EnvelopeProfile(0.0005f, 0.11f, 0f, 0.025f, 10)
        };
    }

    public static float Generate(ref VoiceRenderState render, int note, int sampleRate)
    {
        DrumVoiceKind kind = render.DrumKind;
        float time = render.DrumAgeSamples / (float)sampleRate;
        float rawNoise = NextNoise(ref render.NoiseState);
        float sample;

        switch (kind)
        {
            case DrumVoiceKind.Kick:
            {
                // Cuerpo del clean-room, pero con un barrido algo más contenido y
                // el click del legacy reducido para evitar fatiga en mezclas densas.
                float frequency = 42f + 105f * FastAudioMath.ExpNeg(time * 26f);
                AdvancePhase(ref render.Phase, frequency, sampleRate);
                float body = FastAudioMath.Sin01(render.Phase);
                float click = LowPass(ref render.DrumFilterState, rawNoise, 3200f, sampleRate)
                              * FastAudioMath.ExpNeg(time * 105f);
                sample = body * 0.96f + click * 0.11f;
                break;
            }
            case DrumVoiceKind.Snare:
            {
                // Más cuerpo y menos arena blanca que el clean-room original.
                AdvancePhase(ref render.Phase, 185f, sampleRate);
                AdvancePhase(ref render.SecondaryPhase, 292f, sampleRate);
                float body = FastAudioMath.Sin01(render.Phase) * 0.50f +
                             FastAudioMath.Sin01(render.SecondaryPhase) * 0.16f;
                float softNoise = LowPass(ref render.DrumFilterState, rawNoise, 5200f, sampleRate);
                sample = body + softNoise * 0.56f;
                break;
            }
            case DrumVoiceKind.Clap:
            {
                float burst = (time < 0.012f || (time > 0.020f && time < 0.032f) ||
                               (time > 0.040f && time < 0.054f)) ? 1f : 0.28f;
                float softNoise = LowPass(ref render.DrumFilterState, rawNoise, 4800f, sampleRate);
                sample = softNoise * burst * 0.84f;
                break;
            }
            case DrumVoiceKind.ClosedHiHat:
            case DrumVoiceKind.OpenHiHat:
            {
                // Conserva el brillo metálico por capas, pero desplaza los parciales
                // hacia abajo y los mezcla con ruido filtrado al estilo legacy.
                AdvancePhase(ref render.Phase, 3_180f, sampleRate);
                AdvancePhase(ref render.SecondaryPhase, 4_760f, sampleRate);
                float metal = (render.Phase < 0.5f ? 1f : -1f) * 0.20f +
                              (render.SecondaryPhase < 0.5f ? 1f : -1f) * 0.14f;
                float noise = LowPass(ref render.DrumFilterState, rawNoise,
                    kind == DrumVoiceKind.ClosedHiHat ? 6_200f : 5_400f, sampleRate);
                sample = metal + noise * 0.56f;
                break;
            }
            case DrumVoiceKind.Tom:
            {
                float baseFrequency = note switch
                {
                    41 => 82f,
                    43 => 98f,
                    45 => 117f,
                    47 => 139f,
                    48 => 156f,
                    50 => 185f,
                    _ => 120f
                };
                float frequency = baseFrequency * (1f + 0.20f * FastAudioMath.ExpNeg(time * 20f));
                AdvancePhase(ref render.Phase, frequency, sampleRate);
                float attackNoise = LowPass(ref render.DrumFilterState, rawNoise, 3600f, sampleRate)
                                    * FastAudioMath.ExpNeg(time * 38f);
                sample = FastAudioMath.Sin01(render.Phase) * 0.92f + attackNoise * 0.09f;
                break;
            }
            case DrumVoiceKind.Cymbal:
            {
                // El clean-room usaba parciales sobre 5–7 kHz muy dominantes.
                // Aquí quedan como una capa tenue sobre ruido suavizado.
                AdvancePhase(ref render.Phase, 2_760f, sampleRate);
                AdvancePhase(ref render.SecondaryPhase, 4_120f, sampleRate);
                float a = render.Phase < 0.5f ? 1f : -1f;
                float b = render.SecondaryPhase < 0.5f ? 1f : -1f;
                float noise = LowPass(ref render.DrumFilterState, rawNoise, 5_800f, sampleRate);
                sample = a * 0.13f + b * 0.10f + noise * 0.70f;
                break;
            }
            default:
            {
                float frequency = 115f + (note - 35) * 6f;
                AdvancePhase(ref render.Phase, Math.Clamp(frequency, 55f, 760f), sampleRate);
                float noise = LowPass(ref render.DrumFilterState, rawNoise, 4_600f, sampleRate);
                sample = FastAudioMath.Sin01(render.Phase) * 0.43f + noise * 0.48f;
                break;
            }
        }

        // Segunda etapa muy ligera. Recorta la zona más áspera sin borrar el
        // ataque chiptune ni convertir todo el kit en percusión apagada.
        float output = LowPass(ref render.DrumFilterState2, sample,
            kind is DrumVoiceKind.ClosedHiHat or DrumVoiceKind.OpenHiHat or DrumVoiceKind.Cymbal
                ? 8_200f
                : 10_500f,
            sampleRate);

        render.DrumAgeSamples++;
        return Math.Clamp(output, -1.35f, 1.35f);
    }

    private static float LowPass(ref float state, float input, float cutoffHz, int sampleRate)
    {
        float normalized = Math.Clamp(cutoffHz / Math.Max(1f, sampleRate), 0.0001f, 0.45f);
        float alpha = 1f - FastAudioMath.ExpNeg(6.28318530718f * normalized);
        state += alpha * (input - state);
        return state;
    }

    private static void AdvancePhase(ref float phase, float frequency, int sampleRate)
    {
        phase += frequency / sampleRate;
        phase -= MathF.Floor(phase);
    }

    private static float NextNoise(ref uint noiseState)
    {
        uint state = noiseState;
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        noiseState = state;
        return state / (float)uint.MaxValue * 2f - 1f;
    }
}
