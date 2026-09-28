using System.Runtime.CompilerServices;

namespace MIDIRift.Synth.DsnLike;

/// <summary>
/// Dedicated synthetic General MIDI percussion kit for DSN-like (channel 10).
/// Covers the standard GM percussion range 35..81 without samples and without
/// routing percussion through melodic VCO patches.
/// </summary>
public sealed class DsnDrumKit
{
    private const int MaxVoices = 32;
    private readonly DrumVoice[] _voices = new DrumVoice[MaxVoices];
    private readonly float _sampleRate;
    private uint _noise = 0x9E3779B9u;
    private DsnDrumKitSettings _settings;

    public DsnDrumKit(float sampleRate = 44100f, DsnDrumKitSettings? settings = null)
    {
        _sampleRate = sampleRate;
        _settings = settings ?? DsnDrumKitSettingsStore.Load();
    }

    public void ApplySettings(DsnDrumKitSettings settings) => _settings = settings;
    public int ActiveVoiceCount => _voices.Count(v => v.Active);

    public void NoteOn(int gmNote, float velocity)
    {
        int slot = -1;
        float quietest = float.MaxValue;
        for (int i = 0; i < _voices.Length; i++)
        {
            if (!_voices[i].Active) { slot = i; break; }
            if (_voices[i].Amp < quietest) { quietest = _voices[i].Amp; slot = i; }
        }
        _voices[slot] = CreateVoice(gmNote, Math.Clamp(velocity, 0f, 1f));
    }

    public void Reset() => Array.Clear(_voices);

    public void ReleaseAll()
    {
        for (int i = 0; i < _voices.Length; i++)
            if (_voices[i].Active) _voices[i].Decay *= 0.82f;
    }

    public void Render(Span<float> dst)
    {
        for (int vi = 0; vi < _voices.Length; vi++)
        {
            ref DrumVoice v = ref _voices[vi];
            if (!v.Active) continue;

            float phase = v.Phase, phase2 = v.Phase2, amp = v.Amp, inc = v.Inc, hpState = v.HpState;
            for (int i = 0; i < dst.Length && amp > 0.00008f; i++)
            {
                float n, s;
                switch (v.Kind)
                {
                    case DrumKind.Kick:
                        s = Triangle(phase) + (amp > 0.58f ? 0.18f : 0f);
                        inc *= 0.99905f;
                        break;
                    case DrumKind.Snare:
                        n = Noise(); s = n * 0.76f + Triangle(phase) * 0.24f;
                        break;
                    case DrumKind.Clap:
                        n = Noise();
                        s = n * (0.72f + 0.28f * ((phase2 < .18f || (phase2 > .34f && phase2 < .50f)) ? 1f : .35f));
                        break;
                    case DrumKind.Rim:
                        s = (phase < .08f ? 1f : -.22f) + Triangle(phase2) * .18f;
                        break;
                    case DrumKind.Tom:
                        s = Triangle(phase) * .82f + Triangle(phase2) * .18f;
                        inc *= 0.99975f;
                        break;
                    case DrumKind.HatClosed:
                    case DrumKind.HatOpen:
                        n = Noise();
                        s = HighMetal(n * .68f + (phase < .5f ? .32f : -.32f), ref hpState);
                        break;
                    case DrumKind.Cymbal:
                    case DrumKind.Ride:
                        n = Noise();
                        s = HighMetal(n * .48f + SquareCluster(phase, phase2) * .52f, ref hpState);
                        break;
                    case DrumKind.Bell:
                        s = Triangle(phase) * .55f + Triangle(phase2) * .45f;
                        break;
                    case DrumKind.Cowbell:
                        s = (phase < .34f ? 1f : -1f) * .58f + (phase2 < .41f ? .42f : -.42f);
                        break;
                    case DrumKind.Shaker:
                        s = HighMetal(Noise(), ref hpState);
                        break;
                    case DrumKind.Wood:
                        s = Triangle(phase) * .7f + (phase2 < .15f ? .3f : -.12f);
                        break;
                    case DrumKind.Guiro:
                        s = Noise() * .35f + (phase < .22f ? .65f : -.18f);
                        break;
                    case DrumKind.Triangle:
                        s = Triangle(phase) * .8f + Triangle(phase2) * .2f;
                        break;
                    default:
                        s = Noise() * .55f + Triangle(phase) * .45f;
                        break;
                }

                dst[i] += s * amp * v.Velocity * v.Gain;
                phase += inc; phase -= MathF.Floor(phase);
                phase2 += v.Inc2; phase2 -= MathF.Floor(phase2);
                amp *= v.Decay;
            }

            v.Phase = phase; v.Phase2 = phase2; v.Amp = amp; v.Inc = inc; v.HpState = hpState;
            if (amp <= 0.00008f) v.Active = false;
        }
    }

