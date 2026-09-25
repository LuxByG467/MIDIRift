using System.Collections.Generic;

namespace MIDIRift;

public class Channel
{
    // ── Identificación ────────────────────────────────────────────────────
    public int MidiChannel { get; set; } = 0;
    public int BankSelect { get; set; } = 0;

    // ── Secuencia ─────────────────────────────────────────────────────────
    public List<MidiStep> Track { get; set; } = new();

    // Snapshot contiguo usado exclusivamente por el hilo de audio. Desktop evita
    // recorrer List<T> en reproducción; Android conserva la lista para el parser
    // y la UI, pero sella una copia una sola vez al construir el engine.
    public MidiStep[] PlaybackTrack { get; private set; } = System.Array.Empty<MidiStep>();
    public int PlaybackTrackCount => PlaybackTrack.Length;

    public void SealPlaybackTrack()
    {
        PlaybackTrack = Track.Count == 0 ? System.Array.Empty<MidiStep>() : Track.ToArray();
    }
    public int Index { get; set; } = 0;
    public double TimeLeft { get; set; } = 0;

    // ── Pool de voces fijo — sin allocations durante playback ─────────────
    // Máximo 6 voces polifónicas (igual que antes), pero ahora son structs
    // en un array fijo: cero GC pressure, cero RemoveAt().
    // VoiceCount es el número de slots activos (0..MaxVoices).
    public const int MaxVoices = 16;
    public Voice[] VoicePool = new Voice[MaxVoices];
    public int VoiceCount = 0;
    private ulong _voiceSerial;

    // Pool separado para percusión one-shot. No comparte ADSR ni NoteOff con
    // las voces tonales, y permite superponer kick/snare/hat sin cortar el golpe anterior.
    public const int MaxDrumVoices = 8;
    public DrumVoice[] DrumPool = new DrumVoice[MaxDrumVoices];
    public int DrumVoiceCount = 0;

    // Compatibilidad: si algún código externo usaba ch.Voices como List,
    // elimina esta propiedad y actualiza esos call-sites.
    // La dejamos comentada para que sea fácil detectarlos.
    // public List<Voice> Voices { get; set; } = new();

    // ── Síntesis ──────────────────────────────────────────────────────────
    public WaveType WaveType { get; set; } = WaveType.Square;
    public float Volume { get; set; } = 0.25f;
    // Multiplicador elegido por el usuario. Se mantiene separado del headroom base del motor.
    public float UserGain = 1f;
    public double Duty { get; set; } = 0.5;
    public double EffectiveDuty { get; private set; } = 0.5;

    // ── ADSR defaults ─────────────────────────────────────────────────────
    public double DefaultAttack { get; set; } = 0.01;
    public double DefaultDecay { get; set; } = 0.04;
    public double DefaultSustain { get; set; } = 0.7;
    public double DefaultRelease { get; set; } = 0.02;

    // ── ADSR activos ──────────────────────────────────────────────────────
    public double Attack { get; set; } = 0.01;
    public double Decay { get; set; } = 0.04;
    public double Sustain { get; set; } = 0.7;
    public double Release { get; set; } = 0.02;

    // ── ADSR pre-calculado en samples ─────────────────────────────────────
    // Se actualiza en AdvanceStep una vez por step, no una vez por muestra.
    public double AttackSamples { get; set; } = 441;
    public double DecaySamples { get; set; } = 1764;
    public double ReleaseSamples { get; set; } = 882;

    // Incrementos ADSR del canal. Se recalculan solo cuando cambia el ADSR y
    // cada nueva voz se limita a copiarlos; evita tres divisiones por NoteOn.
    public double AttackStep { get; private set; } = 1.0 / 441.0;
    public double DecayStep { get; private set; } = (1.0 - 0.7) / 1764.0;
    public double ReleaseStep { get; private set; } = 1.0 / 882.0;

    // Últimos valores normalizados vistos en el stream. Permiten evitar los
    // Math.Pow de ScaleAttack/ScaleRelease cuando un MidiStep repite el mismo CC.
    public float LastAttackParameter { get; set; } = float.NaN;
    public float LastReleaseParameter { get; set; } = float.NaN;
    public float LastDecayParameter { get; set; } = float.NaN;

