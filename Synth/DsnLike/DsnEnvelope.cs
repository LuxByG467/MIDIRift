using System.Runtime.CompilerServices;

namespace MIDIRift.Synth.DsnLike;

internal struct DsnEnvelope
{
    public DsnEnvelopeStage Stage;
    public float Level;

    private float _attackStep;
    private float _decayStep;
    private float _releaseStep;
    private float _sustain;

    public void Configure(DsnLikePatch patch, float sampleRate)
        => Configure(patch.AttackSeconds, patch.DecaySeconds, patch.SustainLevel, patch.ReleaseSeconds, sampleRate);

    public void Configure(float attackSeconds, float decaySeconds, float sustainLevel, float releaseSeconds, float sampleRate)
    {
        _sustain = DsnFastMath.Clamp(sustainLevel, 0f, 1f);
        _attackStep = StepForSeconds(attackSeconds, sampleRate);
        _decayStep = StepForSeconds(decaySeconds, sampleRate);
        _releaseStep = StepForSeconds(releaseSeconds, sampleRate);
    }

    public void Reset()
    {
        Stage = DsnEnvelopeStage.Off;
        Level = 0f;
    }

    public void NoteOn()
    {
        Stage = DsnEnvelopeStage.Attack;
        if (_attackStep >= 1f)
        {
            Level = 1f;
            Stage = DsnEnvelopeStage.Decay;
        }
    }

    public void NoteOff()
    {
        if (Stage == DsnEnvelopeStage.Off)
            return;

        Stage = DsnEnvelopeStage.Release;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Next()
    {
        switch (Stage)
        {
            case DsnEnvelopeStage.Attack:
                Level += _attackStep;
                if (Level >= 1f)
                {
                    Level = 1f;
                    Stage = DsnEnvelopeStage.Decay;
                }
                break;

            case DsnEnvelopeStage.Decay:
                Level -= _decayStep;
                if (Level <= _sustain)
                {
                    Level = _sustain;
                    Stage = DsnEnvelopeStage.Sustain;
                }
                break;

            case DsnEnvelopeStage.Release:
                Level -= _releaseStep;
                if (Level <= 0f)
                {
                    Level = 0f;
                    Stage = DsnEnvelopeStage.Off;
                }
                break;
        }

        return Level;
    }


    /// <summary>Advance ADSR state without producing PCM. Used by DSN seek reconstruction.</summary>
    public void AdvanceFrames(int frames)
    {
        if (frames <= 0 || Stage == DsnEnvelopeStage.Off || Stage == DsnEnvelopeStage.Sustain)
            return;

        while (frames > 0 && Stage != DsnEnvelopeStage.Off && Stage != DsnEnvelopeStage.Sustain)
        {
            switch (Stage)
            {
                case DsnEnvelopeStage.Attack:
                {
                    int needed = Math.Max(1, (int)MathF.Ceiling((1f - Level) / _attackStep));
                    int step = Math.Min(frames, needed);
                    Level += _attackStep * step;
                    frames -= step;
                    if (step == needed || Level >= 1f) { Level = 1f; Stage = DsnEnvelopeStage.Decay; }
                    break;
                }
                case DsnEnvelopeStage.Decay:
                {
                    int needed = Math.Max(1, (int)MathF.Ceiling((Level - _sustain) / _decayStep));
                    int step = Math.Min(frames, needed);
                    Level -= _decayStep * step;
                    frames -= step;
                    if (step == needed || Level <= _sustain) { Level = _sustain; Stage = DsnEnvelopeStage.Sustain; }
                    break;
                }
                case DsnEnvelopeStage.Release:
                {
                    int needed = Math.Max(1, (int)MathF.Ceiling(Level / _releaseStep));
                    int step = Math.Min(frames, needed);
                    Level -= _releaseStep * step;
                    frames -= step;
                    if (step == needed || Level <= 0f) { Level = 0f; Stage = DsnEnvelopeStage.Off; }
                    break;
                }
            }
        }
    }

    private static float StepForSeconds(float seconds, float sampleRate)
    {
        if (seconds <= 0f)
            return 1f;
        return DsnFastMath.Clamp(1f / (seconds * sampleRate), 0.0000001f, 1f);
    }
}
