using System.Numerics;
using System.Diagnostics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using MIDIRift.CleanRoom.Features.Midi;

namespace MIDIRift.CleanRoom.Features.Midi.Synthesis;

public enum VoiceEnvelopeStage : byte
{
    Inactive = 0,
    Attack = 1,
    Decay = 2,
    Sustain = 3,
    Release = 4
}

public struct VoiceLogicalState
{
    public bool Active;
    public int ChannelIndex;
    public int Note;
    public float Velocity;
    public bool KeyHeld;
    public bool SustainHeld;
    public bool SostenutoHeld;
    public long StartSequence;
    public ChiptuneWaveType WaveType;
    public bool IsPercussion;

    public void Start(int channelIndex, int note, float velocity, ChiptuneWaveType waveType, bool isPercussion, long startSequence)
    {
        Active = true;
        ChannelIndex = channelIndex;
        Note = Math.Clamp(note, 0, 127);
        Velocity = Math.Clamp(velocity, 0f, 1f);
        KeyHeld = true;
        SustainHeld = false;
        SostenutoHeld = false;
        StartSequence = startSequence;
        WaveType = waveType;
        IsPercussion = isPercussion;
    }

    public void Reset()
    {
        Active = false;
        ChannelIndex = -1;
        Note = 0;
        Velocity = 0f;
        KeyHeld = false;
        SustainHeld = false;
        SostenutoHeld = false;
        StartSequence = 0;
        WaveType = ChiptuneWaveType.Pulse50;
        IsPercussion = false;
    }
}

public struct VoiceRenderState
{
    public float Phase;
    public float EnvelopeLevel;
    public float AttackIncrement;
    public float DecayIncrement;
    public float SustainLevel;
    public float ReleaseIncrement;
    public float ReleaseSeconds;
    public VoiceEnvelopeStage EnvelopeStage;
    public IWaveGenerator? Generator;
    public uint NoiseState;
    public float AntiPopGain;
    public float AntiPopIncrement;
    public DrumVoiceKind DrumKind;
    public int DrumAgeSamples;
    public float SecondaryPhase;
    public float DrumFilterState;
    public float DrumFilterState2;
    public float LfoPhase;
    public int LfoAgeSamples;
    public float LfoRateHz;
    public float LfoDelaySeconds;
    public float LfoFadeSeconds;
    public float LfoBaseDepthSemitones;
    public float LfoWheelDepthSemitones;
    public float CurrentNote;
    public float TargetNote;
    public float PortamentoStep;
    public int PortamentoSamplesRemaining;
    public float CurrentFrequency;
    public float TargetFrequency;
    public float PortamentoMultiplier;
    public float VibratoRatio;
    public float VibratoRatioStep;
    public int LfoControlCountdown;
    public float ToneLowState;
    public float ToneBandState;

    public void Start(EnvelopeProfile profile, LfoProfile lfoProfile, ChiptuneWaveType waveType, bool isPercussion, int note, int channelIndex, int sampleRate)
    {
        EnvelopeProfile normalized = profile.Normalize();
        int attackSamples = Math.Max(1, (int)MathF.Round(normalized.AttackSeconds * sampleRate));
        int decaySamples = Math.Max(1, (int)MathF.Round(normalized.DecaySeconds * sampleRate));

        Phase = 0f;
        EnvelopeLevel = 0f;
        AttackIncrement = 1f / attackSamples;
        SustainLevel = normalized.SustainLevel;
        DecayIncrement = (1f - SustainLevel) / decaySamples;
        ReleaseIncrement = 0f;
        ReleaseSeconds = normalized.ReleaseSeconds;
        EnvelopeStage = VoiceEnvelopeStage.Attack;
        Generator = isPercussion ? null : WaveGeneratorRegistry.Resolve(waveType);
        DrumKind = isPercussion ? DrumKitLayering.Classify(note) : DrumVoiceKind.None;
        DrumAgeSamples = 0;
        SecondaryPhase = 0f;
        DrumFilterState = 0f;
        DrumFilterState2 = 0f;
        LfoPhase = 0f;
        LfoAgeSamples = 0;
        LfoRateHz = isPercussion ? 0f : Math.Max(0f, lfoProfile.RateHz);
        LfoDelaySeconds = Math.Max(0f, lfoProfile.DelaySeconds);
        LfoFadeSeconds = Math.Max(0.0001f, lfoProfile.FadeSeconds);
        LfoBaseDepthSemitones = isPercussion ? 0f : Math.Max(0f, lfoProfile.BaseDepthSemitones);
        LfoWheelDepthSemitones = isPercussion ? 0f : Math.Max(0f, lfoProfile.WheelDepthSemitones);
        CurrentNote = note;
        TargetNote = note;
        PortamentoStep = 0f;
        PortamentoSamplesRemaining = 0;
        CurrentFrequency = NoteToFrequency(note);
        TargetFrequency = CurrentFrequency;
        PortamentoMultiplier = 1f;
        VibratoRatio = 1f;
        VibratoRatioStep = 0f;
        LfoControlCountdown = 0;
        ToneLowState = 0f;
        ToneBandState = 0f;
        NoiseState = unchecked((uint)(0x9E3779B9u ^ (uint)(note * 747796405) ^ ((uint)channelIndex * 2891336453u)));
        if (NoiseState == 0)
            NoiseState = 0xA341316Cu;

        AntiPopGain = 0f;
        AntiPopIncrement = 1f / normalized.AntiPopSamples;
    }

    public void BeginRelease(int sampleRate)
    {
        if (EnvelopeStage == VoiceEnvelopeStage.Inactive || EnvelopeStage == VoiceEnvelopeStage.Release)
            return;

        int releaseSamples = Math.Max(1, (int)MathF.Round(Math.Max(ReleaseSeconds, 0.0001f) * sampleRate));
        ReleaseIncrement = Math.Max(EnvelopeLevel, 0.000001f) / releaseSamples;
        EnvelopeStage = VoiceEnvelopeStage.Release;
    }

    public bool AdvanceEnvelope()
    {
        if (EnvelopeStage == VoiceEnvelopeStage.Attack)
        {
            EnvelopeLevel += AttackIncrement;
            if (EnvelopeLevel >= 1f)
            {
                EnvelopeLevel = 1f;
                EnvelopeStage = VoiceEnvelopeStage.Decay;
            }
        }
        else if (EnvelopeStage == VoiceEnvelopeStage.Decay)
        {
            EnvelopeLevel -= DecayIncrement;
            if (EnvelopeLevel <= SustainLevel)
            {
                EnvelopeLevel = SustainLevel;
                if (SustainLevel <= 0.000001f)
                {
                    Reset();
                    return false;
                }
                EnvelopeStage = VoiceEnvelopeStage.Sustain;
            }
        }
        else if (EnvelopeStage == VoiceEnvelopeStage.Sustain)
        {
            EnvelopeLevel = SustainLevel;
        }
        else if (EnvelopeStage == VoiceEnvelopeStage.Release)
        {
            EnvelopeLevel -= ReleaseIncrement;
            if (EnvelopeLevel <= 0f)
            {
                Reset();
                return false;
            }
        }
        else
        {
            Reset();
            return false;
        }

        if (AntiPopGain < 1f)
        {
            AntiPopGain += AntiPopIncrement;
            if (AntiPopGain > 1f)
                AntiPopGain = 1f;
        }

        return true;
    }

    /// <summary>
    /// RT-5. Avanza ADSR/anti-pop exactamente en cantidad de frames pero sin
    /// ejecutar el branch de envelope una vez por muestra. Sólo se usa cuando
    /// la voz no está contribuyendo audio.
    /// </summary>
    public bool AdvanceEnvelopeFrames(int frames)
    {
        int remaining = Math.Max(0, frames);
        while (remaining > 0)
        {
            switch (EnvelopeStage)
            {
                case VoiceEnvelopeStage.Attack:
                {
                    if (AttackIncrement <= 0f)
                    {
                        EnvelopeLevel = 1f;
                        EnvelopeStage = VoiceEnvelopeStage.Decay;
                        continue;
                    }

                    int toBoundary = Math.Max(
                        1,
                        (int)MathF.Ceiling((1f - EnvelopeLevel) / AttackIncrement));
                    int step = Math.Min(remaining, toBoundary);
                    EnvelopeLevel += AttackIncrement * step;
                    remaining -= step;

                    if (step == toBoundary || EnvelopeLevel >= 1f)
                    {
                        EnvelopeLevel = 1f;
                        EnvelopeStage = VoiceEnvelopeStage.Decay;
                    }
                    break;
                }

                case VoiceEnvelopeStage.Decay:
                {
                    if (DecayIncrement <= 0f)
                    {
                        EnvelopeLevel = SustainLevel;
                        EnvelopeStage = SustainLevel <= 0.000001f
                            ? VoiceEnvelopeStage.Inactive
                            : VoiceEnvelopeStage.Sustain;
                        if (EnvelopeStage == VoiceEnvelopeStage.Inactive)
                            return false;
                        continue;
                    }

                    int toBoundary = Math.Max(
                        1,
                        (int)MathF.Ceiling((EnvelopeLevel - SustainLevel) / DecayIncrement));
                    int step = Math.Min(remaining, toBoundary);
                    EnvelopeLevel -= DecayIncrement * step;
                    remaining -= step;

                    if (step == toBoundary || EnvelopeLevel <= SustainLevel)
                    {
                        EnvelopeLevel = SustainLevel;
                        if (SustainLevel <= 0.000001f)
                        {
                            EnvelopeStage = VoiceEnvelopeStage.Inactive;
                            return false;
                        }
                        EnvelopeStage = VoiceEnvelopeStage.Sustain;
                    }
                    break;
                }

                case VoiceEnvelopeStage.Sustain:
                    EnvelopeLevel = SustainLevel;
                    remaining = 0;
                    break;

                case VoiceEnvelopeStage.Release:
                {
                    if (ReleaseIncrement <= 0f)
                    {
                        EnvelopeLevel = 0f;
                        EnvelopeStage = VoiceEnvelopeStage.Inactive;
                        return false;
                    }

                    int toBoundary = Math.Max(
                        1,
                        (int)MathF.Ceiling(EnvelopeLevel / ReleaseIncrement));
                    int step = Math.Min(remaining, toBoundary);
                    EnvelopeLevel -= ReleaseIncrement * step;
                    remaining -= step;

                    if (step == toBoundary || EnvelopeLevel <= 0f)
                    {
                        EnvelopeLevel = 0f;
                        EnvelopeStage = VoiceEnvelopeStage.Inactive;
                        return false;
                    }
                    break;
                }

                default:
                    return false;
            }
        }

        if (AntiPopGain < 1f && frames > 0)
            AntiPopGain = Math.Min(1f, AntiPopGain + AntiPopIncrement * frames);

        return EnvelopeStage != VoiceEnvelopeStage.Inactive;
    }

