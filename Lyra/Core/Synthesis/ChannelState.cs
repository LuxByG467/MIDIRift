using MIDIRift.CleanRoom.Features.Midi;

namespace MIDIRift.CleanRoom.Features.Midi.Synthesis;

public sealed class ChannelState
{
    public int RuntimeIndex { get; }
    public MidiChannelId Id { get; }
    public bool IsPercussion { get; }

    public int Program { get; private set; }
    public ChiptuneWaveType WaveType { get; private set; }
    public float Volume { get; private set; }
    public float Expression { get; private set; }
    public float Pan { get; private set; }
    public float PanGainL { get; private set; }
    public float PanGainR { get; private set; }
    public float PitchBendRatio { get; private set; }
    public float PitchBendRangeSemitones { get; private set; } = 2f;
    private float _pitchBendNormalized;
    public float FineTuningSemitones { get; private set; }
    public float CoarseTuningSemitones { get; private set; }
    public bool Sustain { get; private set; }
    public bool Sostenuto { get; private set; }
    public bool SoftPedal { get; private set; }
    public float Modulation { get; private set; }
    public bool PortamentoEnabled { get; private set; }
    public bool LegatoEnabled { get; private set; }
    public float PortamentoSeconds { get; private set; }
    public int? PortamentoSourceNote { get; private set; }
    public float Brightness { get; private set; } = 64f / 127f;
    public float Resonance { get; private set; } = 64f / 127f;
    public float AttackTime { get; private set; } = 64f / 127f;
    public float DecayTime { get; private set; } = 64f / 127f;
    public float ReleaseTime { get; private set; } = 64f / 127f;
    public float VibratoRate { get; private set; } = 64f / 127f;
    public float VibratoDepth { get; private set; } = 64f / 127f;
    public float VibratoDelay { get; private set; } = 64f / 127f;
    public float UserGain { get; private set; } = 1f;
    public bool Muted { get; private set; }
    public bool Solo { get; private set; }

    private readonly int _initialProgram;
    private readonly ChiptuneWaveType _initialWaveType;

    public ChannelState(CompiledChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        RuntimeIndex = channel.RuntimeIndex;
        Id = channel.Id;
        IsPercussion = channel.IsPercussion;
        _initialProgram = channel.InitialProgram;
        _initialWaveType = channel.InitialWaveType;
        Reset();
    }

    public void Reset()
    {
        Program = _initialProgram;
        WaveType = _initialWaveType;
        Volume = 1f;
        Expression = 1f;
        Pan = 0f;
        PanGainL = PanGainR = 0.70710678f;
        PitchBendRatio = 1f;
        PitchBendRangeSemitones = 2f;
        _pitchBendNormalized = 0f;
        FineTuningSemitones = 0f;
        CoarseTuningSemitones = 0f;
        Sustain = false;
        Sostenuto = false;
        SoftPedal = false;
        Modulation = 0f;
        PortamentoEnabled = false;
        LegatoEnabled = false;
        PortamentoSeconds = 0.08f;
        PortamentoSourceNote = null;
        Brightness = Resonance = AttackTime = DecayTime = ReleaseTime =
            VibratoRate = VibratoDepth = VibratoDelay = 64f / 127f;
    }

    public void SetProgram(int program)
    {
        Program = Math.Clamp(program, 0, 127);
        WaveType = ChiptuneWaveTypeMapper.FromProgram(Id.Channel, Program);
    }

    public void SetWaveType(ChiptuneWaveType waveType) => WaveType = waveType;

    public void SetVolume(float value) => Volume = Math.Clamp(value, 0f, 1f);
    public void SetExpression(float value) => Expression = Math.Clamp(value, 0f, 1f);
    public void SetPan(float value)
    {
        Pan = Math.Clamp(value, -1f, 1f);
        PanGainL = FastAudioMath.Sqrt((1f - Pan) * 0.5f);
        PanGainR = FastAudioMath.Sqrt((1f + Pan) * 0.5f);
    }

    public void SetPitchBend(float normalizedBend)
    {
        _pitchBendNormalized = Math.Clamp(normalizedBend, -1f, 1f);
        RecalculatePitchRatio();
    }

    public void SetPitchBendRange(float semitones)
    {
        PitchBendRangeSemitones = Math.Clamp(semitones, 0f, 24f);
        RecalculatePitchRatio();
    }

    public void SetFineTuning(float semitones)
    {
        FineTuningSemitones = Math.Clamp(semitones, -1f, 1f);
        RecalculatePitchRatio();
    }

    public void SetCoarseTuning(float semitones)
    {
        CoarseTuningSemitones = Math.Clamp(semitones, -64f, 63f);
        RecalculatePitchRatio();
    }

    private void RecalculatePitchRatio()
    {
        float totalSemitones =
            _pitchBendNormalized * PitchBendRangeSemitones +
            FineTuningSemitones +
            CoarseTuningSemitones;
        PitchBendRatio = FastAudioMath.Exp2(totalSemitones / 12f);
    }

    public void SetSustain(bool enabled) => Sustain = enabled;
    public void SetSostenuto(bool enabled) => Sostenuto = enabled;
    public void SetSoftPedal(bool enabled) => SoftPedal = enabled;
    public void SetModulation(float value) => Modulation = Math.Clamp(value, 0f, 1f);
    public void SetPortamentoEnabled(bool enabled) => PortamentoEnabled = enabled;
    public void SetLegatoEnabled(bool enabled) => LegatoEnabled = enabled;
    public void SetUserMix(float gain, bool muted, bool solo)
    {
        UserGain = Math.Clamp(gain, 0f, 2f);
        Muted = muted;
        Solo = solo;
    }

