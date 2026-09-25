namespace MIDIRift;

/// <summary>
/// Struct en lugar de class: cero heap allocation por voz.
/// El array de voces vive en el Channel y se reutiliza.
/// </summary>
public struct Voice
{
    public int MidiNote;
    public bool KeyDown;
    public double Phase;
    public double Freq;
    public double EnvLevel;
    public EnvState EnvState;
    public bool Active;
    public float Velocity;
    public float PolyAfterTouch;
    public bool SostenutoHeld;

    // Orden monotónico de creación dentro del canal. Se usa únicamente para
    // desempatar el voice stealing sin confundir una voz recién nacida (Attack,
    // EnvLevel=0) con una voz vieja realmente prescindible.
    public ulong StartOrder;

    // ── Portamento precalculado ───────────────────────────────────────────
    // El multiplicador exponencial se calcula cuando cambia la nota objetivo,
    // no dentro del hot path por cada muestra.
    public double PortamentoTarget;
    public double PortamentoMultiplier;
    public bool PortamentoActive;

    // ── ADSR pre-calculado en samples (se fija en AdvanceStep, no por muestra) ──
    // Evita recalcular Attack*SampleRate en cada una de las 44100 muestras/seg.
    public double AttackSamples;
    public double DecaySamples;
    public double ReleaseSamples;
    public double SustainLevel;

    // Incrementos ADSR precalculados para el hot path. Evitan millones de
    // divisiones por segundo cuando hay muchos canales y voces activas.
    public double AttackStep;
    public double DecayStep;
    public double ReleaseStep;

    public static Voice Create(int midiNote, double freq, double startFreq, float velocity, float polyAT) => new()
    {
        MidiNote = midiNote,
        KeyDown = true,
        Phase = 0,
        Freq = startFreq,
        EnvLevel = 0,
        EnvState = EnvState.Attack,
        Active = true,
        Velocity = velocity,
        PolyAfterTouch = polyAT,
        SostenutoHeld = false,
        PortamentoTarget = freq,
        PortamentoMultiplier = 1.0,
        PortamentoActive = false,
        // Los valores ADSR los completa AdvanceStep vía CopyAdsrTo()
    };
}