    public void Reset()
    {
        Phase = 0f;
        EnvelopeLevel = 0f;
        AttackIncrement = 0f;
        DecayIncrement = 0f;
        SustainLevel = 0f;
        ReleaseIncrement = 0f;
        ReleaseSeconds = 0f;
        EnvelopeStage = VoiceEnvelopeStage.Inactive;
        Generator = null;
        NoiseState = 0xA341316Cu;
        AntiPopGain = 0f;
        AntiPopIncrement = 0f;
        DrumKind = DrumVoiceKind.None;
        DrumAgeSamples = 0;
        SecondaryPhase = 0f;
        DrumFilterState = 0f;
        DrumFilterState2 = 0f;
        LfoPhase = 0f;
        LfoAgeSamples = 0;
        LfoRateHz = 0f;
        LfoDelaySeconds = 0f;
        LfoFadeSeconds = 0f;
        LfoBaseDepthSemitones = 0f;
        LfoWheelDepthSemitones = 0f;
        CurrentNote = 0f;
        TargetNote = 0f;
        PortamentoStep = 0f;
        PortamentoSamplesRemaining = 0;
        CurrentFrequency = 0f;
        TargetFrequency = 0f;
        PortamentoMultiplier = 1f;
        VibratoRatio = 1f;
        VibratoRatioStep = 0f;
        LfoControlCountdown = 0;
        ToneLowState = 0f;
        ToneBandState = 0f;
    }

    private static float NoteToFrequency(float note)
        => 440f * FastAudioMath.Exp2((note - 69f) / 12f);
}

public struct VoiceState
{
    public VoiceLogicalState Logical;
    public VoiceRenderState Render;

    public bool Active => Logical.Active;
    public int ChannelIndex => Logical.ChannelIndex;
    public int Note => Logical.Note;
    public float Velocity => Logical.Velocity;
    public long StartSequence => Logical.StartSequence;
    public VoiceEnvelopeStage EnvelopeStage => Render.EnvelopeStage;
    public float Level => Render.EnvelopeLevel;

    public void Start(
        int channelIndex,
        int note,
        float velocity,
        ChiptuneWaveType waveType,
        bool isPercussion,
        int program,
        long startSequence,
        int sampleRate)
    {
        Logical.Start(channelIndex, note, velocity, waveType, isPercussion, startSequence);
        EnvelopeProfile profile = isPercussion ? DrumKitLayering.EnvelopeFor(note) : EnvelopeProfile.ForProgram(program, waveType);
        LfoProfile lfoProfile = isPercussion ? default : LfoProfile.ForProgram(program);
        Render.Start(profile, lfoProfile, waveType, isPercussion, Logical.Note, channelIndex, sampleRate);
    }

    public void HandleNoteOff(bool sustainEnabled, int sampleRate)
    {
        if (!Logical.Active)
            return;

        Logical.KeyHeld = false;
        if (Logical.IsPercussion)
            return;

        if (sustainEnabled)
        {
            Logical.SustainHeld = true;
            return;
        }

        if (!Logical.SostenutoHeld)
            BeginRelease(sampleRate);
    }

    public void ReleaseFromSustain(int sampleRate)
    {
        if (!Logical.Active || !Logical.SustainHeld || Logical.KeyHeld)
            return;

        Logical.SustainHeld = false;
        if (!Logical.SostenutoHeld)
            BeginRelease(sampleRate);
    }

    public void CaptureSostenuto()
    {
        if (Logical.Active && !Logical.IsPercussion && Logical.KeyHeld)
            Logical.SostenutoHeld = true;
    }

    public void ReleaseFromSostenuto(bool sustainEnabled, int sampleRate)
    {
        if (!Logical.Active || !Logical.SostenutoHeld) return;
        Logical.SostenutoHeld = false;
        if (!Logical.KeyHeld && !Logical.SustainHeld && !sustainEnabled)
            BeginRelease(sampleRate);
    }

    public void ResetControllerHolds(int sampleRate)
    {
        if (!Logical.Active || Logical.IsPercussion) return;
        Logical.SustainHeld = false;
        Logical.SostenutoHeld = false;
        if (!Logical.KeyHeld) BeginRelease(sampleRate);
    }

    public void BeginRelease(int sampleRate)
    {
        if (!Logical.Active)
            return;

        Logical.SustainHeld = false;
        Logical.SostenutoHeld = false;
        Render.BeginRelease(sampleRate);
    }

    /// <summary>
    /// RT-8: kernel melódico por bloques para voces que ya están en Sustain.
    /// Saca ADSR/anti-pop, dispatch de WaveType y varias comprobaciones invariantes
    /// fuera del loop por muestra. El LFO conserva su control-rate de 64 frames.
    /// Devuelve false si la voz necesita el camino general (attack/decay/release,
    /// portamento o percusión).
    /// </summary>
    public bool TryRenderSustainKnownWaveBlock(
        ChannelState channel,
        int sampleRate,
        ChiptuneWaveType knownWaveType,
        float[] destination,
        int frameCount)
    {
        if (!Logical.Active || Logical.IsPercussion || frameCount <= 0 ||
            MathF.Abs(CenteredController(channel.Brightness)) > 0.0001f ||
            MathF.Abs(CenteredController(channel.Resonance)) > 0.0001f ||
            Render.EnvelopeStage != VoiceEnvelopeStage.Sustain ||
            Render.AntiPopGain < 0.999999f ||
            Render.PortamentoSamplesRemaining > 0)
            return false;

        float phase = Render.Phase;
        float frequencyBase = Render.CurrentFrequency * channel.PitchBendRatio;
        float envelope = Render.SustainLevel;
        float vibratoRatio = Render.VibratoRatio;
        float vibratoStep = Render.VibratoRatioStep;
        int countdown = Render.LfoControlCountdown;
        int lfoAge = Render.LfoAgeSamples;
        float lfoPhase = Render.LfoPhase;
        uint noiseState = Render.NoiseState;
        int written = 0;

        const int LfoControlFrames = 64;
        float invSampleRate = 1f / sampleRate;
        int delaySamples = (int)(Render.LfoDelaySeconds * sampleRate);
        float fadeSamples = Math.Max(1f, Render.LfoFadeSeconds * sampleRate);
        float wheelDepth = channel.Modulation * Render.LfoWheelDepthSemitones;

        while (written < frameCount)
        {
            if (countdown <= 0)
            {
                float nextRatio = 1f;
                if (Render.LfoRateHz > 0f && lfoAge >= delaySamples)
                {
                    float fade = Math.Clamp((lfoAge - delaySamples) / fadeSamples, 0f, 1f);
                    float depth = fade * (Render.LfoBaseDepthSemitones + wheelDepth);
                    lfoPhase += Render.LfoRateHz * LfoControlFrames * invSampleRate;
                    if (lfoPhase >= 1f)
                        lfoPhase -= MathF.Floor(lfoPhase);
                    if (depth > 0.000001f)
                        nextRatio = FastAudioMath.Exp2(FastAudioMath.Sin01(lfoPhase) * depth / 12f);
                }

                vibratoStep = (nextRatio - vibratoRatio) / LfoControlFrames;
                countdown = LfoControlFrames;
            }

            int chunk = Math.Min(frameCount - written, countdown);
            int end = written + chunk;

            // El switch ocurre una vez por chunk/voz, no una vez por sample.
            switch (knownWaveType)
            {
                case ChiptuneWaveType.Pulse50:
                    for (; written < end; written++)
                    {
                        vibratoRatio += vibratoStep;
                        phase += frequencyBase * vibratoRatio * invSampleRate;
                        if (phase >= 1f) phase -= 1f;
                        destination[written] = (phase < 0.50f ? 1f : -1f) * envelope;
                    }
                    break;

                case ChiptuneWaveType.Pulse25:
                    for (; written < end; written++)
                    {
                        vibratoRatio += vibratoStep;
                        phase += frequencyBase * vibratoRatio * invSampleRate;
                        if (phase >= 1f) phase -= 1f;
                        destination[written] = (phase < 0.25f ? 1f : -1f) * envelope;
                    }
                    break;

                case ChiptuneWaveType.Sine:
                    for (; written < end; written++)
                    {
                        vibratoRatio += vibratoStep;
                        phase += frequencyBase * vibratoRatio * invSampleRate;
                        if (phase >= 1f) phase -= 1f;
                        destination[written] = FastAudioMath.Sin01(phase) * envelope;
                    }
                    break;

                case ChiptuneWaveType.Triangle:
                    for (; written < end; written++)
                    {
                        vibratoRatio += vibratoStep;
                        phase += frequencyBase * vibratoRatio * invSampleRate;
                        if (phase >= 1f) phase -= 1f;
                        destination[written] = (1f - 4f * MathF.Abs(phase - 0.5f)) * envelope;
                    }
                    break;

                case ChiptuneWaveType.Saw:
                    for (; written < end; written++)
                    {
                        vibratoRatio += vibratoStep;
                        phase += frequencyBase * vibratoRatio * invSampleRate;
                        if (phase >= 1f) phase -= 1f;
                        destination[written] = (2f * phase - 1f) * envelope;
                    }
                    break;

                case ChiptuneWaveType.BassHybrid:
                    for (; written < end; written++)
                    {
                        vibratoRatio += vibratoStep;
                        phase += frequencyBase * vibratoRatio * invSampleRate;
                        if (phase >= 1f) phase -= 1f;
                        float triangle = 1f - 4f * MathF.Abs(phase - 0.5f);
                        destination[written] =
                            (FastAudioMath.Sin01(phase) * 0.65f + triangle * 0.35f) * envelope;
                    }
                    break;

                case ChiptuneWaveType.Noise:
                    for (; written < end; written++)
                    {
                        vibratoRatio += vibratoStep;
                        phase += frequencyBase * vibratoRatio * invSampleRate;
                        if (phase >= 1f) phase -= 1f;
                        destination[written] = GenerateNoise(ref noiseState) * envelope;
                    }
                    break;

                default:
                    return false;
            }

            countdown -= chunk;
            lfoAge += chunk;
        }

        Render.Phase = phase;
        Render.VibratoRatio = vibratoRatio;
        Render.VibratoRatioStep = vibratoStep;
        Render.LfoControlCountdown = countdown;
        Render.LfoAgeSamples = lfoAge;
        Render.LfoPhase = lfoPhase;
        Render.NoiseState = noiseState;
        Render.EnvelopeLevel = envelope;
        return true;
    }