    private DrumVoice CreateVoice(int note, float vel)
    {
        // GM percussion map. Neighboring instruments intentionally share a cheap
        // synthesis family but receive different pitch/decay/gain parameters.
        DrumKind kind;
        float hz, seconds, gain = .40f, ratio = 1.73f;
        switch (note)
        {
            case 35: kind=DrumKind.Kick; hz=58; seconds=.38f; gain=.50f; break; // Acoustic Bass Drum
            case 36: kind=DrumKind.Kick; hz=72; seconds=.32f; gain=.52f; break;
            case 37: kind=DrumKind.Rim; hz=1150; seconds=.07f; gain=.32f; ratio=1.91f; break;
            case 38: kind=DrumKind.Snare; hz=185; seconds=.23f; gain=.43f; break;
            case 39: kind=DrumKind.Clap; hz=310; seconds=.17f; gain=.38f; break;
            case 40: kind=DrumKind.Snare; hz=225; seconds=.19f; gain=.40f; break;
            case 41: kind=DrumKind.Tom; hz=92; seconds=.32f; gain=.45f; break;
            case 42: kind=DrumKind.HatClosed; hz=6900; seconds=.055f; gain=.30f; break;
            case 43: kind=DrumKind.Tom; hz=108; seconds=.30f; gain=.44f; break;
            case 44: kind=DrumKind.HatClosed; hz=6100; seconds=.075f; gain=.27f; break;
            case 45: kind=DrumKind.Tom; hz=128; seconds=.28f; gain=.43f; break;
            case 46: kind=DrumKind.HatOpen; hz=5700; seconds=.36f; gain=.30f; break;
            case 47: kind=DrumKind.Tom; hz=151; seconds=.26f; gain=.42f; break;
            case 48: kind=DrumKind.Tom; hz=178; seconds=.24f; gain=.41f; break;
            case 49: kind=DrumKind.Cymbal; hz=4200; seconds=.82f; gain=.31f; ratio=1.47f; break;
            case 50: kind=DrumKind.Tom; hz=210; seconds=.22f; gain=.40f; break;
            case 51: kind=DrumKind.Ride; hz=3600; seconds=.72f; gain=.27f; ratio=1.61f; break;
            case 52: kind=DrumKind.Cymbal; hz=4700; seconds=.62f; gain=.28f; ratio=1.39f; break;
            case 53: kind=DrumKind.Bell; hz=980; seconds=.55f; gain=.26f; ratio=2.41f; break;
            case 54: kind=DrumKind.Shaker; hz=6500; seconds=.13f; gain=.24f; break; // Tambourine
            case 55: kind=DrumKind.Cymbal; hz=3900; seconds=.70f; gain=.29f; ratio=1.52f; break;
            case 56: kind=DrumKind.Cowbell; hz=560; seconds=.18f; gain=.30f; ratio=1.48f; break;
            case 57: kind=DrumKind.Cymbal; hz=4450; seconds=.92f; gain=.30f; ratio=1.43f; break;
            case 58: kind=DrumKind.Rim; hz=1550; seconds=.09f; gain=.28f; ratio=1.78f; break; // Vibraslap
            case 59: kind=DrumKind.Ride; hz=4100; seconds=.78f; gain=.25f; ratio=1.67f; break;
            case 60: kind=DrumKind.Wood; hz=640; seconds=.13f; gain=.31f; ratio=1.34f; break; // High Bongo
            case 61: kind=DrumKind.Wood; hz=480; seconds=.15f; gain=.32f; ratio=1.31f; break;
            case 62: kind=DrumKind.Tom; hz=240; seconds=.16f; gain=.34f; break; // Mute Hi Conga
            case 63: kind=DrumKind.Tom; hz=205; seconds=.24f; gain=.36f; break;
            case 64: kind=DrumKind.Tom; hz=165; seconds=.27f; gain=.37f; break;
            case 65: kind=DrumKind.Tom; hz=285; seconds=.17f; gain=.32f; break; // High Timbale
            case 66: kind=DrumKind.Tom; hz=225; seconds=.20f; gain=.33f; break;
            case 67: kind=DrumKind.Bell; hz=780; seconds=.22f; gain=.28f; ratio=1.82f; break; // Agogo
            case 68: kind=DrumKind.Bell; hz=620; seconds=.24f; gain=.29f; ratio=1.79f; break;
            case 69: kind=DrumKind.Shaker; hz=7200; seconds=.12f; gain=.22f; break; // Cabasa
            case 70: kind=DrumKind.Shaker; hz=6100; seconds=.18f; gain=.23f; break; // Maracas
            case 71: kind=DrumKind.Rim; hz=1900; seconds=.045f; gain=.24f; ratio=1.63f; break; // Short whistle
            case 72: kind=DrumKind.Rim; hz=1450; seconds=.24f; gain=.24f; ratio=1.61f; break; // Long whistle
            case 73: kind=DrumKind.Guiro; hz=1250; seconds=.09f; gain=.23f; ratio=1.27f; break;
            case 74: kind=DrumKind.Guiro; hz=920; seconds=.34f; gain=.24f; ratio=1.23f; break;
            case 75: kind=DrumKind.Wood; hz=1120; seconds=.055f; gain=.25f; ratio=1.53f; break; // Claves
            case 76: kind=DrumKind.Wood; hz=820; seconds=.10f; gain=.28f; ratio=1.42f; break;
            case 77: kind=DrumKind.Wood; hz=610; seconds=.12f; gain=.29f; ratio=1.38f; break;
            case 78: kind=DrumKind.Shaker; hz=5200; seconds=.20f; gain=.22f; break; // Cuica approximation
            case 79: kind=DrumKind.Shaker; hz=4300; seconds=.30f; gain=.23f; break;
            case 80: kind=DrumKind.Triangle; hz=1760; seconds=.08f; gain=.22f; ratio=1.50f; break;
            case 81: kind=DrumKind.Triangle; hz=1760; seconds=.65f; gain=.21f; ratio=1.50f; break;
            default: kind=DrumKind.Generic; hz=220 + note*3; seconds=.14f; gain=.30f; break;
        }

        var edit = _settings.Get(Math.Clamp(note, 35, 81));
        float tuneRatio = MathF.Pow(2f, Math.Clamp(edit.TuneSemitones, -24f, 24f) / 12f);
        hz *= tuneRatio;
        seconds *= Math.Clamp(edit.DecayScale, .20f, 4f);
        gain *= Math.Clamp(edit.GainScale, 0f, 2f);

        // Tone is intentionally family-agnostic for v0.2.8: it moves the
        // secondary oscillator/metal ratio without replacing each drum's
        // synthesis recipe. 0.5 = factory identity.
        float tone = Math.Clamp(edit.Tone, 0f, 1f);
        ratio *= .72f + tone * .56f;

        float decay = MathF.Exp(-6.9f / Math.Max(1f, seconds * _sampleRate));
        return new DrumVoice
        {
            Active=true, Kind=kind, Velocity=vel, Amp=1f, Decay=decay, Gain=gain,
            Inc=hz/_sampleRate, Inc2=hz*ratio/_sampleRate
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Triangle(float phase) => 1f - 4f * MathF.Abs(phase - .5f);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float SquareCluster(float a, float b)
        => (a < .37f ? 1f : -1f) * .55f + (b < .43f ? .45f : -.45f);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float HighMetal(float raw, ref float state)
    {
        float s = raw - state * .82f;
        state = raw;
        return s;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float Noise()
    {
        uint x=_noise; x^=x<<13; x^=x>>17; x^=x<<5; _noise=x;
        return (x*(1f/uint.MaxValue))*2f-1f;
    }

    private enum DrumKind : byte
    { Generic, Kick, Snare, Clap, Rim, Tom, HatClosed, HatOpen, Cymbal, Ride, Bell, Cowbell, Shaker, Wood, Guiro, Triangle }

    private struct DrumVoice
    {
        public bool Active;
        public DrumKind Kind;
        public float Velocity, Amp, Decay, Gain, Phase, Phase2, Inc, Inc2, HpState;
    }
}
