using MIDIRift.CleanRoom.Features.Midi;

namespace MIDIRift.CleanRoom.Features.Midi.Synthesis;

public enum GmInstrumentFamily : byte
{
    Piano,
    ChromaticPercussion,
    Organ,
    Guitar,
    Bass,
    Strings,
    Ensemble,
    Brass,
    Reed,
    Pipe,
    SynthLead,
    SynthPad,
    SynthEffects,
    Ethnic,
    Percussive,
    SoundEffects
}

public readonly record struct EnvelopeProfile(
    float AttackSeconds,
    float DecaySeconds,
    float SustainLevel,
    float ReleaseSeconds,
    int AntiPopSamples)
{
    public static GmInstrumentFamily FamilyForProgram(int program)
    {
        int normalizedProgram = Math.Clamp(program, 0, 127);
        return (GmInstrumentFamily)(normalizedProgram / 8);
    }

    public static EnvelopeProfile ForProgram(int program, ChiptuneWaveType waveType)
    {
        GmInstrumentFamily family = FamilyForProgram(program);
        EnvelopeProfile familyProfile = family switch
        {
            GmInstrumentFamily.Piano => new EnvelopeProfile(0.0018f, 0.180f, 0.48f, 0.115f, 24),
            GmInstrumentFamily.ChromaticPercussion => new EnvelopeProfile(0.0012f, 0.240f, 0.25f, 0.090f, 20),
            GmInstrumentFamily.Organ => new EnvelopeProfile(0.0045f, 0.035f, 0.96f, 0.075f, 32),
            GmInstrumentFamily.Guitar => new EnvelopeProfile(0.0018f, 0.145f, 0.56f, 0.105f, 24),
            GmInstrumentFamily.Bass => new EnvelopeProfile(0.0035f, 0.105f, 0.76f, 0.125f, 32),
            GmInstrumentFamily.Strings => new EnvelopeProfile(0.028f, 0.120f, 0.88f, 0.260f, 64),
            GmInstrumentFamily.Ensemble => new EnvelopeProfile(0.038f, 0.160f, 0.84f, 0.330f, 72),
            GmInstrumentFamily.Brass => new EnvelopeProfile(0.010f, 0.085f, 0.83f, 0.145f, 40),
            GmInstrumentFamily.Reed => new EnvelopeProfile(0.012f, 0.075f, 0.86f, 0.135f, 40),
            GmInstrumentFamily.Pipe => new EnvelopeProfile(0.018f, 0.080f, 0.90f, 0.180f, 48),
            GmInstrumentFamily.SynthLead => new EnvelopeProfile(0.0030f, 0.055f, 0.84f, 0.090f, 28),
            GmInstrumentFamily.SynthPad => new EnvelopeProfile(0.055f, 0.210f, 0.80f, 0.420f, 96),
            GmInstrumentFamily.SynthEffects => new EnvelopeProfile(0.020f, 0.180f, 0.68f, 0.300f, 64),
            GmInstrumentFamily.Ethnic => new EnvelopeProfile(0.0025f, 0.150f, 0.58f, 0.120f, 28),
            GmInstrumentFamily.Percussive => new EnvelopeProfile(0.0010f, 0.095f, 0.18f, 0.060f, 16),
            _ => new EnvelopeProfile(0.0020f, 0.120f, 0.46f, 0.085f, 20)
        };

        return ApplyWaveCharacter(familyProfile, waveType).Normalize();
    }

    public static EnvelopeProfile ForWaveType(ChiptuneWaveType waveType)
    {
        return ApplyWaveCharacter(
            new EnvelopeProfile(0.0035f, 0.070f, 0.80f, 0.105f, 32),
            waveType).Normalize();
    }

    private static EnvelopeProfile ApplyWaveCharacter(EnvelopeProfile profile, ChiptuneWaveType waveType)
    {
        float attackScale = 1f;
        float decayScale = 1f;
        float sustainOffset = 0f;
        float releaseScale = 1f;
        int antiPopSamples = profile.AntiPopSamples;

        switch (waveType)
        {
            case ChiptuneWaveType.Noise:
                attackScale = 0.55f;
                decayScale = 0.72f;
                sustainOffset = -0.10f;
                releaseScale = 0.70f;
                antiPopSamples = Math.Min(antiPopSamples, 32);
                break;
            case ChiptuneWaveType.BassHybrid:
                attackScale = 1.10f;
                decayScale = 1.18f;
                sustainOffset = 0.05f;
                releaseScale = 1.15f;
                break;
            case ChiptuneWaveType.Sine:
                attackScale = 1.20f;
                decayScale = 1.10f;
                sustainOffset = 0.05f;
                releaseScale = 1.15f;
                break;
            case ChiptuneWaveType.Triangle:
                attackScale = 1.15f;
                decayScale = 1.08f;
                sustainOffset = 0.04f;
                releaseScale = 1.12f;
                break;
            case ChiptuneWaveType.Saw:
                attackScale = 0.85f;
                decayScale = 0.92f;
                sustainOffset = -0.04f;
                releaseScale = 0.95f;
                break;
            case ChiptuneWaveType.Pulse25:
                attackScale = 0.90f;
                decayScale = 0.94f;
                sustainOffset = -0.02f;
                releaseScale = 0.92f;
                break;
            case ChiptuneWaveType.Pulse50:
            default:
                break;
        }

        return new EnvelopeProfile(
            profile.AttackSeconds * attackScale,
            profile.DecaySeconds * decayScale,
            Math.Clamp(profile.SustainLevel + sustainOffset, 0f, 1f),
            profile.ReleaseSeconds * releaseScale,
            antiPopSamples);
    }

    public EnvelopeProfile Normalize()
    {
        return new EnvelopeProfile(
            Math.Clamp(AttackSeconds, 0.0001f, 30f),
            Math.Clamp(DecaySeconds, 0.0001f, 30f),
            Math.Clamp(SustainLevel, 0f, 1f),
            Math.Clamp(ReleaseSeconds, 0.0001f, 30f),
            Math.Clamp(AntiPopSamples, 1, 4096));
    }
}