    /// <summary>
    /// RT-9: kernel melódico por bloques para Attack/Decay/Sustain/Release.
    /// Conserva la progresión lineal exacta del ADSR y anti-pop, pero mueve el
    /// dispatch de etapa/wavetype y los límites de etapa fuera del loop por muestra.
    /// Portamento sigue usando el camino general porque modifica frecuencia cada sample.
    /// </summary>
    public bool TryRenderEnvelopeKnownWaveBlock(
        ChannelState channel,
        int sampleRate,
        ChiptuneWaveType knownWaveType,
        float[] destination,
        int frameCount)
    {
        if (!Logical.Active || Logical.IsPercussion || frameCount <= 0 ||
            MathF.Abs(CenteredController(channel.Brightness)) > 0.0001f ||
            MathF.Abs(CenteredController(channel.Resonance)) > 0.0001f ||
            Render.PortamentoSamplesRemaining > 0 ||
            Render.EnvelopeStage == VoiceEnvelopeStage.Inactive)
            return false;

        float phase = Render.Phase;
        float frequencyBase = Render.CurrentFrequency * channel.PitchBendRatio;
        float vibratoRatio = Render.VibratoRatio;
        float vibratoStep = Render.VibratoRatioStep;
        int countdown = Render.LfoControlCountdown;
        int lfoAge = Render.LfoAgeSamples;
        float lfoPhase = Render.LfoPhase;
        uint noiseState = Render.NoiseState;
        float envelope = Render.EnvelopeLevel;
        float antiPop = Render.AntiPopGain;
        VoiceEnvelopeStage stage = Render.EnvelopeStage;
        int written = 0;

        const int LfoControlFrames = 64;
        float invSampleRate = 1f / sampleRate;
        int delaySamples = (int)(Render.LfoDelaySeconds * sampleRate);
        float fadeSamples = Math.Max(1f, Render.LfoFadeSeconds * sampleRate);
        float wheelDepth = channel.Modulation * Render.LfoWheelDepthSemitones;

        while (written < frameCount && stage != VoiceEnvelopeStage.Inactive)
        {
            if (countdown <= 0)
            {
                float nextRatio = 1f;
                if (Render.LfoRateHz > 0f && lfoAge >= delaySamples)
                {
                    float fade = Math.Clamp((lfoAge - delaySamples) / fadeSamples, 0f, 1f);
                    float depth = fade * (Render.LfoBaseDepthSemitones + wheelDepth);
                    lfoPhase += Render.LfoRateHz * LfoControlFrames * invSampleRate;
                    if (lfoPhase >= 1f)
                        lfoPhase -= MathF.Floor(lfoPhase);
                    if (depth > 0.000001f)
                        nextRatio = FastAudioMath.Exp2(FastAudioMath.Sin01(lfoPhase) * depth / 12f);
                }
                vibratoStep = (nextRatio - vibratoRatio) / LfoControlFrames;
                countdown = LfoControlFrames;
            }

            // Cuántas muestras podemos procesar sin cruzar una frontera ADSR.
            int envelopeFrames = frameCount - written;
            float envelopeStep;
            switch (stage)
            {
                case VoiceEnvelopeStage.Attack:
                    envelopeStep = Render.AttackIncrement;
                    envelopeFrames = envelopeStep > 0f
                        ? Math.Min(envelopeFrames, Math.Max(1, (int)MathF.Ceiling((1f - envelope) / envelopeStep)))
                        : 1;
                    break;
                case VoiceEnvelopeStage.Decay:
                    envelopeStep = -Render.DecayIncrement;
                    envelopeFrames = Render.DecayIncrement > 0f
                        ? Math.Min(envelopeFrames, Math.Max(1, (int)MathF.Ceiling((envelope - Render.SustainLevel) / Render.DecayIncrement)))
                        : 1;
                    break;
                case VoiceEnvelopeStage.Sustain:
                    envelope = Render.SustainLevel;
                    envelopeStep = 0f;
                    break;
                case VoiceEnvelopeStage.Release:
                    envelopeStep = -Render.ReleaseIncrement;
                    envelopeFrames = Render.ReleaseIncrement > 0f
                        ? Math.Min(envelopeFrames, Math.Max(1, (int)MathF.Ceiling(envelope / Render.ReleaseIncrement)))
                        : 1;
                    break;
                default:
                    stage = VoiceEnvelopeStage.Inactive;
                    continue;
            }

            int chunk = Math.Min(envelopeFrames, Math.Min(frameCount - written, countdown));
            int end = written + chunk;
            bool hitsEnvelopeBoundary = chunk == envelopeFrames && stage != VoiceEnvelopeStage.Sustain;
            // El último sample de Attack/Decay/Release necesita semántica exacta de
            // AdvanceEnvelope(): clamp ANTES de generar; Release que llega a cero no
            // genera ni avanza fase/LFO. Lo sacamos del loop caliente para no pagar
            // clamps/branches en cada muestra.
            int hotEnd = hitsEnvelopeBoundary ? end - 1 : end;

            // AdvanceEnvelope() actualiza primero ADSR y anti-pop y después genera.
            // Hacemos exactamente el mismo orden, pero sin el switch ADSR por sample.
            switch (knownWaveType)
            {
                case ChiptuneWaveType.Pulse50:
                    for (; written < hotEnd; written++)
                    {
                        envelope += envelopeStep;
                        if (antiPop < 1f) antiPop = Math.Min(1f, antiPop + Render.AntiPopIncrement);
                        vibratoRatio += vibratoStep;
                        phase += frequencyBase * vibratoRatio * invSampleRate;
                        if (phase >= 1f) phase -= 1f;
                        destination[written] = (phase < 0.50f ? 1f : -1f) * envelope * antiPop;
                    }
                    break;
                case ChiptuneWaveType.Pulse25:
                    for (; written < hotEnd; written++)
                    {
                        envelope += envelopeStep;
                        if (antiPop < 1f) antiPop = Math.Min(1f, antiPop + Render.AntiPopIncrement);
                        vibratoRatio += vibratoStep;
                        phase += frequencyBase * vibratoRatio * invSampleRate;
                        if (phase >= 1f) phase -= 1f;
                        destination[written] = (phase < 0.25f ? 1f : -1f) * envelope * antiPop;
                    }
                    break;
                case ChiptuneWaveType.Sine:
                    for (; written < hotEnd; written++)
                    {
                        envelope += envelopeStep;
                        if (antiPop < 1f) antiPop = Math.Min(1f, antiPop + Render.AntiPopIncrement);
                        vibratoRatio += vibratoStep;
                        phase += frequencyBase * vibratoRatio * invSampleRate;
                        if (phase >= 1f) phase -= 1f;
                        destination[written] = FastAudioMath.Sin01(phase) * envelope * antiPop;
                    }
                    break;
                case ChiptuneWaveType.Triangle:
                    for (; written < hotEnd; written++)
                    {
                        envelope += envelopeStep;
                        if (antiPop < 1f) antiPop = Math.Min(1f, antiPop + Render.AntiPopIncrement);
                        vibratoRatio += vibratoStep;
                        phase += frequencyBase * vibratoRatio * invSampleRate;
                        if (phase >= 1f) phase -= 1f;
                        destination[written] = (1f - 4f * MathF.Abs(phase - 0.5f)) * envelope * antiPop;
                    }
                    break;
                case ChiptuneWaveType.Saw:
                    for (; written < hotEnd; written++)
                    {
                        envelope += envelopeStep;
                        if (antiPop < 1f) antiPop = Math.Min(1f, antiPop + Render.AntiPopIncrement);
                        vibratoRatio += vibratoStep;
                        phase += frequencyBase * vibratoRatio * invSampleRate;
                        if (phase >= 1f) phase -= 1f;
                        destination[written] = (2f * phase - 1f) * envelope * antiPop;
                    }
                    break;
                case ChiptuneWaveType.BassHybrid:
                    for (; written < hotEnd; written++)
                    {
                        envelope += envelopeStep;
                        if (antiPop < 1f) antiPop = Math.Min(1f, antiPop + Render.AntiPopIncrement);
                        vibratoRatio += vibratoStep;
                        phase += frequencyBase * vibratoRatio * invSampleRate;
                        if (phase >= 1f) phase -= 1f;
                        float triangle = 1f - 4f * MathF.Abs(phase - 0.5f);
                        destination[written] = (FastAudioMath.Sin01(phase) * 0.65f + triangle * 0.35f) * envelope * antiPop;
                    }
                    break;
                case ChiptuneWaveType.Noise:
                    for (; written < hotEnd; written++)
                    {
                        envelope += envelopeStep;
                        if (antiPop < 1f) antiPop = Math.Min(1f, antiPop + Render.AntiPopIncrement);
                        vibratoRatio += vibratoStep;
                        phase += frequencyBase * vibratoRatio * invSampleRate;
                        if (phase >= 1f) phase -= 1f;
                        destination[written] = GenerateNoise(ref noiseState) * envelope * antiPop;
                    }
                    break;
                default:
                    return false;
            }

            int controlFramesAdvanced = chunk;
            if (hitsEnvelopeBoundary)
            {
                if (stage == VoiceEnvelopeStage.Release)
                {
                    // AdvanceEnvelope() devolvería false aquí: este frame es silencio y
                    // NO alcanza fase, LFO ni anti-pop. Evita el pequeño overshoot
                    // negativo que convertía ráfagas Square en un timbre "rasgado".
                    destination[written++] = 0f;
                    controlFramesAdvanced--;
                }
                else
                {
                    // Attack/Decay sí generan el sample de frontera, pero ya clamped.
                    envelope = stage == VoiceEnvelopeStage.Attack ? 1f : Render.SustainLevel;
                    if (antiPop < 1f) antiPop = Math.Min(1f, antiPop + Render.AntiPopIncrement);
                    vibratoRatio += vibratoStep;
                    phase += frequencyBase * vibratoRatio * invSampleRate;
                    if (phase >= 1f) phase -= 1f;
                    destination[written++] = GenerateKnownWave(knownWaveType, phase, ref noiseState) * envelope * antiPop;
                }
            }

            countdown -= controlFramesAdvanced;
            lfoAge += controlFramesAdvanced;

            // Resolver la frontera una sola vez por chunk, en lugar de por sample.
            if (chunk == envelopeFrames)
            {
                switch (stage)
                {
                    case VoiceEnvelopeStage.Attack:
                        envelope = 1f;
                        stage = VoiceEnvelopeStage.Decay;
                        break;
                    case VoiceEnvelopeStage.Decay:
                        envelope = Render.SustainLevel;
                        stage = Render.SustainLevel <= 0.000001f
                            ? VoiceEnvelopeStage.Inactive
                            : VoiceEnvelopeStage.Sustain;
                        break;
                    case VoiceEnvelopeStage.Release:
                        envelope = 0f;
                        stage = VoiceEnvelopeStage.Inactive;
                        break;
                }
            }
        }

        if (written < frameCount)
            Array.Clear(destination, written, frameCount - written);

        Render.Phase = phase;
        Render.VibratoRatio = vibratoRatio;
        Render.VibratoRatioStep = vibratoStep;
        Render.LfoControlCountdown = countdown;
        Render.LfoAgeSamples = lfoAge;
        Render.LfoPhase = lfoPhase;
        Render.NoiseState = noiseState;
        Render.EnvelopeLevel = envelope;
        Render.AntiPopGain = antiPop;
        Render.EnvelopeStage = stage;

        if (stage == VoiceEnvelopeStage.Inactive)
        {
            // La mezcla de este bloque ya está en destination; liberar la voz para el siguiente.
            Logical.Active = false;
        }
        return true;
    }

