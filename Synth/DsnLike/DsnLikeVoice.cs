using System.Runtime.CompilerServices;

namespace MIDIRift.Synth.DsnLike;

/// <summary>
/// Cheap first-pass dual-VCO voice. Expensive parameter work happens on NoteOn
/// or every ControlInterval samples, not on every audio sample.
/// </summary>
public sealed class DsnLikeVoice
{
    public const int ControlInterval = 32;

    private readonly float _sampleRate;
    private DsnLikePatch _patch = DsnLikePatch.Default;
    private DsnOscillator _osc1;
    private DsnOscillator _osc2;
    private DsnEnvelope _envelope;
    private DsnLfo _lfo;
    private DsnStateVariableFilter _filter;

    private float _baseInc1;
    private float _pitchBendRatio = 1f;
    private float _modWheel;
    private float _brightness = 64f / 127f;
    private float _midiResonance = 64f / 127f;
    private float _midiAttack = 64f / 127f;
    private float _midiDecay = 64f / 127f;
    private float _midiRelease = 64f / 127f;
    private float _midiVibratoRate = 64f / 127f;
    private float _midiVibratoDepth = 64f / 127f;
    private float _midiVibratoDelay = 64f / 127f;
    private float _softPedalGain = 1f;
    private int _vibratoAgeFrames;
    private float _unmodulatedInc1;
    private float _portamentoTargetInc1;
    private float _portamentoRatioPerControl = 1f;
    private int _portamentoControlsRemaining;
    private float _baseInc2;
    private float _noteBaseInc1;
    private float _osc2Ratio;
    private float _velocity;
    private float _lfoIncrement;
    private float _lfoValue;
    private float _cutoff;
    private float _pulseWidth1;
    private float _pulseWidth2;
    private int _controlCountdown;

    // Compiled hot-path patch state. Do not repeatedly dereference the record
    // or evaluate disabled DSP inside the sample loop.
    private DsnOscillatorWave _osc1Wave;
    private DsnOscillatorWave _osc2Wave;
    private DsnFilterMode _filterMode;
    private float _osc1Level;
    private float _osc2Level;
    private float _fmAmount;
    private float _outputGain;
    private float _driveGain;
    private bool _useFm;
    private bool _useSync;
    private bool _useDrive;
    private DsnVoiceRoute _route;