    // ── CC state ──────────────────────────────────────────────────────────
    public float PitchMult { get; set; } = 1f;
    public float CcVolume { get; set; } = 1f;
    public float CcPan { get; set; } = 0f;

    // Ganancias estéreo precalculadas. El paneo solo cambia al avanzar un
    // MidiStep, así que calcular sin/cos por muestra era trabajo ceremonial.
    public float PanGainL { get; private set; } = 0.70710677f;
    public float PanGainR { get; private set; } = 0.70710677f;
    public float CcExpression { get; set; } = 1f;
    public bool CcSustain { get; set; }
    public float AfterTouch { get; set; } = 0f;
    public float Brightness { get; set; } = 1f;
    public float SoftPedalGain { get; set; } = 1f;
    public float VibratoDepthController { get; set; } = 64f / 127f;
    public float VibratoDelayController { get; set; } = 64f / 127f;
    public long VibratoAgeSamples { get; set; }

    // ── LFO ───────────────────────────────────────────────────────────────
    public float ModDepth { get; set; } = 0f;
    public double LfoPhase { get; set; } = 0.0;
    public double LfoRate { get; set; } = 5.0;

    // ── Portamento ────────────────────────────────────────────────────────
    public bool PortamentoOn { get; set; }
    public float PortamentoTime { get; set; } = 0f;
    public double PortamentoFreq { get; set; } = 0.0;
    public double PortamentoTarget { get; set; } = 0.0;
    public int PortamentoSourceNote { get; set; } = -1;

    // ── DMG Noise ─────────────────────────────────────────────────────────
    public uint Lfsr { get; set; } = 0x7FFF;
    public double NoiseTimer { get; set; } = double.MaxValue;
    public double NoisePeriod { get; set; } = double.MaxValue;
    public bool NoiseShort { get; set; }
    public float NoiseOut { get; set; }

    public bool SostenutoActive { get; set; }
    public bool LegatoOn { get; set; }
    public float SoundVariation { get; set; } = 0f;
    public float ResonanceDutyOffset { get; private set; } = 0f;


    /// <summary>Actualiza el paneo con ley de potencia constante (-3 dB al centro).</summary>
    public void SetPan(float pan)
    {
        CcPan = System.Math.Clamp(pan, -1f, 1f);
        float angle = (CcPan + 1f) * (System.MathF.PI * 0.25f);
        PanGainL = System.MathF.Cos(angle);
        PanGainR = System.MathF.Sin(angle);
    }


    /// <summary>Actualiza CC71 y deja listo el duty efectivo fuera del hot path.</summary>
    public void SetResonance(float resonance)
    {
        float normalized = System.Math.Clamp(resonance, 0f, 1f);
        // Sin CC71 el offset debe ser cero. El control desplaza el duty de
        // forma moderada sin llevar casi toda la escala al clamp.
        ResonanceDutyOffset = normalized * 0.20f;
        EffectiveDuty = System.Math.Clamp(Duty + ResonanceDutyOffset, 0.05, 0.95);
    }

    /// <summary>Recalcula el duty cuando cambia la forma/configuración base.</summary>
    public void RebuildEffectiveDuty()
    {
        EffectiveDuty = System.Math.Clamp(Duty + ResonanceDutyOffset, 0.05, 0.95);
    }

    // ── Helpers de pool ───────────────────────────────────────────────────