    /// <summary>
    /// RT-6: igual que NextSample, pero el wavetype ya viene conocido por el bucket.
    /// Evita resolver/virtualizar IWaveGenerator en el hot path melódico.
    /// </summary>
    public float NextSampleKnownWave(ChannelState channel, int sampleRate, ChiptuneWaveType knownWaveType)
    {
        if (!Logical.Active || !Render.AdvanceEnvelope())
        {
            Reset();
            return 0f;
        }

        if (Logical.IsPercussion)
            return DrumKitLayering.Generate(ref Render, Logical.Note, sampleRate)
                   * Render.EnvelopeLevel * Render.AntiPopGain;

        const int LfoControlFrames = 64;
        if (Render.LfoControlCountdown <= 0)
        {
            float nextRatio = 1f;
            int delaySamples = (int)(Render.LfoDelaySeconds * sampleRate);
            if (Render.LfoRateHz > 0f && Render.LfoAgeSamples >= delaySamples)
            {
                float fade = Math.Clamp(
                    (Render.LfoAgeSamples - delaySamples) / Math.Max(1f, Render.LfoFadeSeconds * sampleRate),
                    0f,
                    1f);
                float depth = fade *
                    (Render.LfoBaseDepthSemitones + channel.Modulation * Render.LfoWheelDepthSemitones);
                Render.LfoPhase += Render.LfoRateHz * LfoControlFrames / sampleRate;
                Render.LfoPhase -= MathF.Floor(Render.LfoPhase);
                if (depth > 0.000001f)
                    nextRatio = FastAudioMath.Exp2(FastAudioMath.Sin01(Render.LfoPhase) * depth / 12f);
            }

            Render.VibratoRatioStep = (nextRatio - Render.VibratoRatio) / LfoControlFrames;
            Render.LfoControlCountdown = LfoControlFrames;
        }

        Render.VibratoRatio += Render.VibratoRatioStep;
        Render.LfoControlCountdown--;
        Render.LfoAgeSamples++;

        if (Render.PortamentoSamplesRemaining > 0)
        {
            Render.CurrentFrequency *= Render.PortamentoMultiplier;
            Render.PortamentoSamplesRemaining--;
            if (Render.PortamentoSamplesRemaining == 0)
            {
                Render.CurrentFrequency = Render.TargetFrequency;
                Render.CurrentNote = Render.TargetNote;
            }
        }

        float frequency = Render.CurrentFrequency * channel.PitchBendRatio * Render.VibratoRatio;
        Render.Phase += frequency / sampleRate;
        Render.Phase -= MathF.Floor(Render.Phase);

        float waveform = GenerateKnownWave(knownWaveType, Render.Phase, ref Render.NoiseState);
        waveform = ApplyChipToneControllers(waveform, channel);
        return waveform * Render.EnvelopeLevel * Render.AntiPopGain;
    }

    private static float GenerateKnownWave(ChiptuneWaveType waveType, float phase, ref uint noiseState)
    {
        return waveType switch
        {
            ChiptuneWaveType.Pulse50 => phase < 0.50f ? 1f : -1f,
            ChiptuneWaveType.Pulse25 => phase < 0.25f ? 1f : -1f,
            ChiptuneWaveType.Sine => FastAudioMath.Sin01(phase),
            ChiptuneWaveType.Triangle => 1f - 4f * MathF.Abs(phase - 0.5f),
            ChiptuneWaveType.Saw => 2f * phase - 1f,
            ChiptuneWaveType.BassHybrid =>
                FastAudioMath.Sin01(phase) * 0.65f +
                (1f - 4f * MathF.Abs(phase - 0.5f)) * 0.35f,
            ChiptuneWaveType.Noise => GenerateNoise(ref noiseState),
            _ => phase < 0.50f ? 1f : -1f,
        };
    }

    private static float GenerateNoise(ref uint noiseState)
    {
        uint state = noiseState;
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        noiseState = state;
        return state / (float)uint.MaxValue * 2f - 1f;
    }

    public float NextSample(ChannelState channel, int sampleRate)
    {
        if (!Logical.Active || !Render.AdvanceEnvelope())
        {
            Reset();
            return 0f;
        }

        float waveform;
        if (Logical.IsPercussion)
        {
            waveform = DrumKitLayering.Generate(ref Render, Logical.Note, sampleRate);
        }
        else
        {
            // El LFO se evalúa a frecuencia de control (cada 16 muestras), no con
            // Sin/Pow por voz y por muestra. Entre puntos se interpola el ratio.
            const int LfoControlFrames = 64;
            if (Render.LfoControlCountdown <= 0)
            {
                float nextRatio = 1f;
                int delaySamples = (int)(Render.LfoDelaySeconds * sampleRate);
                if (Render.LfoRateHz > 0f && Render.LfoAgeSamples >= delaySamples)
                {
                    float fade = Math.Clamp(
                        (Render.LfoAgeSamples - delaySamples) / Math.Max(1f, Render.LfoFadeSeconds * sampleRate),
                        0f,
                        1f);
                    float depth = fade * (Render.LfoBaseDepthSemitones + channel.Modulation * Render.LfoWheelDepthSemitones);
                    Render.LfoPhase += Render.LfoRateHz * LfoControlFrames / sampleRate;
                    Render.LfoPhase -= MathF.Floor(Render.LfoPhase);
                    if (depth > 0.000001f)
                        nextRatio = FastAudioMath.Exp2(FastAudioMath.Sin01(Render.LfoPhase) * depth / 12f);
                }

                Render.VibratoRatioStep = (nextRatio - Render.VibratoRatio) / LfoControlFrames;
                Render.LfoControlCountdown = LfoControlFrames;
            }
            Render.VibratoRatio += Render.VibratoRatioStep;
            Render.LfoControlCountdown--;
            Render.LfoAgeSamples++;

            // Portamento exponencial precalculado: evita convertir nota MIDI a Hz
            // mediante Pow en cada muestra.
            if (Render.PortamentoSamplesRemaining > 0)
            {
                Render.CurrentFrequency *= Render.PortamentoMultiplier;
                Render.PortamentoSamplesRemaining--;
                if (Render.PortamentoSamplesRemaining == 0)
                {
                    Render.CurrentFrequency = Render.TargetFrequency;
                    Render.CurrentNote = Render.TargetNote;
                }
            }
            float frequency = Render.CurrentFrequency * channel.PitchBendRatio * Render.VibratoRatio;
            Render.Phase += frequency / sampleRate;
            Render.Phase -= MathF.Floor(Render.Phase);

            IWaveGenerator? generator = Render.Generator;
            if (generator is null)
            {
                generator = WaveGeneratorRegistry.Resolve(Logical.WaveType);
                Render.Generator = generator;
            }

            waveform = generator.Generate(Render.Phase, ref Render.NoiseState);
            waveform = ApplyChipToneControllers(waveform, channel);
        }
        return waveform * Render.EnvelopeLevel * Render.AntiPopGain;
    }