    public DsnLikeVoice(float sampleRate = 44100f, uint seed = 1)
    {
        if (sampleRate <= 0f)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));

        _sampleRate = sampleRate;
        _osc1 = new DsnOscillator(seed * 747796405u + 2891336453u);
        _osc2 = new DsnOscillator(seed * 277803737u + 1013904223u);
        _lfo = new DsnLfo(seed ^ 0x9E3779B9u);
        ApplyPatch(DsnLikePatch.Default);
    }

    public bool IsActive => _envelope.Stage != DsnEnvelopeStage.Off;
    public int Note { get; private set; } = -1;
    public float EnvelopeLevel => _envelope.Level;
    public bool KeyHeld { get; private set; }
    public bool SustainHeld { get; private set; }
    public bool SostenutoHeld { get; private set; }
    public long StartSequence { get; private set; }
    public DsnEnvelopeStage EnvelopeStage => _envelope.Stage;

    public void ApplyPatch(DsnLikePatch patch)
    {
        _patch = patch ?? throw new ArgumentNullException(nameof(patch));
        ConfigureEffectiveEnvelope();
        UpdateEffectiveLfoRate();

        _osc1Wave = _patch.Osc1Wave;
        _osc2Wave = _patch.Osc2Wave;
        _filterMode = _patch.FilterMode;
        _osc1Level = _patch.Osc1Level;
        _osc2Level = _patch.Osc2Level;
        _fmAmount = _patch.FmAmount;
        _outputGain = _patch.OutputGain;
        _osc2Ratio = MathF.Pow(2f, _patch.Osc2Semitones / 12f);
        _driveGain = 1f + _patch.Drive * 6f;
        _useFm = DsnFastMath.FastAbs(_fmAmount) > 0.0001f;
        _useSync = _patch.HardSync;
        _useDrive = _patch.Drive > 0.0001f;
        _route = !_useFm && !_useSync
            ? (_useDrive ? DsnVoiceRoute.DriveOnly : DsnVoiceRoute.Clean)
            : (_useFm && _useSync && _useDrive ? DsnVoiceRoute.Full : DsnVoiceRoute.Generic);

        _controlCountdown = 0;
        UpdateControl();
    }

    public void NoteOn(int midiNote, float velocity = 1f, long startSequence = 0, int? glideFromNote = null, float portamentoSeconds = 0f)
    {
        Note = midiNote;
        StartSequence = startSequence;
        KeyHeld = true;
        SustainHeld = false;
        SostenutoHeld = false;
        _velocity = DsnFastMath.Clamp(velocity, 0f, 1f);
        _vibratoAgeFrames = 0;

        // MathF.Pow is intentionally outside the sample loop.
        float hz = 440f * MathF.Pow(2f, (midiNote - 69) / 12f);
        _noteBaseInc1 = hz / _sampleRate;
        _portamentoTargetInc1 = _noteBaseInc1;
        if (glideFromNote.HasValue && portamentoSeconds > 0.0001f)
        {
            float fromHz = 440f * MathF.Pow(2f, (glideFromNote.Value - 69) / 12f);
            _unmodulatedInc1 = fromHz / _sampleRate;
            _baseInc1 = _unmodulatedInc1;
            _portamentoControlsRemaining = Math.Max(1, (int)MathF.Ceiling(portamentoSeconds * _sampleRate / ControlInterval));
            _portamentoRatioPerControl = MathF.Pow(_portamentoTargetInc1 / Math.Max(0.0000001f, _baseInc1), 1f / _portamentoControlsRemaining);
        }
        else
        {
            _unmodulatedInc1 = _noteBaseInc1;
            _baseInc1 = _unmodulatedInc1;
            _portamentoControlsRemaining = 0;
            _portamentoRatioPerControl = 1f;
        }
        _baseInc2 = _baseInc1 * _osc2Ratio;

        _osc1.ResetPhase();
        _osc2.ResetPhase();
        _filter.Reset();
        _envelope.NoteOn();
        _controlCountdown = 0;
    }

    public void NoteOff() => HandleNoteOff(sustain: false);

    public void HandleNoteOff(bool sustain)
    {
        if (!IsActive || !KeyHeld)
            return;

        // One MIDI NoteOff consumes exactly one physical key-held voice.
        // With sustain down, remember that this particular voice has already
        // consumed its NoteOff, but do not start Release until pedal-up.
        KeyHeld = false;
        SustainHeld = sustain;
        if (!sustain && !SostenutoHeld)
            _envelope.NoteOff();
    }

    public void ReleaseFromSustain()
    {
        if (!IsActive || KeyHeld || !SustainHeld)
            return;

        SustainHeld = false;
        SostenutoHeld = false;
        _envelope.NoteOff();
    }

    public void CaptureSostenuto()
    {
        if (IsActive && KeyHeld)
            SostenutoHeld = true;
    }

    public void ReleaseFromSostenuto(bool sustainDown)
    {
        if (!IsActive || !SostenutoHeld)
            return;

        SostenutoHeld = false;
        if (!KeyHeld && !SustainHeld && !sustainDown)
            _envelope.NoteOff();
    }

    public void SetSoftPedal(bool enabled)
    {
        // MIDI soft pedal is an expressive controller. DSN keeps it transient:
        // modest attenuation without changing/saving the patch.
        _softPedalGain = enabled ? 0.72f : 1f;
    }

    public void BeginRelease()
    {
        if (!IsActive)
            return;

        KeyHeld = false;
        SustainHeld = false;
        _envelope.NoteOff();
    }

    public void Reset()
    {
        Note = -1;
        KeyHeld = false;
        SustainHeld = false;
        SostenutoHeld = false;
        StartSequence = 0;
        _velocity = 0f;
        _osc1.ResetPhase();
        _osc2.ResetPhase();
        _filter.Reset();
        _envelope.Reset();
        _controlCountdown = 0;
        _pitchBendRatio = 1f;
        _modWheel = 0f;
        _brightness = 64f / 127f;
        _midiResonance = 64f / 127f;
        _midiAttack = 64f / 127f;
        _midiDecay = 64f / 127f;
        _midiRelease = 64f / 127f;
        _midiVibratoRate = 64f / 127f;
        _midiVibratoDepth = 64f / 127f;
        _midiVibratoDelay = 64f / 127f;
        _vibratoAgeFrames = 0;
        _portamentoControlsRemaining = 0;
        _portamentoRatioPerControl = 1f;
        _portamentoTargetInc1 = 0f;
    }

    public void SetPitchBendRatio(float ratio)
        => _pitchBendRatio = DsnFastMath.Clamp(ratio, 0.25f, 4f);

    public void SetModulation(float value)
        => _modWheel = DsnFastMath.Clamp(value, 0f, 1f);

    public void SetBrightness(float value)
    {
        _brightness = DsnFastMath.Clamp(value, 0f, 1f);
        // Force a control update soon so a CC74 sweep is audible without
        // waiting for an unrelated state transition.
        _controlCountdown = 0;
    }

    public void SetResonance(float value)
    {
        _midiResonance = DsnFastMath.Clamp(value, 0f, 1f);
        _controlCountdown = 0;
    }

    public void SetAttackTime(float value)
    {
        _midiAttack = DsnFastMath.Clamp(value, 0f, 1f);
        ConfigureEffectiveEnvelope();
    }

    public void SetDecayTime(float value)
    {
        _midiDecay = DsnFastMath.Clamp(value, 0f, 1f);
        ConfigureEffectiveEnvelope();
    }

    public void SetReleaseTime(float value)
    {
        _midiRelease = DsnFastMath.Clamp(value, 0f, 1f);
        ConfigureEffectiveEnvelope();
    }

    public void SetVibratoRate(float value)
    {
        _midiVibratoRate = DsnFastMath.Clamp(value, 0f, 1f);
        UpdateEffectiveLfoRate();
    }

    public void SetVibratoDepth(float value)
    {
        _midiVibratoDepth = DsnFastMath.Clamp(value, 0f, 1f);
        _controlCountdown = 0;
    }

    public void SetVibratoDelay(float value)
    {
        _midiVibratoDelay = DsnFastMath.Clamp(value, 0f, 1f);
        _controlCountdown = 0;
    }

    private static float CenteredController(float value)
        => (value - (64f / 127f)) * (127f / 63f);

    private static float TimeScale(float value)
        => MathF.Pow(2f, CenteredController(value) * 2f);

    private void ConfigureEffectiveEnvelope()
    {
        _envelope.Configure(
            _patch.AttackSeconds * TimeScale(_midiAttack),
            _patch.DecaySeconds * TimeScale(_midiDecay),
            _patch.SustainLevel,
            _patch.ReleaseSeconds * TimeScale(_midiRelease),
            _sampleRate);
    }

    private void UpdateEffectiveLfoRate()
    {
        float rateScale = MathF.Pow(2f, CenteredController(_midiVibratoRate));
        _lfoIncrement = DsnFastMath.Clamp(_patch.LfoHz * rateScale, 0f, 100f) / _sampleRate;
    }

    public void AdvanceStateOnly(int frames)
    {
        if (!IsActive || frames <= 0) return;
        _envelope.AdvanceFrames(frames);
        _vibratoAgeFrames = Math.Min(int.MaxValue - frames, _vibratoAgeFrames) + frames;
        if (!IsActive) { KeyHeld = false; SustainHeld = false; return; }

        if (_portamentoControlsRemaining > 0)
        {
            int controls = Math.Min(_portamentoControlsRemaining, Math.Max(1, (frames + ControlInterval - 1) / ControlInterval));
            _unmodulatedInc1 *= MathF.Pow(_portamentoRatioPerControl, controls);
            _portamentoControlsRemaining -= controls;
            if (_portamentoControlsRemaining <= 0) _unmodulatedInc1 = _portamentoTargetInc1;
            _baseInc1 = _unmodulatedInc1 * _pitchBendRatio;
            _baseInc2 = _baseInc1 * _osc2Ratio;
        }
    }

    public void Render(Span<float> destination)
    {
        if (!IsActive || destination.IsEmpty)
            return;

        // Sustained notes dominate real music and benchmarks. Once ADSR reaches
        // Sustain, do not execute the envelope state-machine switch per sample.
        if (_envelope.Stage == DsnEnvelopeStage.Sustain)
        {
            RenderSustainBlock(destination);
            return;
        }

        // 0.1.8: real MIDI spends a surprising amount of time in Attack/Decay/Release.
        // Keep exact envelope stepping, but do not fall back to the fully generic
        // FM/sync/drive renderer for the common clean Saw+Pulse+LP patch.
        if (_route == DsnVoiceRoute.Clean &&
            _osc1Wave == DsnOscillatorWave.Saw &&
            _osc2Wave == DsnOscillatorWave.Pulse &&
            _filterMode == DsnFilterMode.LowPass)
        {
            RenderTransitionSawPulseLowPass(destination);
            return;
        }

        for (int i = 0; i < destination.Length; i++)
        {
            if (!IsActive)
                break;
            destination[i] += RenderSample();
        }
    }

    private void RenderSustainBlock(Span<float> destination)
    {
        float envGain = _envelope.Level * _velocity * _outputGain * _softPedalGain;

        // Compile the broad patch route once in ApplyPatch. Common clean patches
        // never execute FM/sync/drive conditionals in their sample loop.
        if (_route == DsnVoiceRoute.Clean)
        {
            // Dispatch waveform/filter specialization once per block, never once per sample.
            // Default (Saw + Pulse + LP) is deliberately the hottest route.
            if (_osc1Wave == DsnOscillatorWave.Saw &&
                _osc2Wave == DsnOscillatorWave.Pulse &&
                _filterMode == DsnFilterMode.LowPass)
            {
                RenderSustainSawPulseLowPass(destination, envGain);
                return;
            }

            if (_osc1Wave == DsnOscillatorWave.Pulse &&
                _osc2Wave == DsnOscillatorWave.Saw &&
                _filterMode == DsnFilterMode.LowPass)
            {
                RenderSustainPulseSawLowPass(destination, envGain);
                return;
            }

            RenderSustainClean(destination, envGain);
            return;
        }

        // Diverse Factory Bank frequently uses Drive without FM/Sync. Previously
        // those voices fell through the generic per-sample renderer and paid
        // waveform dispatch + FM/sync branches + filter-mode dispatch every sample.
        if (_route == DsnVoiceRoute.DriveOnly)
        {
            if (_filterMode == DsnFilterMode.LowPass)
            {
                RenderSustainDriveLowPass(destination, envGain);
                return;
            }

            RenderSustainDrive(destination, envGain);
            return;
        }

        for (int i = 0; i < destination.Length; i++)
        {
            if (--_controlCountdown <= 0)
            {
                UpdateControl();
                _controlCountdown = ControlInterval;
            }

            float osc2 = _osc2.Render(_osc2Wave, _pulseWidth2);
            float osc1 = _osc1.Render(_osc1Wave, _pulseWidth1);

            bool osc1Wrapped;
            if (_useFm)
            {
                float fmScale = 1f + osc2 * _fmAmount;
                if (fmScale < 0.05f) fmScale = 0.05f;
                osc1Wrapped = _osc1.Advance(_baseInc1 * fmScale);
            }
            else
            {
                osc1Wrapped = _osc1.Advance(_baseInc1);
            }

            _osc2.Advance(_baseInc2);

            if (_useSync && osc1Wrapped)
                _osc2.ResetPhase();

            float mixed = osc1 * _osc1Level + osc2 * _osc2Level;
            float output = _filter.Process(mixed, _filterMode) * envGain;

            if (_useDrive)
                output = DsnFastMath.FastDrive(output * _driveGain);

            destination[i] += output;
        }
    }

    private void RenderTransitionSawPulseLowPass(Span<float> destination)
    {
        float phase1 = _osc1.Phase;
        float phase2 = _osc2.Phase;
        for (int i = 0; i < destination.Length && IsActive; i++)
        {
            if (--_controlCountdown <= 0)
            {
                UpdateControl();
                _controlCountdown = ControlInterval;
            }

            float osc1 = phase1 + phase1 - 1f;
            float osc2 = phase2 < _pulseWidth2 ? 1f : -1f;
            phase1 += _baseInc1; if (phase1 >= 1f) phase1 -= 1f;
            phase2 += _baseInc2; if (phase2 >= 1f) phase2 -= 1f;

            float env = _envelope.Next();
            float mixed = osc1 * _osc1Level + osc2 * _osc2Level;
            destination[i] += _filter.ProcessLowPass(mixed) * env * _velocity * _outputGain * _softPedalGain;
        }
        _osc1.Phase = phase1;
        _osc2.Phase = phase2;
    }

    private void RenderSustainSawPulseLowPass(Span<float> destination, float envGain)
    {
        // 0.1.6 block kernel: keep oscillator phase in locals and pay the
        // control-rate branch once per chunk instead of once per sample.
        float phase1 = _osc1.Phase;
        float phase2 = _osc2.Phase;
        int offset = 0;

        while (offset < destination.Length)
        {
            if (_controlCountdown <= 0)
            {
                UpdateControl();
                _controlCountdown = ControlInterval;
            }

            int count = Math.Min(_controlCountdown, destination.Length - offset);
            float inc1 = _baseInc1;
            float inc2 = _baseInc2;
            float pw2 = _pulseWidth2;
            float level1 = _osc1Level;
            float level2 = _osc2Level;

            int end = offset + count;
            for (int i = offset; i < end; i++)
            {
                float osc1 = phase1 + phase1 - 1f;
                float osc2 = phase2 < pw2 ? 1f : -1f;

                phase1 += inc1;
                if (phase1 >= 1f) phase1 -= 1f;
                else if (phase1 < 0f) phase1 += 1f;

                phase2 += inc2;
                if (phase2 >= 1f) phase2 -= 1f;
                else if (phase2 < 0f) phase2 += 1f;

                float mixed = osc1 * level1 + osc2 * level2;
                destination[i] += _filter.ProcessLowPass(mixed) * envGain;
            }

            offset = end;
            _controlCountdown -= count;
        }

        _osc1.Phase = phase1;
        _osc2.Phase = phase2;
    }

    private void RenderSustainPulseSawLowPass(Span<float> destination, float envGain)
    {
        float phase1 = _osc1.Phase;
        float phase2 = _osc2.Phase;
        int offset = 0;

        while (offset < destination.Length)
        {
            if (_controlCountdown <= 0)
            {
                UpdateControl();
                _controlCountdown = ControlInterval;
            }

            int count = Math.Min(_controlCountdown, destination.Length - offset);
            float inc1 = _baseInc1;
            float inc2 = _baseInc2;
            float pw1 = _pulseWidth1;
            float level1 = _osc1Level;
            float level2 = _osc2Level;

            int end = offset + count;
            for (int i = offset; i < end; i++)
            {
                float osc1 = phase1 < pw1 ? 1f : -1f;
                float osc2 = phase2 + phase2 - 1f;

                phase1 += inc1;
                if (phase1 >= 1f) phase1 -= 1f;
                else if (phase1 < 0f) phase1 += 1f;

                phase2 += inc2;
                if (phase2 >= 1f) phase2 -= 1f;
                else if (phase2 < 0f) phase2 += 1f;

                float mixed = osc1 * level1 + osc2 * level2;
                destination[i] += _filter.ProcessLowPass(mixed) * envGain;
            }

            offset = end;
            _controlCountdown -= count;
        }

        _osc1.Phase = phase1;
        _osc2.Phase = phase2;
    }

    // Specialized DSN0.2.6 kernel for the real-world hot route observed in
    // Hall of the Mountain King: Dual VCO + LP SVF + Drive, with FM/Sync disabled.
    // Control-rate state is chunked, oscillator state stays in locals, filter mode
    // is resolved once per block, and FastDrive is the only drive operation left
    // in the inner sample loop.
    private void RenderSustainDriveLowPass(Span<float> destination, float envGain)
    {
        float phase1 = _osc1.Phase;
        float phase2 = _osc2.Phase;
        int offset = 0;

        while (offset < destination.Length)
        {
            if (_controlCountdown <= 0)
            {
                UpdateControl();
                _controlCountdown = ControlInterval;
            }

            int count = Math.Min(_controlCountdown, destination.Length - offset);
            float inc1 = _baseInc1;
            float inc2 = _baseInc2;
            float pw1 = _pulseWidth1;
            float pw2 = _pulseWidth2;
            float level1 = _osc1Level;
            float level2 = _osc2Level;
            float driveGain = _driveGain;
            int end = offset + count;

            // Resolve waveform pair once per control chunk. Saw/Pulse and
            // Pulse/Saw are common enough to avoid generic oscillator dispatch.
            if (_osc1Wave == DsnOscillatorWave.Saw && _osc2Wave == DsnOscillatorWave.Pulse)
            {
                for (int i = offset; i < end; i++)
                {
                    float osc1 = phase1 + phase1 - 1f;
                    float osc2 = phase2 < pw2 ? 1f : -1f;
                    phase1 += inc1; if (phase1 >= 1f) phase1 -= 1f; else if (phase1 < 0f) phase1 += 1f;
                    phase2 += inc2; if (phase2 >= 1f) phase2 -= 1f; else if (phase2 < 0f) phase2 += 1f;
                    float mixed = osc1 * level1 + osc2 * level2;
                    float output = _filter.ProcessLowPass(mixed) * envGain;
                    destination[i] += DsnFastMath.FastDrive(output * driveGain);
                }
            }
            else if (_osc1Wave == DsnOscillatorWave.Pulse && _osc2Wave == DsnOscillatorWave.Saw)
            {
                for (int i = offset; i < end; i++)
                {
                    float osc1 = phase1 < pw1 ? 1f : -1f;
                    float osc2 = phase2 + phase2 - 1f;
                    phase1 += inc1; if (phase1 >= 1f) phase1 -= 1f; else if (phase1 < 0f) phase1 += 1f;
                    phase2 += inc2; if (phase2 >= 1f) phase2 -= 1f; else if (phase2 < 0f) phase2 += 1f;
                    float mixed = osc1 * level1 + osc2 * level2;
                    float output = _filter.ProcessLowPass(mixed) * envGain;
                    destination[i] += DsnFastMath.FastDrive(output * driveGain);
                }
            }
            else
            {
                // Less common waveform pairs still skip FM/sync/filter-mode/drive
                // conditionals; oscillator dispatch remains the only generic part.
                _osc1.Phase = phase1;
                _osc2.Phase = phase2;
                for (int i = offset; i < end; i++)
                {
                    float osc2 = _osc2.Render(_osc2Wave, pw2);
                    float osc1 = _osc1.Render(_osc1Wave, pw1);
                    _osc1.Advance(inc1);
                    _osc2.Advance(inc2);
                    float mixed = osc1 * level1 + osc2 * level2;
                    float output = _filter.ProcessLowPass(mixed) * envGain;
                    destination[i] += DsnFastMath.FastDrive(output * driveGain);
                }
                phase1 = _osc1.Phase;
                phase2 = _osc2.Phase;
            }

            offset = end;
            _controlCountdown -= count;
        }

        _osc1.Phase = phase1;
        _osc2.Phase = phase2;
    }

    // Drive-only fallback for HP/BP patches. Still removes FM/sync/drive branches
    // from every sample and resolves the filter mode once per block.
    private void RenderSustainDrive(Span<float> destination, float envGain)
    {
        for (int i = 0; i < destination.Length; i++)
        {
            if (--_controlCountdown <= 0)
            {
                UpdateControl();
                _controlCountdown = ControlInterval;
            }

            float osc2 = _osc2.Render(_osc2Wave, _pulseWidth2);
            float osc1 = _osc1.Render(_osc1Wave, _pulseWidth1);
            _osc1.Advance(_baseInc1);
            _osc2.Advance(_baseInc2);
            float mixed = osc1 * _osc1Level + osc2 * _osc2Level;
            float filtered = _filterMode == DsnFilterMode.HighPass
                ? _filter.ProcessHighPass(mixed)
                : _filter.ProcessBandPass(mixed);
            destination[i] += DsnFastMath.FastDrive(filtered * envGain * _driveGain);
        }
    }

    private void RenderSustainClean(Span<float> destination, float envGain)
    {
        for (int i = 0; i < destination.Length; i++)
        {
            if (--_controlCountdown <= 0)
            {
                UpdateControl();
                _controlCountdown = ControlInterval;
            }

            float osc2 = _osc2.Render(_osc2Wave, _pulseWidth2);
            float osc1 = _osc1.Render(_osc1Wave, _pulseWidth1);
            _osc1.Advance(_baseInc1);
            _osc2.Advance(_baseInc2);
            float mixed = osc1 * _osc1Level + osc2 * _osc2Level;
            float filtered = _filterMode switch
            {
                DsnFilterMode.HighPass => _filter.ProcessHighPass(mixed),
                DsnFilterMode.BandPass => _filter.ProcessBandPass(mixed),
                _ => _filter.ProcessLowPass(mixed)
            };
            destination[i] += filtered * envGain;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float RenderSample()
    {
        if (!IsActive)
            return 0f;

        if (--_controlCountdown <= 0)
        {
            UpdateControl();
            _controlCountdown = ControlInterval;
        }

        float env = _envelope.Next();

        float osc2 = _osc2.Render(_osc2Wave, _pulseWidth2);
        float osc1 = _osc1.Render(_osc1Wave, _pulseWidth1);

        bool osc1Wrapped;
        if (_useFm)
        {
            float fmScale = 1f + osc2 * _fmAmount;
            if (fmScale < 0.05f) fmScale = 0.05f;
            osc1Wrapped = _osc1.Advance(_baseInc1 * fmScale);
        }
        else
        {
            osc1Wrapped = _osc1.Advance(_baseInc1);
        }

        _osc2.Advance(_baseInc2);

        if (_useSync && osc1Wrapped)
            _osc2.ResetPhase();

        float mixed = osc1 * _osc1Level + osc2 * _osc2Level;
        float output = _filter.Process(mixed, _filterMode) * env * _velocity * _outputGain * _softPedalGain;

        if (_useDrive)
            output = DsnFastMath.SoftClip(output * _driveGain);

        return output;
    }

    private void UpdateControl()
    {
        _lfoValue = _lfo.Advance(_patch.LfoWave, _lfoIncrement * ControlInterval);

        float env = _envelope.Level;
        float cutoffMod = env * _patch.EnvelopeToCutoff + _lfoValue * _patch.LfoToCutoff;
        // Modulation is intentionally linear in Hz in v0.1: cheap and predictable.
        // CC74 is modulation, never patch mutation. MIDI 64 is neutral;
        // the full 0..127 range spans roughly -2..+2 octaves of cutoff.
        float brightnessCentered = (_brightness - (64f / 127f)) * (127f / 63f);
        float brightnessRatio = MathF.Pow(2f, brightnessCentered * 2f);
        _cutoff = _patch.CutoffHz * (1f + cutoffMod) * brightnessRatio;
        float resonanceCentered = (_midiResonance - (64f / 127f)) * (127f / 63f);
        float effectiveResonance = DsnFastMath.Clamp(
            _patch.Resonance + resonanceCentered * 0.45f, 0f, 0.95f);
        _filter.Set(_cutoff, effectiveResonance, _sampleRate);

        _pulseWidth1 = DsnFastMath.Clamp(
            _patch.PulseWidth1 + _lfoValue * _patch.LfoToPulseWidth * 0.45f, 0.05f, 0.95f);
        _pulseWidth2 = DsnFastMath.Clamp(
            _patch.PulseWidth2 + _lfoValue * _patch.LfoToPulseWidth * 0.45f, 0.05f, 0.95f);

        // Pitch bend is channel-wide. CC1 adds conventional vibrato depth on top
        // of the patch's own LFO pitch routing, while preserving the DSN patch identity.
        if (_portamentoControlsRemaining > 0)
        {
            _unmodulatedInc1 *= _portamentoRatioPerControl;
            if (--_portamentoControlsRemaining <= 0)
                _unmodulatedInc1 = _portamentoTargetInc1;
        }

        _vibratoAgeFrames = Math.Min(int.MaxValue - ControlInterval, _vibratoAgeFrames) + ControlInterval;
        float delayAboveNeutral = Math.Max(0f, CenteredController(_midiVibratoDelay));
        float delayFrames = delayAboveNeutral * 2f * _sampleRate;
        float vibratoRamp = delayFrames <= 1f
            ? 1f
            : DsnFastMath.Clamp((_vibratoAgeFrames - delayFrames) / Math.Max(1f, _sampleRate * 0.08f), 0f, 1f);

        float midiVibratoDepth = CenteredController(_midiVibratoDepth);
        float pitchDepthSemitones =
            (_patch.LfoToPitch + (_modWheel * 0.50f) + midiVibratoDepth) * vibratoRamp;
        float lfoRatio = 1f;
        if (pitchDepthSemitones != 0f && Note >= 0)
        {
            float cents = _lfoValue * pitchDepthSemitones * 100f;
            lfoRatio = MathF.Pow(2f, cents / 1200f);
        }

        float musicalBase = _unmodulatedInc1 * _pitchBendRatio * lfoRatio;
        _baseInc2 = musicalBase * _osc2Ratio;
        _baseInc1 = musicalBase;
    }
}


internal enum DsnVoiceRoute
{
    Clean,
    DriveOnly,
    Generic,
    Full
}