    public void SetPortamentoTime(float normalized)
    {
        float value = Math.Clamp(normalized, 0f, 1f);
        PortamentoSeconds = 0.005f + value * value * 1.995f;
    }

    public void SetBrightness(float value) => Brightness = Math.Clamp(value, 0f, 1f);
    public void SetResonance(float value) => Resonance = Math.Clamp(value, 0f, 1f);
    public void SetAttackTime(float value) => AttackTime = Math.Clamp(value, 0f, 1f);
    public void SetDecayTime(float value) => DecayTime = Math.Clamp(value, 0f, 1f);
    public void SetReleaseTime(float value) => ReleaseTime = Math.Clamp(value, 0f, 1f);
    public void SetVibratoRate(float value) => VibratoRate = Math.Clamp(value, 0f, 1f);
    public void SetVibratoDepth(float value) => VibratoDepth = Math.Clamp(value, 0f, 1f);
    public void SetVibratoDelay(float value) => VibratoDelay = Math.Clamp(value, 0f, 1f);

    public void SetPortamentoSourceNote(int midiNote)
        => PortamentoSourceNote = Math.Clamp(midiNote, 0, 127);

    public int? ConsumePortamentoSourceNote()
    {
        int? note = PortamentoSourceNote;
        PortamentoSourceNote = null;
        Brightness = Resonance = AttackTime = DecayTime = ReleaseTime =
            VibratoRate = VibratoDepth = VibratoDelay = 64f / 127f;
        return note;
    }

    public void ResetControllers()
    {
        Volume = 1f;
        Expression = 1f;
        SetPan(0f);
        PitchBendRatio = 1f;
        PitchBendRangeSemitones = 2f;
        _pitchBendNormalized = 0f;
        FineTuningSemitones = 0f;
        CoarseTuningSemitones = 0f;
        Sustain = false;
        Sostenuto = false;
        SoftPedal = false;
        Modulation = 0f;
        PortamentoEnabled = false;
        LegatoEnabled = false;
        PortamentoSeconds = 0.08f;
        PortamentoSourceNote = null;
    }

    public ChannelStateSnapshot CaptureSnapshot()
    {
        return new ChannelStateSnapshot(
            Program,
            WaveType,
            Volume,
            Expression,
            Pan,
            PitchBendRatio,
            PitchBendRangeSemitones,
            _pitchBendNormalized,
            FineTuningSemitones,
            CoarseTuningSemitones,
            Sustain,
            Sostenuto,
            SoftPedal,
            Modulation,
            PortamentoEnabled,
            LegatoEnabled,
            PortamentoSeconds,
            PortamentoSourceNote,
            Brightness,
            Resonance,
            AttackTime,
            DecayTime,
            ReleaseTime,
            VibratoRate,
            VibratoDepth,
            VibratoDelay);
    }

    public void RestoreSnapshot(ChannelStateSnapshot snapshot)
    {
        Program = snapshot.Program;
        WaveType = snapshot.WaveType;
        Volume = snapshot.Volume;
        Expression = snapshot.Expression;
        SetPan(snapshot.Pan);
        PitchBendRangeSemitones = Math.Clamp(snapshot.PitchBendRangeSemitones, 0f, 24f);
        _pitchBendNormalized = Math.Clamp(snapshot.PitchBendNormalized, -1f, 1f);
        FineTuningSemitones = Math.Clamp(snapshot.FineTuningSemitones, -1f, 1f);
        CoarseTuningSemitones = Math.Clamp(snapshot.CoarseTuningSemitones, -64f, 63f);

        // Recalculate from the semantic components rather than trusting only
        // the cached ratio. This keeps future Pitch Bend events correct after seek.
        RecalculatePitchRatio();

        Sustain = snapshot.Sustain;
        Sostenuto = snapshot.Sostenuto;
        SoftPedal = snapshot.SoftPedal;
        Modulation = Math.Clamp(snapshot.Modulation, 0f, 1f);
        PortamentoEnabled = snapshot.PortamentoEnabled;
        LegatoEnabled = snapshot.LegatoEnabled;
        PortamentoSeconds = Math.Clamp(snapshot.PortamentoSeconds, 0.005f, 2f);
        PortamentoSourceNote = snapshot.PortamentoSourceNote.HasValue
            ? Math.Clamp(snapshot.PortamentoSourceNote.Value, 0, 127)
            : null;
        Brightness = Math.Clamp(snapshot.Brightness, 0f, 1f);
        Resonance = Math.Clamp(snapshot.Resonance, 0f, 1f);
        AttackTime = Math.Clamp(snapshot.AttackTime, 0f, 1f);
        DecayTime = Math.Clamp(snapshot.DecayTime, 0f, 1f);
        ReleaseTime = Math.Clamp(snapshot.ReleaseTime, 0f, 1f);
        VibratoRate = Math.Clamp(snapshot.VibratoRate, 0f, 1f);
        VibratoDepth = Math.Clamp(snapshot.VibratoDepth, 0f, 1f);
        VibratoDelay = Math.Clamp(snapshot.VibratoDelay, 0f, 1f);
    }
}