    /// <summary>
    /// Avanza una voz melódica sin calcular ni mezclar la forma de onda.
    /// Se usa cuando su contribución es inaudible. Mantiene envelope, fase,
    /// LFO y portamento sincronizados para que vuelva correctamente si deja
    /// de estar muteada o su ganancia cambia.
    /// </summary>
    /// <summary>
    /// RT-5 fast path para una voz melódica inaudible. Hace avanzar el estado
    /// por bloques completos en vez de iterar muestra por muestra.
    /// </summary>
    public int AdvanceStateOnlyFrames(ChannelState channel, int sampleRate, int frames)
    {
        if (!Logical.Active || frames <= 0)
            return 0;
        if (Logical.IsPercussion)
            return 0;

        int requested = frames;
        float startFrequency = Math.Max(Render.CurrentFrequency, 0f);

        if (!Render.AdvanceEnvelopeFrames(frames))
        {
            Reset();
            return requested;
        }

        // Portamento: una sola potencia por bloque silencioso en lugar de una
        // multiplicación por muestra.
        int glideFrames = Math.Min(frames, Math.Max(0, Render.PortamentoSamplesRemaining));
        if (glideFrames > 0)
        {
            if (glideFrames >= Render.PortamentoSamplesRemaining)
            {
                Render.CurrentFrequency = Render.TargetFrequency;
                Render.CurrentNote = Render.TargetNote;
                Render.PortamentoSamplesRemaining = 0;
            }
            else
            {
                Render.CurrentFrequency *= FastAudioMath.Pow(Render.PortamentoMultiplier, glideFrames);
                Render.PortamentoSamplesRemaining -= glideFrames;
            }
        }

        // LFO a control-rate. Para una voz inaudible nos importa preservar el
        // tiempo/fase al reaparecer, no generar cada punto intermedio.
        int delaySamples = (int)(Render.LfoDelaySeconds * sampleRate);
        int oldAge = Render.LfoAgeSamples;
        int newAge = oldAge + frames;
        if (Render.LfoRateHz > 0f && newAge > delaySamples)
        {
            int activeStart = Math.Max(oldAge, delaySamples);
            int activeFrames = Math.Max(0, newAge - activeStart);
            Render.LfoPhase += Render.LfoRateHz * activeFrames / sampleRate;
            Render.LfoPhase -= MathF.Floor(Render.LfoPhase);

            float fade = Math.Clamp(
                (newAge - delaySamples) /
                Math.Max(1f, Render.LfoFadeSeconds * sampleRate),
                0f,
                1f);
            float depth = fade *
                (Render.LfoBaseDepthSemitones +
                 channel.Modulation * Render.LfoWheelDepthSemitones);
            Render.VibratoRatio = depth > 0.000001f
                ? FastAudioMath.Exp2(FastAudioMath.Sin01(Render.LfoPhase) * depth / 12f)
                : 1f;
            Render.VibratoRatioStep = 0f;
        }
        else
        {
            Render.VibratoRatio = 1f;
            Render.VibratoRatioStep = 0f;
        }

        Render.LfoAgeSamples = newAge;
        Render.LfoControlCountdown = 0;

        // La fase del oscilador se avanza con la frecuencia media del bloque.
        // En una voz inaudible esto conserva continuidad perceptual al reaparecer
        // sin pagar un phase update por muestra.
        float endFrequency = Math.Max(Render.CurrentFrequency, 0f);
        float averageFrequency = (startFrequency + endFrequency) * 0.5f;
        float phaseAdvance =
            averageFrequency * channel.PitchBendRatio * Render.VibratoRatio *
            frames / sampleRate;
        Render.Phase += phaseAdvance;
        Render.Phase -= MathF.Floor(Render.Phase);

        return requested;
    }

    public bool AdvanceStateOnly(ChannelState channel, int sampleRate)
    {
        if (!Logical.Active || !Render.AdvanceEnvelope())
        {
            Reset();
            return false;
        }

        if (Logical.IsPercussion)
        {
            // Para percusión no hacemos fast-forward aproximado porque filtros,
            // ruido y capas dependen del historial muestra a muestra.
            // El caller evita usar RT-4 para drums salvo que ya no estén activos.
            return true;
        }

        const int LfoControlFrames = 64;
        if (Render.LfoControlCountdown <= 0)
        {
            float nextRatio = 1f;
            int delaySamples = (int)(Render.LfoDelaySeconds * sampleRate);
            if (Render.LfoRateHz > 0f && Render.LfoAgeSamples >= delaySamples)
            {
                float fade = Math.Clamp(
                    (Render.LfoAgeSamples - delaySamples) / Math.Max(1f, Render.LfoFadeSeconds * sampleRate),
                    0f,
                    1f);
                float depth = fade * (Render.LfoBaseDepthSemitones + channel.Modulation * Render.LfoWheelDepthSemitones);
                Render.LfoPhase += Render.LfoRateHz * LfoControlFrames / sampleRate;
                Render.LfoPhase -= MathF.Floor(Render.LfoPhase);
                if (depth > 0.000001f)
                    nextRatio = FastAudioMath.Exp2(FastAudioMath.Sin01(Render.LfoPhase) * depth / 12f);
            }

            Render.VibratoRatioStep = (nextRatio - Render.VibratoRatio) / LfoControlFrames;
            Render.LfoControlCountdown = LfoControlFrames;
        }

        Render.VibratoRatio += Render.VibratoRatioStep;
        Render.LfoControlCountdown--;
        Render.LfoAgeSamples++;

        if (Render.PortamentoSamplesRemaining > 0)
        {
            Render.CurrentFrequency *= Render.PortamentoMultiplier;
            Render.PortamentoSamplesRemaining--;
            if (Render.PortamentoSamplesRemaining == 0)
            {
                Render.CurrentFrequency = Render.TargetFrequency;
                Render.CurrentNote = Render.TargetNote;
            }
        }

        float frequency = Render.CurrentFrequency * channel.PitchBendRatio * Render.VibratoRatio;
        Render.Phase += frequency / sampleRate;
        Render.Phase -= MathF.Floor(Render.Phase);
        return true;
    }

    private static float CenteredController(float value)
        => (Math.Clamp(value, 0f, 1f) - (64f / 127f)) * (127f / 63f);

    private static float ControllerTimeScale(float value)
        => FastAudioMath.Exp2(CenteredController(value) * 2f);

    public void ApplySoundControllers(ChannelState channel, int sampleRate)
    {
        if (!Logical.Active || Logical.IsPercussion)
            return;

        EnvelopeProfile baseEnvelope = EnvelopeProfile.ForProgram(0, Logical.WaveType);
        // Preserve the voice's program-shaped values as the neutral reference by
        // scaling its existing timings rather than replacing its family profile.
        float attackScale = ControllerTimeScale(channel.AttackTime);
        float decayScale = ControllerTimeScale(channel.DecayTime);
        float releaseScale = ControllerTimeScale(channel.ReleaseTime);

        if (Render.EnvelopeStage == VoiceEnvelopeStage.Attack)
        {
            float remaining = Math.Max(0f, 1f - Render.EnvelopeLevel);
            int samples = Math.Max(1, (int)MathF.Round(0.020f * attackScale * sampleRate));
            Render.AttackIncrement = remaining / samples;
        }
        if (Render.EnvelopeStage == VoiceEnvelopeStage.Decay)
        {
            float remaining = Math.Max(0f, Render.EnvelopeLevel - Render.SustainLevel);
            int samples = Math.Max(1, (int)MathF.Round(0.120f * decayScale * sampleRate));
            Render.DecayIncrement = remaining / samples;
        }
        Render.ReleaseSeconds = Math.Clamp(Render.ReleaseSeconds * releaseScale, 0.0001f, 30f);

        float rateCentered = CenteredController(channel.VibratoRate);
        Render.LfoRateHz = Math.Max(0f, Render.LfoRateHz * FastAudioMath.Exp2(rateCentered));
        Render.LfoBaseDepthSemitones = Math.Max(
            0f, Render.LfoBaseDepthSemitones + CenteredController(channel.VibratoDepth));
        float delayCentered = Math.Max(0f, CenteredController(channel.VibratoDelay));
        Render.LfoDelaySeconds = Math.Clamp(Render.LfoDelaySeconds + delayCentered * 2f, 0f, 4f);
        Render.LfoControlCountdown = 0;
    }

    private float ApplyChipToneControllers(float sample, ChannelState channel)
    {
        float b = CenteredController(channel.Brightness);
        float r = CenteredController(channel.Resonance);
        if (MathF.Abs(b) < 0.0001f && MathF.Abs(r) < 0.0001f)
            return sample;

        // Lightweight two-state chip-colour filter. This is deliberately not
        // DSN's VCF: CC74 controls harmonic brightness while CC71 adds a modest
        // resonant edge. Neutral controllers are bit-for-bit bypassed.
        float cutoff = Math.Clamp(0.32f + b * 0.24f, 0.06f, 0.62f);
        float resonance = Math.Clamp(r * 0.55f, -0.35f, 0.55f);
        float high = sample - Render.ToneLowState - resonance * Render.ToneBandState;
        Render.ToneBandState += cutoff * high;
        Render.ToneLowState += cutoff * Render.ToneBandState;
        return Math.Clamp(Render.ToneLowState + Math.Max(0f, r) * Render.ToneBandState * 0.35f, -1.5f, 1.5f);
    }

    public void BeginPortamentoFrom(int sourceNote, float portamentoSeconds, int sampleRate)
    {
        if (!Logical.Active || Logical.IsPercussion) return;
        int source = Math.Clamp(sourceNote, 0, 127);
        float sourceHz = 440f * FastAudioMath.Exp2((source - 69f) / 12f);
        Render.CurrentNote = source;
        Render.CurrentFrequency = sourceHz;
        Render.TargetNote = Logical.Note;
        Render.TargetFrequency = 440f * FastAudioMath.Exp2((Logical.Note - 69f) / 12f);
        int samples = Math.Max(1, (int)MathF.Round(Math.Max(0.0001f, portamentoSeconds) * sampleRate));
        Render.PortamentoSamplesRemaining = samples;
        Render.PortamentoStep = (Render.TargetNote - Render.CurrentNote) / samples;
        Render.PortamentoMultiplier = FastAudioMath.Pow(Render.TargetFrequency / Math.Max(sourceHz, 0.0001f), 1f / samples);
    }

    public void RetargetLegato(int note, float velocity, float portamentoSeconds, int sampleRate)
    {
        if (!Logical.Active || Logical.IsPercussion)
            return;

        int clampedNote = Math.Clamp(note, 0, 127);
        Logical.Note = clampedNote;
        Logical.Velocity = Math.Clamp(velocity, 0f, 1f);
        Logical.KeyHeld = true;
        Logical.SustainHeld = false;
        Render.TargetNote = clampedNote;
        Render.TargetFrequency = 440f * FastAudioMath.Exp2((clampedNote - 69f) / 12f);
        int glideSamples = Math.Max(1, (int)MathF.Round(Math.Max(0f, portamentoSeconds) * sampleRate));
        Render.PortamentoSamplesRemaining = glideSamples;
        Render.PortamentoStep = (Render.TargetNote - Render.CurrentNote) / glideSamples;
        float safeCurrent = Math.Max(Render.CurrentFrequency, 0.0001f);
        Render.PortamentoMultiplier = FastAudioMath.Pow(Render.TargetFrequency / safeCurrent, 1f / glideSamples);
    }

