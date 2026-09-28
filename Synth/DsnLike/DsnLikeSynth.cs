namespace MIDIRift.Synth.DsnLike;

/// <summary>
/// Small polyphonic host for the v0.1 DSP draft. It is deliberately independent
/// from MIDI playback and Android audio backends so it can be benchmarked first.
/// </summary>
public sealed class DsnLikeSynth
{
    private readonly DsnLikeVoice[] _voices;
    private DsnLikePatch _patch;
    private long _startSequence;
    private int? _lastNote;
    private float _pitchBendRatio = 1f;
    private float _modulation;
    private float _brightness = 64f / 127f;
    private float _resonance = 64f / 127f;
    private float _attackTime = 64f / 127f;
    private float _decayTime = 64f / 127f;
    private float _releaseTime = 64f / 127f;
    private float _vibratoRate = 64f / 127f;
    private float _vibratoDepth = 64f / 127f;
    private float _vibratoDelay = 64f / 127f;
    private bool _sostenuto;
    private bool _softPedal;

    public DsnLikeSynth(int polyphony = 32, float sampleRate = 44100f, DsnLikePatch? patch = null)
    {
        if (polyphony <= 0)
            throw new ArgumentOutOfRangeException(nameof(polyphony));

        _patch = patch ?? DsnLikePatch.Default;
        _voices = new DsnLikeVoice[polyphony];

        for (int i = 0; i < _voices.Length; i++)
        {
            _voices[i] = new DsnLikeVoice(sampleRate, (uint)(i + 1));
            _voices[i].ApplyPatch(_patch);
        }
    }

    public int Polyphony => _voices.Length;
    public int ActiveVoiceCount => _voices.Count(v => v.IsActive);
    public DsnLikePatch Patch => _patch;

    /// <summary>Apply a patch immediately, including currently sounding voices. Used by the live editor.</summary>
    public void ApplyPatch(DsnLikePatch patch)
    {
        _patch = patch ?? throw new ArgumentNullException(nameof(patch));
        foreach (var voice in _voices)
            voice.ApplyPatch(_patch);
    }

    /// <summary>Change the program for future NoteOns without mutating notes already sounding.</summary>
    public void SetPatchForNewVoices(DsnLikePatch patch)
        => _patch = patch ?? throw new ArgumentNullException(nameof(patch));

    public void NoteOn(int note, float velocity = 1f, bool portamento = false, float portamentoSeconds = 0f, int? portamentoSourceNote = null)
    {
        // Fixed pool, deterministic stealing. Prefer a free slot, then a voice
        // already in Release, then the oldest active voice. Never grow the pool.
        DsnLikeVoice? selected = null;
        DsnLikeVoice? oldestRelease = null;
        DsnLikeVoice? oldestActive = null;
        long oldestReleaseSequence = long.MaxValue;
        long oldestActiveSequence = long.MaxValue;

        foreach (var voice in _voices)
        {
            if (!voice.IsActive)
            {
                selected = voice;
                break;
            }

            if (voice.EnvelopeStage == DsnEnvelopeStage.Release &&
                voice.StartSequence < oldestReleaseSequence)
            {
                oldestRelease = voice;
                oldestReleaseSequence = voice.StartSequence;
            }

            if (voice.StartSequence < oldestActiveSequence)
            {
                oldestActive = voice;
                oldestActiveSequence = voice.StartSequence;
            }
        }

        selected ??= oldestRelease ?? oldestActive ?? _voices[0];
        selected.ApplyPatch(_patch);
        int? glideFrom = portamento ? (portamentoSourceNote ?? _lastNote) : null;
        selected.NoteOn(note, velocity, ++_startSequence, glideFrom, portamentoSeconds);
        selected.SetPitchBendRatio(_pitchBendRatio);
        selected.SetModulation(_modulation);
        selected.SetBrightness(_brightness);
        selected.SetResonance(_resonance);
        selected.SetAttackTime(_attackTime);
        selected.SetDecayTime(_decayTime);
        selected.SetReleaseTime(_releaseTime);
        selected.SetVibratoRate(_vibratoRate);
        selected.SetVibratoDepth(_vibratoDepth);
        selected.SetVibratoDelay(_vibratoDelay);
        selected.SetSoftPedal(_softPedal);
        _lastNote = note;
    }

    public void NoteOff(int note, bool sustain = false)
    {
        // D6.2 semantics: duplicate NoteOns of the same note are paired FIFO.
        // Critically, voices already in Release / sustain-pending are NOT eligible
        // for another NoteOff. The old implementation could repeatedly select the
        // same releasing duplicate and strand newer voices forever.
        DsnLikeVoice? selected = null;
        long oldestSequence = long.MaxValue;

        foreach (var voice in _voices)
        {
            if (!voice.IsActive || voice.Note != note || !voice.KeyHeld)
                continue;

            if (voice.StartSequence < oldestSequence)
            {
                oldestSequence = voice.StartSequence;
                selected = voice;
            }
        }

        selected?.HandleNoteOff(sustain);
    }

    public void ReleasePending()
    {
        foreach (var voice in _voices)
            voice.ReleaseFromSustain();
    }