    /// <summary>
    /// Agrega una voz al pool. Si está lleno, roba directamente el slot menos
    /// audible. Evita desplazar cinco structs completos por cada NoteOn denso.
    /// </summary>
    public void AddVoice(Voice v)
    {
        v.StartOrder = ++_voiceSerial;

        if (VoiceCount < MaxVoices)
        {
            VoicePool[VoiceCount++] = v;
            return;
        }

        int victim = -1;
        double bestScore = double.MaxValue;
        ulong oldestOrder = ulong.MaxValue;

        // Primera prioridad: slots inactivos o voces cuya tecla ya se soltó.
        // Una voz en Attack con EnvLevel=0 NO es silenciosa: acaba de nacer.
        for (int i = 0; i < MaxVoices; i++)
        {
            ref Voice candidate = ref VoicePool[i];
            if (!candidate.Active)
            {
                victim = i;
                break;
            }

            if (candidate.KeyDown && candidate.EnvState != EnvState.Release &&
                candidate.EnvState != EnvState.Off)
                continue;

            double score = candidate.EnvLevel * Math.Max(0.05f, candidate.Velocity);
            if (score < bestScore ||
                (Math.Abs(score - bestScore) < 1e-12 && candidate.StartOrder < oldestOrder))
            {
                bestScore = score;
                oldestOrder = candidate.StartOrder;
                victim = i;
            }
        }

        // Si todas siguen sostenidas, robar la voz activa más antigua. Esto es
        // mucho menos destructivo que elegir la menor EnvLevel, que castigaba
        // sistemáticamente las notas nuevas durante acordes densos.
        if (victim < 0)
        {
            oldestOrder = ulong.MaxValue;
            for (int i = 0; i < MaxVoices; i++)
            {
                ref Voice candidate = ref VoicePool[i];
                if (candidate.StartOrder < oldestOrder)
                {
                    oldestOrder = candidate.StartOrder;
                    victim = i;
                }
            }
        }

        VoicePool[victim < 0 ? 0 : victim] = v;
    }

    /// <summary>Elimina las voces inactivas compactando el array. Sin allocations.</summary>
    public void CompactVoices()
    {
        int write = 0;
        for (int read = 0; read < VoiceCount; read++)
            if (VoicePool[read].Active)
                VoicePool[write++] = VoicePool[read];
        VoiceCount = write;
    }

    /// <summary>Copia los valores ADSR pre-calculados del canal a una voz.</summary>
    public void StampAdsr(ref Voice v)
    {
        v.AttackSamples = AttackSamples;
        v.DecaySamples = DecaySamples;
        v.ReleaseSamples = ReleaseSamples;
        v.SustainLevel = Sustain;

        // Solo copiar: las divisiones ya se hicieron al cambiar el ADSR del canal.
        v.AttackStep = AttackStep;
        v.DecayStep = DecayStep;
        v.ReleaseStep = ReleaseStep;
    }

    /// <summary>Recalcula AttackSamples/DecaySamples/ReleaseSamples desde los valores actuales.</summary>
    public void RebuildAdsrSamples(int sampleRate)
    {
        AttackSamples = System.Math.Max(1, Attack * sampleRate);
        DecaySamples = System.Math.Max(1, Decay * sampleRate);
        ReleaseSamples = System.Math.Max(1, Release * sampleRate);
        AttackStep = 1.0 / AttackSamples;
        DecayStep = (1.0 - Sustain) / DecaySamples;
        ReleaseStep = 1.0 / ReleaseSamples;
    }

    public void AddDrumVoice(DrumVoice voice)
    {
        if (DrumVoiceCount < MaxDrumVoices)
        {
            DrumPool[DrumVoiceCount++] = voice;
            return;
        }

        // Robar la voz con menor energía restante, no necesariamente la más vieja.
        int quietest = 0;
        float quietestLevel = DrumPool[0].TonalLevel + DrumPool[0].NoiseLevel;
        for (int i = 1; i < MaxDrumVoices; i++)
        {
            float level = DrumPool[i].TonalLevel + DrumPool[i].NoiseLevel;
            if (level < quietestLevel)
            {
                quietest = i;
                quietestLevel = level;
            }
        }
        DrumPool[quietest] = voice;
    }

    public void CompactDrumVoices()
    {
        int write = 0;
        for (int read = 0; read < DrumVoiceCount; read++)
            if (DrumPool[read].Active)
                DrumPool[write++] = DrumPool[read];
        DrumVoiceCount = write;
    }

    public void ClearVoices()
    {
        VoiceCount = 0;
        DrumVoiceCount = 0;
        _voiceSerial = 0;
        // No hace falta limpiar los arrays: los contadores controlan el rango válido.
    }
}