    public void Reset()
    {
        Logical.Reset();
        Render.Reset();
    }
}

public sealed class VoicePool
{
    // Headroom global de Lyra. 0.18 era demasiado bajo; 1.0 satura mezclas densas.
    // 0.35 conserva ~+5.8 dB respecto al Clean Room original sin convertir
    // casi cualquier acorde/pasaje polifónico en clipping permanente.
    private const float LyraMasterVoiceGain = 0.35f;
    private readonly VoiceState[] _voices;
    private long _startSequence;
    private int _sampleRate = 44_100;
    private readonly float[] _voiceScratch = new float[1024];

    // RT-6: buckets preasignados por wavetype. No hay List<T>, LINQ ni
    // allocations en RenderSegment. El bucket 6 se reserva para percusión.
    private const int WaveBucketCount = 7;
    private const int DrumBucket = 6;
    private readonly int[] _bucketCounts = new int[WaveBucketCount];
    private readonly int[] _bucketVoiceIndices;
    private long _bucketBuildCount;
    private long _bucketedVoiceCount;

    // RT-7A: métricas del kernel ARM NEON explícito.
    private long _neonMixCalls;
    private long _neonMixedFrames;
    private long _numericsMixCalls;
    private long _numericsMixedFrames;

    // RT-3: métricas de presión de polifonía. El pool ya es el límite global
    // (96 voces en AndroidChiptuneEngine); ahora el reemplazo es consciente
    // del valor audible de cada voz.
    private long _stolenVoiceCount;
    private long _duplicateStealCount;
    private long _releaseStealCount;

    // RT-4: cuánto trabajo de síntesis evitamos sin congelar el estado de voz.
    private long _silenceSkippedVoiceSegments;
    private long _silenceSkippedVoiceFrames;

    // RT-5: control-rate/state fast-forward.
    private long _controlFastForwardCalls;
    private long _controlFastForwardFrames;

    public static bool IsSimdAccelerated => Vector.IsHardwareAccelerated;
    public static bool IsNeonAccelerated => AdvSimd.IsSupported;

    public VoicePool(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _voices = new VoiceState[capacity];
        _bucketVoiceIndices = new int[capacity * WaveBucketCount];
        Reset();
    }

    public int Capacity => _voices.Length;
    public long StolenVoiceCount => _stolenVoiceCount;
    public long DuplicateStealCount => _duplicateStealCount;
    public long ReleaseStealCount => _releaseStealCount;
    public long SilenceSkippedVoiceSegments => _silenceSkippedVoiceSegments;
    public long SilenceSkippedVoiceFrames => _silenceSkippedVoiceFrames;
    public long ControlFastForwardCalls => _controlFastForwardCalls;
    public long ControlFastForwardFrames => _controlFastForwardFrames;
    public long BucketBuildCount => _bucketBuildCount;
    public long BucketedVoiceCount => _bucketedVoiceCount;
    public long NeonMixCalls => _neonMixCalls;
    public long NeonMixedFrames => _neonMixedFrames;
    public long NumericsMixCalls => _numericsMixCalls;
    public long NumericsMixedFrames => _numericsMixedFrames;