    public void SetSostenuto(bool enabled, bool sustainDown)
    {
        if (enabled == _sostenuto)
            return;

        _sostenuto = enabled;
        if (enabled)
        {
            foreach (var voice in _voices)
                voice.CaptureSostenuto();
        }
        else
        {
            foreach (var voice in _voices)
                voice.ReleaseFromSostenuto(sustainDown);
        }
    }

    public void SetSoftPedal(bool enabled)
    {
        _softPedal = enabled;
        foreach (var voice in _voices)
            if (voice.IsActive) voice.SetSoftPedal(enabled);
    }

    public void ReleaseAll()
    {
        foreach (var voice in _voices)
            voice.BeginRelease();
    }

    public void AllSoundOff()
    {
        // MIDI CC120 is a panic/hard-silence command, unlike CC123 which
        // enters the normal Release stage.
        foreach (var voice in _voices)
            voice.Reset();
        _lastNote = null;
    }

    public void SetPitchBendRatio(float ratio)
    {
        _pitchBendRatio = DsnFastMath.Clamp(ratio, 0.25f, 4f);
        foreach (var voice in _voices) if (voice.IsActive) voice.SetPitchBendRatio(_pitchBendRatio);
    }

    public void SetModulation(float value)
    {
        _modulation = DsnFastMath.Clamp(value, 0f, 1f);
        foreach (var voice in _voices) if (voice.IsActive) voice.SetModulation(_modulation);
    }

    public void SetBrightness(float value)
    {
        _brightness = DsnFastMath.Clamp(value, 0f, 1f);
        foreach (var voice in _voices) if (voice.IsActive) voice.SetBrightness(_brightness);
    }

    public void SetResonance(float value)
    {
        _resonance = DsnFastMath.Clamp(value, 0f, 1f);
        foreach (var voice in _voices) if (voice.IsActive) voice.SetResonance(_resonance);
    }

    public void SetAttackTime(float value)
    {
        _attackTime = DsnFastMath.Clamp(value, 0f, 1f);
        foreach (var voice in _voices) if (voice.IsActive) voice.SetAttackTime(_attackTime);
    }

    public void SetDecayTime(float value)
    {
        _decayTime = DsnFastMath.Clamp(value, 0f, 1f);
        foreach (var voice in _voices) if (voice.IsActive) voice.SetDecayTime(_decayTime);
    }

    public void SetReleaseTime(float value)
    {
        _releaseTime = DsnFastMath.Clamp(value, 0f, 1f);
        foreach (var voice in _voices) if (voice.IsActive) voice.SetReleaseTime(_releaseTime);
    }

    public void SetVibratoRate(float value)
    {
        _vibratoRate = DsnFastMath.Clamp(value, 0f, 1f);
        foreach (var voice in _voices) if (voice.IsActive) voice.SetVibratoRate(_vibratoRate);
    }

    public void SetVibratoDepth(float value)
    {
        _vibratoDepth = DsnFastMath.Clamp(value, 0f, 1f);
        foreach (var voice in _voices) if (voice.IsActive) voice.SetVibratoDepth(_vibratoDepth);
    }

    public void SetVibratoDelay(float value)
    {
        _vibratoDelay = DsnFastMath.Clamp(value, 0f, 1f);
        foreach (var voice in _voices) if (voice.IsActive) voice.SetVibratoDelay(_vibratoDelay);
    }

    public void ResetMidiControllers()
    {
        _pitchBendRatio = 1f;
        _modulation = 0f;
        _brightness = 64f / 127f;
        _resonance = 64f / 127f;
        _attackTime = 64f / 127f;
        _decayTime = 64f / 127f;
        _releaseTime = 64f / 127f;
        _vibratoRate = 64f / 127f;
        _vibratoDepth = 64f / 127f;
        _vibratoDelay = 64f / 127f;
        _vibratoDelay = 64f / 127f;
        _sostenuto = false;
        _softPedal = false;

        foreach (var voice in _voices)
        {
            if (!voice.IsActive) continue;
            voice.SetPitchBendRatio(1f);
            voice.SetModulation(0f);
            voice.SetBrightness(_brightness);
            voice.SetResonance(_resonance);
            voice.SetAttackTime(_attackTime);
            voice.SetDecayTime(_decayTime);
            voice.SetReleaseTime(_releaseTime);
            voice.SetVibratoRate(_vibratoRate);
            voice.SetVibratoDepth(_vibratoDepth);
            voice.SetVibratoDelay(_vibratoDelay);
            voice.ReleaseFromSostenuto(sustainDown: false);
            voice.SetSoftPedal(false);
        }
    }

    public void AdvanceStateOnly(int frames)
    {
        foreach (var voice in _voices) if (voice.IsActive) voice.AdvanceStateOnly(frames);
    }

    public void Reset()
    {
        foreach (var voice in _voices)
            voice.Reset();
        _startSequence = 0;
        _lastNote = null;
        _pitchBendRatio = 1f;
        _modulation = 0f;
        _brightness = 64f / 127f;
        _resonance = 64f / 127f;
        _attackTime = 64f / 127f;
        _decayTime = 64f / 127f;
        _releaseTime = 64f / 127f;
        _vibratoRate = 64f / 127f;
        _vibratoDepth = 64f / 127f;
    }

    public void Render(Span<float> monoDestination)
    {
        monoDestination.Clear();
        foreach (var voice in _voices)
        {
            if (voice.IsActive)
                voice.Render(monoDestination);
        }
    }
}