    public void ConfigureSampleRate(int sampleRate)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        _sampleRate = sampleRate;
    }

    public void Reset()
    {
        for (int index = 0; index < _voices.Length; index++)
            _voices[index].Reset();
        _startSequence = 0;
        _stolenVoiceCount = 0;
        _duplicateStealCount = 0;
        _releaseStealCount = 0;
        _silenceSkippedVoiceSegments = 0;
        _silenceSkippedVoiceFrames = 0;
        _controlFastForwardCalls = 0;
        _controlFastForwardFrames = 0;
        _bucketBuildCount = 0;
        _bucketedVoiceCount = 0;
        _neonMixCalls = 0;
        _neonMixedFrames = 0;
        _numericsMixCalls = 0;
        _numericsMixedFrames = 0;
        Array.Clear(_bucketCounts);
    }

    public void StartVoice(int channelIndex, int note, float velocity, ChiptuneWaveType waveType, bool isPercussion, int program, bool portamentoEnabled, bool legatoEnabled, float portamentoSeconds, int? portamentoSourceNote = null, ChannelState? controllerState = null)
    {
        if (!isPercussion && (portamentoEnabled || legatoEnabled))
        {
            int legatoVoiceIndex = FindNewestHeldVoice(channelIndex);
            if (legatoVoiceIndex >= 0)
            {
                ref VoiceState legatoVoice = ref _voices[legatoVoiceIndex];
                legatoVoice.RetargetLegato(note, velocity, portamentoEnabled ? portamentoSeconds : 0.005f, _sampleRate);
                return;
            }
        }

        int voiceIndex = FindVoiceForStart();
        _startSequence++;
        _voices[voiceIndex].Start(channelIndex, note, velocity, waveType, isPercussion, program, _startSequence, _sampleRate);
        if (!isPercussion && controllerState is not null)
            _voices[voiceIndex].ApplySoundControllers(controllerState, _sampleRate);
        if (!isPercussion && portamentoSourceNote.HasValue)
            _voices[voiceIndex].BeginPortamentoFrom(portamentoSourceNote.Value, portamentoSeconds, _sampleRate);
    }

    public void SetChannelWaveType(int channelIndex, ChiptuneWaveType waveType)
    {
        for (int index = 0; index < _voices.Length; index++)
        {
            ref VoiceState voice = ref _voices[index];
            if (!voice.Active || voice.ChannelIndex != channelIndex || voice.Logical.IsPercussion)
                continue;

            voice.Logical.WaveType = waveType;
            voice.Render.Generator = WaveGeneratorRegistry.Resolve(waveType);
        }
    }

    public void ReleaseNote(int channelIndex, int note, bool sustain)
    {
        // MIDI permite NoteOn solapados de la misma nota/canal. Un NoteOff debe
        // consumir UNA instancia todavía sostenida, no mandar simultáneamente
        // a Release todas las voces duplicadas. Emparejamos FIFO: la voz KeyHeld
        // más antigua recibe primero el NoteOff. StartSequence nos da una identidad
        // estable sin asignaciones ni estructuras auxiliares en el hot path.
        int selectedIndex = -1;
        long oldestSequence = long.MaxValue;

        for (int index = 0; index < _voices.Length; index++)
        {
            ref VoiceState voice = ref _voices[index];
            if (!voice.Active ||
                voice.ChannelIndex != channelIndex ||
                voice.Note != note ||
                !voice.Logical.KeyHeld)
            {
                continue;
            }

            if (voice.StartSequence < oldestSequence)
            {
                oldestSequence = voice.StartSequence;
                selectedIndex = index;
            }
        }

        if (selectedIndex >= 0)
            _voices[selectedIndex].HandleNoteOff(sustain, _sampleRate);
    }

    public void ReleasePending(int channelIndex)
    {
        for (int index = 0; index < _voices.Length; index++)
        {
            ref VoiceState voice = ref _voices[index];
            if (voice.Active && voice.ChannelIndex == channelIndex)
                voice.ReleaseFromSustain(_sampleRate);
        }
    }

    public void ApplySoundControllers(int channelIndex, ChannelState channel)
    {
        for (int i = 0; i < _voices.Length; i++)
        {
            ref VoiceState voice = ref _voices[i];
            if (voice.Active && voice.ChannelIndex == channelIndex && !voice.Logical.IsPercussion)
                voice.ApplySoundControllers(channel, _sampleRate);
        }
    }

    public void SetSostenuto(int channelIndex, bool enabled, bool sustainEnabled)
    {
        for (int i = 0; i < _voices.Length; i++)
        {
            ref VoiceState voice = ref _voices[i];
            if (!voice.Active || voice.ChannelIndex != channelIndex) continue;
            if (enabled) voice.CaptureSostenuto();
            else voice.ReleaseFromSostenuto(sustainEnabled, _sampleRate);
        }
    }

    public void ResetControllerHolds(int channelIndex)
    {
        for (int i = 0; i < _voices.Length; i++)
        {
            ref VoiceState voice = ref _voices[i];
            if (voice.Active && voice.ChannelIndex == channelIndex)
                voice.ResetControllerHolds(_sampleRate);
        }
    }

    public void ReleaseAll(int channelIndex)
    {
        for (int index = 0; index < _voices.Length; index++)
        {
            ref VoiceState voice = ref _voices[index];
            if (voice.Active && voice.ChannelIndex == channelIndex)
                voice.BeginRelease(_sampleRate);
        }
    }

    public void AllSoundOff(int channelIndex)
    {
        for (int index = 0; index < _voices.Length; index++)
        {
            ref VoiceState voice = ref _voices[index];
            if (voice.Active && voice.ChannelIndex == channelIndex)
                voice.Reset();
        }
    }

    public int CaptureSnapshot(
        EngineVoiceSnapshot[] destination,
        int[] activeVoiceCounts,
        int[] heldNoteCounts,
        float[] peakEnvelopeLevels)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(activeVoiceCounts);
        ArgumentNullException.ThrowIfNull(heldNoteCounts);
        ArgumentNullException.ThrowIfNull(peakEnvelopeLevels);

        Array.Clear(activeVoiceCounts);
        Array.Clear(heldNoteCounts);
        Array.Clear(peakEnvelopeLevels);

        int written = 0;
        for (int index = 0; index < _voices.Length; index++)
        {
            ref VoiceState voice = ref _voices[index];
            if (!voice.Active)
                continue;

            int channelIndex = voice.ChannelIndex;
            if ((uint)channelIndex < (uint)activeVoiceCounts.Length)
            {
                activeVoiceCounts[channelIndex]++;
                if (voice.Logical.KeyHeld)
                    heldNoteCounts[channelIndex]++;
                if (voice.Level > peakEnvelopeLevels[channelIndex])
                    peakEnvelopeLevels[channelIndex] = voice.Level;
            }

            if (written >= destination.Length)
                continue;

            destination[written] = new EngineVoiceSnapshot(
                channelIndex,
                voice.Note,
                voice.Velocity,
                voice.Logical.KeyHeld,
                voice.Logical.SustainHeld,
                voice.EnvelopeStage,
                voice.Level);
            written++;
        }

        return written;
    }

    public VoiceState[] CaptureState(out long startSequence)
    {
        var copy = new VoiceState[_voices.Length];
        Array.Copy(_voices, copy, _voices.Length);
        startSequence = _startSequence;
        return copy;
    }

    public void RestoreState(VoiceState[] voices, long startSequence)
    {
        ArgumentNullException.ThrowIfNull(voices);
        if (voices.Length != _voices.Length)
            throw new ArgumentException("La capacidad del checkpoint no coincide con el VoicePool.", nameof(voices));

        Array.Copy(voices, _voices, _voices.Length);
        _startSequence = startSequence;
    }

    /// <summary>
    /// Avanza el estado de todas las voces sin sintetizar ni mezclar PCM.
    /// Las voces melódicas usan el fast-path por bloques; la percusión conserva
    /// su estado exacto recorriendo únicamente sus voces activas. Este camino
    /// está pensado para seek y evita pagar el coste del mixer, DSP y capturas.
    /// </summary>
    public void AdvanceStateOnlyFrames(ChannelState[] channels, int sampleRate, int frames)
    {
        ArgumentNullException.ThrowIfNull(channels);
        if (frames <= 0) return;

        for (int index = 0; index < _voices.Length; index++)
        {
            ref VoiceState voice = ref _voices[index];
            if (!voice.Active)
                continue;

            int channelIndex = voice.ChannelIndex;
            if ((uint)channelIndex >= (uint)channels.Length)
            {
                voice.Reset();
                continue;
            }

            ChannelState channel = channels[channelIndex];
            if (!voice.Logical.IsPercussion)
            {
                voice.AdvanceStateOnlyFrames(channel, sampleRate, frames);
                continue;
            }

            // La capa de drums tiene filtros/ruido dependientes de muestra.
            // Sólo las voces de percusión pagan este bucle; seguimos evitando
            // mezcla estéreo, SIMD, EQ, Bass Restoration y buffers de captura.
            for (int frame = 0; frame < frames && voice.Active; frame++)
                _ = voice.NextSample(channel, sampleRate);
        }
    }

    public bool HasActiveVoices()
    {
        for (int index = 0; index < _voices.Length; index++)
        {
            if (_voices[index].Active)
                return true;
        }
        return false;
    }


    public int CountActiveVoices()
    {
        int count = 0;
        for (int index = 0; index < _voices.Length; index++)
        {
            if (_voices[index].Active)
                count++;
        }
        return count;
    }

    public void RenderSegment(
        ChannelState[] channels,
        int sampleRate,
        float[] leftMix,
        float[] rightMix,
        int startFrame,
        int frameCount,
        float[][]? channelCaptureBuffers,
        bool[]? captureChannels,
        bool anySolo)
    {
        if (frameCount <= 0)
            return;
        if (frameCount > _voiceScratch.Length)
            throw new ArgumentOutOfRangeException(nameof(frameCount));

        BuildWaveBuckets();

        int vectorWidth = Vector<float>.Count;
        bool useSimd = Vector.IsHardwareAccelerated && frameCount >= vectorWidth;

        // Se recorren tipos homogéneos en orden fijo. Esto mejora locality de código
        // y elimina el dispatch virtual del generador para voces melódicas.
        for (int bucket = 0; bucket < WaveBucketCount; bucket++)
        {
            int count = _bucketCounts[bucket];
            int bucketBase = bucket * _voices.Length;
            ChiptuneWaveType waveType = BucketToWaveType(bucket);

            for (int bucketItem = 0; bucketItem < count; bucketItem++)
            {
                int voiceIndex = _bucketVoiceIndices[bucketBase + bucketItem];
                ref VoiceState voice = ref _voices[voiceIndex];
                if (!voice.Active)
                    continue;

                int channelIndex = voice.ChannelIndex;
                if ((uint)channelIndex >= (uint)channels.Length)
                {
                    voice.Reset();
                    continue;
                }

                ChannelState channel = channels[channelIndex];
                bool audible = !channel.Muted && (!anySolo || channel.Solo);
                float gain = audible
                    ? voice.Velocity * channel.Volume * channel.Expression * channel.UserGain * (channel.SoftPedal ? 0.72f : 1f) * LyraMasterVoiceGain
                    : 0f;

                const float SilenceThreshold = 0.00001f;
                const float ReleaseEnvelopeSilenceThreshold = 0.0000631f; // ~ -84 dB interno
                float internalReleaseLevel =
                    Math.Clamp(voice.Level, 0f, 1f) *
                    Math.Clamp(voice.Render.AntiPopGain, 0f, 1f);
                bool canSkipSynthesis =
                    !voice.Logical.IsPercussion &&
                    (!audible ||
                     gain <= SilenceThreshold ||
                     (voice.EnvelopeStage == VoiceEnvelopeStage.Release &&
                      internalReleaseLevel <= ReleaseEnvelopeSilenceThreshold));

                if (canSkipSynthesis)
                {
                    int advanced = voice.AdvanceStateOnlyFrames(channel, sampleRate, frameCount);
                    _silenceSkippedVoiceSegments++;
                    _silenceSkippedVoiceFrames += advanced;
                    _controlFastForwardCalls++;
                    _controlFastForwardFrames += advanced;
                    continue;
                }

                int generated = 0;
                if (bucket == DrumBucket)
                {
                    for (; generated < frameCount; generated++)
                        _voiceScratch[generated] = voice.NextSample(channel, sampleRate);
                }
                else if (voice.TryRenderSustainKnownWaveBlock(
                             channel, sampleRate, waveType, _voiceScratch, frameCount) ||
                         voice.TryRenderEnvelopeKnownWaveBlock(
                             channel, sampleRate, waveType, _voiceScratch, frameCount))
                {
                    generated = frameCount;
                }
                else
                {
                    for (; generated < frameCount; generated++)
                        _voiceScratch[generated] =
                            voice.NextSampleKnownWave(channel, sampleRate, waveType);
                }

                float leftGain = gain * channel.PanGainL;
                float rightGain = gain * channel.PanGainR;
                bool captureChannel = channelCaptureBuffers is not null &&
                                      captureChannels is not null &&
                                      (uint)channelIndex < (uint)captureChannels.Length &&
                                      captureChannels[channelIndex];

                int frame = 0;

                // RT-7A: en Android ARM64 System.Numerics puede informar que no
                // está acelerado aunque el CPU tenga NEON. Probamos AdvSimd de
                // forma explícita primero. Este kernel sólo hace load/mul/add/store
                // sobre bloques contiguos y no toca el estado de la voz.
                if (AdvSimd.IsSupported && frameCount >= 4)
                {
                    float[]? capture = captureChannel
                        ? channelCaptureBuffers![channelIndex]
                        : null;
                    frame = MixVoiceNeon(
                        leftMix,
                        rightMix,
                        capture,
                        startFrame,
                        frameCount,
                        leftGain,
                        rightGain,
                        gain);
                    _neonMixCalls++;
                    _neonMixedFrames += frame;
                }
                else if (useSimd)
                {
                    Vector<float> leftGainVector = new(leftGain);
                    Vector<float> rightGainVector = new(rightGain);
                    Vector<float> monoGainVector = new(gain);
                    int vectorEnd = frameCount - (frameCount % vectorWidth);
                    for (; frame < vectorEnd; frame += vectorWidth)
                    {
                        Vector<float> wave = new(_voiceScratch, frame);
                        int mixIndex = startFrame + frame;
                        Vector<float> left = new(leftMix, mixIndex);
                        Vector<float> right = new(rightMix, mixIndex);
                        (left + wave * leftGainVector).CopyTo(leftMix, mixIndex);
                        (right + wave * rightGainVector).CopyTo(rightMix, mixIndex);

                        if (captureChannel)
                        {
                            float[] capture = channelCaptureBuffers![channelIndex];
                            Vector<float> current = new(capture, mixIndex);
                            (current + wave * monoGainVector).CopyTo(capture, mixIndex);
                        }
                    }
                    _numericsMixCalls++;
                    _numericsMixedFrames += frame;
                }

                for (; frame < frameCount; frame++)
                {
                    float wave = _voiceScratch[frame];
                    int mixIndex = startFrame + frame;
                    leftMix[mixIndex] += wave * leftGain;
                    rightMix[mixIndex] += wave * rightGain;
                    if (captureChannel)
                        channelCaptureBuffers![channelIndex][mixIndex] += wave * gain;
                }

            }
        }
    }

    public void GetVoiceStageCounts(out int held, out int release, out int percussion, out int inactive)
    {
        held = release = percussion = inactive = 0;
        for (int i = 0; i < _voices.Length; i++)
        {
            ref VoiceState voice = ref _voices[i];
            if (!voice.Active) { inactive++; continue; }
            if (voice.Logical.IsPercussion) percussion++;
            else if (voice.EnvelopeStage == VoiceEnvelopeStage.Release) release++;
            else held++;
        }
    }

    /// <summary>
    /// Kernel RT-7A para ARM NEON. Procesa cuatro frames por iteración.
    /// Devuelve cuántos frames fueron procesados; el caller termina el tail
    /// escalar. No hay allocations ni llamadas administradas dentro del loop.
    /// </summary>
    private unsafe int MixVoiceNeon(
        float[] leftMix,
        float[] rightMix,
        float[]? capture,
        int startFrame,
        int frameCount,
        float leftGain,
        float rightGain,
        float monoGain)
    {
        if (!AdvSimd.IsSupported || frameCount < 4)
            return 0;

        int vectorEnd = frameCount & ~3;
        Vector128<float> leftGainVector = Vector128.Create(leftGain);
        Vector128<float> rightGainVector = Vector128.Create(rightGain);
        Vector128<float> monoGainVector = Vector128.Create(monoGain);

        fixed (float* waveBase = _voiceScratch)
        fixed (float* leftBase = leftMix)
        fixed (float* rightBase = rightMix)
        {
            if (capture is not null)
            {
                fixed (float* captureBase = capture)
                {
                    for (int frame = 0; frame < vectorEnd; frame += 4)
                    {
                        int mixIndex = startFrame + frame;
                        Vector128<float> wave = AdvSimd.LoadVector128(waveBase + frame);
                        Vector128<float> left = AdvSimd.LoadVector128(leftBase + mixIndex);
                        Vector128<float> right = AdvSimd.LoadVector128(rightBase + mixIndex);
                        Vector128<float> currentCapture = AdvSimd.LoadVector128(captureBase + mixIndex);

                        left = AdvSimd.Add(left, AdvSimd.Multiply(wave, leftGainVector));
                        right = AdvSimd.Add(right, AdvSimd.Multiply(wave, rightGainVector));
                        currentCapture = AdvSimd.Add(
                            currentCapture,
                            AdvSimd.Multiply(wave, monoGainVector));

                        AdvSimd.Store(leftBase + mixIndex, left);
                        AdvSimd.Store(rightBase + mixIndex, right);
                        AdvSimd.Store(captureBase + mixIndex, currentCapture);
                    }
                }
            }
            else
            {
                for (int frame = 0; frame < vectorEnd; frame += 4)
                {
                    int mixIndex = startFrame + frame;
                    Vector128<float> wave = AdvSimd.LoadVector128(waveBase + frame);
                    Vector128<float> left = AdvSimd.LoadVector128(leftBase + mixIndex);
                    Vector128<float> right = AdvSimd.LoadVector128(rightBase + mixIndex);

                    left = AdvSimd.Add(left, AdvSimd.Multiply(wave, leftGainVector));
                    right = AdvSimd.Add(right, AdvSimd.Multiply(wave, rightGainVector));

                    AdvSimd.Store(leftBase + mixIndex, left);
                    AdvSimd.Store(rightBase + mixIndex, right);
                }
            }
        }

        return vectorEnd;
    }

    private void BuildWaveBuckets()
    {
        Array.Clear(_bucketCounts);
        int bucketed = 0;

        for (int voiceIndex = 0; voiceIndex < _voices.Length; voiceIndex++)
        {
            ref VoiceState voice = ref _voices[voiceIndex];
            if (!voice.Active)
                continue;

            int bucket = voice.Logical.IsPercussion
                ? DrumBucket
                : WaveTypeToBucket(voice.Logical.WaveType);
            int count = _bucketCounts[bucket];
            _bucketVoiceIndices[bucket * _voices.Length + count] = voiceIndex;
            _bucketCounts[bucket] = count + 1;
            bucketed++;
        }

        _bucketBuildCount++;
        _bucketedVoiceCount += bucketed;
    }

    private static int WaveTypeToBucket(ChiptuneWaveType waveType)
        => waveType switch
        {
            ChiptuneWaveType.Pulse50 => 0,
            ChiptuneWaveType.Pulse25 => 1,
            ChiptuneWaveType.Sine => 2,
            ChiptuneWaveType.Triangle => 3,
            ChiptuneWaveType.Saw => 4,
            ChiptuneWaveType.BassHybrid => 5,
            ChiptuneWaveType.Noise => 6,
            _ => 0,
        };

    private static ChiptuneWaveType BucketToWaveType(int bucket)
        => bucket switch
        {
            0 => ChiptuneWaveType.Pulse50,
            1 => ChiptuneWaveType.Pulse25,
            2 => ChiptuneWaveType.Sine,
            3 => ChiptuneWaveType.Triangle,
            4 => ChiptuneWaveType.Saw,
            5 => ChiptuneWaveType.BassHybrid,
            6 => ChiptuneWaveType.Noise,
            _ => ChiptuneWaveType.Pulse50,
        };

    public void RenderFrame(ChannelState[] channels, int sampleRate, out float left, out float right)
    {
        RenderFrame(channels, sampleRate, null, null, -1, out left, out right);
    }

    public void RenderFrame(
        ChannelState[] channels,
        int sampleRate,
        float[][]? channelCaptureBuffers,
        bool[]? captureChannels,
        int captureFrameIndex,
        out float left,
        out float right)
    {
        left = 0f;
        right = 0f;

        for (int index = 0; index < _voices.Length; index++)
        {
            ref VoiceState voice = ref _voices[index];
            if (!voice.Active)
                continue;

            int channelIndex = voice.ChannelIndex;
            if ((uint)channelIndex >= (uint)channels.Length)
            {
                voice.Reset();
                continue;
            }

            ChannelState channel = channels[channelIndex];
            float sample = voice.NextSample(channel, sampleRate);
            float gain = voice.Velocity * channel.Volume * channel.Expression * channel.UserGain * (channel.SoftPedal ? 0.72f : 1f) * LyraMasterVoiceGain;
            float mono = sample * gain;
            left += mono * channel.PanGainL;
            right += mono * channel.PanGainR;

            if (channelCaptureBuffers is not null &&
                captureChannels is not null &&
                captureFrameIndex >= 0 &&
                (uint)channelIndex < (uint)captureChannels.Length &&
                captureChannels[channelIndex])
            {
                channelCaptureBuffers[channelIndex][captureFrameIndex] += mono;
            }
        }
    }

    private int FindNewestHeldVoice(int channelIndex)
    {
        int selected = -1;
        long newest = long.MinValue;
        for (int index = 0; index < _voices.Length; index++)
        {
            ref VoiceState voice = ref _voices[index];
            if (!voice.Active || voice.ChannelIndex != channelIndex || voice.Logical.IsPercussion || !voice.Logical.KeyHeld)
                continue;
            if (voice.StartSequence > newest)
            {
                newest = voice.StartSequence;
                selected = index;
            }
        }
        return selected;
    }

    private int FindVoiceForStart()
    {
        // Camino común: mientras haya hueco no pagamos ningún scoring.
        for (int index = 0; index < _voices.Length; index++)
        {
            if (!_voices[index].Active)
                return index;
        }

        // Pool global lleno. Elegimos la voz menos valiosa acústicamente.
        // Menor score = mejor candidata al robo.
        int selectedIndex = 0;
        float selectedScore = float.MaxValue;
        bool selectedWasDuplicate = false;

        long newestSequence = _startSequence;
        for (int index = 0; index < _voices.Length; index++)
        {
            ref VoiceState voice = ref _voices[index];
            bool duplicate = HasNewerDuplicate(index);
            float score = ComputeStealScore(in voice, duplicate, newestSequence);

            if (score < selectedScore)
            {
                selectedScore = score;
                selectedIndex = index;
                selectedWasDuplicate = duplicate;
            }
            else if (MathF.Abs(score - selectedScore) < 0.0001f &&
                     voice.StartSequence < _voices[selectedIndex].StartSequence)
            {
                // Empate: roba la más vieja.
                selectedIndex = index;
                selectedWasDuplicate = duplicate;
            }
        }

        ref VoiceState selected = ref _voices[selectedIndex];
        _stolenVoiceCount++;
        if (selectedWasDuplicate)
            _duplicateStealCount++;
        if (selected.EnvelopeStage == VoiceEnvelopeStage.Release)
            _releaseStealCount++;

        return selectedIndex;
    }

    private bool HasNewerDuplicate(int candidateIndex)
    {
        ref VoiceState candidate = ref _voices[candidateIndex];
        if (!candidate.Active)
            return false;

        for (int index = 0; index < _voices.Length; index++)
        {
            if (index == candidateIndex)
                continue;

            ref VoiceState other = ref _voices[index];
            if (!other.Active)
                continue;

            if (other.ChannelIndex == candidate.ChannelIndex &&
                other.Note == candidate.Note &&
                other.StartSequence > candidate.StartSequence)
                return true;
        }

        return false;
    }

    private static float ComputeStealScore(
        in VoiceState voice,
        bool hasNewerDuplicate,
        long newestSequence)
    {
        // Etapa de envelope. Release debe desaparecer primero; Attack se protege.
        float score = voice.EnvelopeStage switch
        {
            VoiceEnvelopeStage.Release => -1200f,
            VoiceEnvelopeStage.Sustain => 0f,
            VoiceEnvelopeStage.Decay => 260f,
            VoiceEnvelopeStage.Attack => 1050f,
            _ => -2000f,
        };

        // Una voz casi inaudible es barata de perder. Level y velocity se ponderan
        // por separado porque ambas determinan energía efectiva.
        score += Math.Clamp(voice.Level, 0f, 1f) * 520f;
        score += Math.Clamp(voice.Velocity, 0f, 1f) * 120f;

        // El pedal puede acumular decenas de voces que el usuario ya no sostiene.
        // Son mejores candidatas que una nota físicamente pulsada.
        if (voice.Logical.SustainHeld && !voice.Logical.KeyHeld)
            score -= 280f;
        if (voice.Logical.KeyHeld)
            score += 220f;

        // Si la misma nota/canal ya tiene una instancia más nueva, la antigua
        // aporta poca información musical y se roba antes.
        if (hasNewerDuplicate)
            score -= 520f;

        // Protecciones de percusión: kick/snare conservan el pulso; hats/cymbals
        // pueden sacrificarse antes cuando la mezcla está saturada de voces.
        if (voice.Logical.IsPercussion)
        {
            score += voice.Render.DrumKind switch
            {
                DrumVoiceKind.Kick => 1150f,
                DrumVoiceKind.Snare => 900f,
                DrumVoiceKind.Tom => 420f,
                DrumVoiceKind.Clap => 300f,
                DrumVoiceKind.Percussion => 180f,
                DrumVoiceKind.OpenHiHat => 80f,
                DrumVoiceKind.ClosedHiHat => 20f,
                DrumVoiceKind.Cymbal => -40f,
                _ => 0f,
            };
        }

        // Preferencia suave por conservar voces recientes. Está acotada para no
        // impedir que un release silencioso sea robado aunque sea nuevo.
        long age = Math.Max(0, newestSequence - voice.StartSequence);
        score -= Math.Min(age, 512L) * 0.35f;

        return score;
    }
}
