using System.Runtime.CompilerServices;

namespace MIDIRift;

/// <summary>
/// One time-slice of MIDI data for a single channel.
///
/// Cambiado de class a struct con arrays inline de tamaño fijo:
///   - Cero heap allocation por step durante playback
///   - NoteCount controla cuántas notas son válidas (0..MaxNotes)
///   - Reemplaza List<float> Freqs/Velocities/PolyAfterTouch
///
/// MaxNotes = 6 coincide con Channel.MaxVoices — no tiene sentido
/// tener más notas por step que voces puede sostener el engine.
/// </summary>
public struct MidiStep
{
    public const int MaxNotes = 16;

    [InlineArray(MaxNotes)]
    private struct FloatBuffer { private float _element0; }

    [InlineArray(MaxNotes)]
    private struct IntBuffer { private int _element0; }

    // ── Notas y eventos inline (sin arrays administrados ni allocations) ──
    public int NoteCount;
    private FloatBuffer _freqs;
    private FloatBuffer _velocities;
    private FloatBuffer _polyAfterTouch;

    // Eventos reales del borde inicial de este step. La capacidad debe coincidir
    // con el pool polifónico del canal; de lo contrario se pierden NoteOff en
    // acordes densos y aparecen voces sostenidas fantasma.
    public int NoteOnCount;
    private IntBuffer _onNotes;
    private FloatBuffer _onVelocities;
    public int NoteOffCount;
    private IntBuffer _offNotes;

    // Compatibilidad con el TrackerPanel, que sólo representa seis notas.
    public float Freq0 { readonly get => _freqs[0]; set => _freqs[0] = value; }
    public float Freq1 { readonly get => _freqs[1]; set => _freqs[1] = value; }
    public float Freq2 { readonly get => _freqs[2]; set => _freqs[2] = value; }
    public float Freq3 { readonly get => _freqs[3]; set => _freqs[3] = value; }
    public float Freq4 { readonly get => _freqs[4]; set => _freqs[4] = value; }
    public float Freq5 { readonly get => _freqs[5]; set => _freqs[5] = value; }

    // ── Timing ────────────────────────────────────────────────────────────
    public float DurationMs;

    // ── Pitch / dynamics ──────────────────────────────────────────────────
    public float PitchMult;   // default 1f
    public float Volume;      // default 1f
    public float Pan;         // default 0f   (-1..+1)
    public float Expression;  // default 1f
    public float Brightness;  // default 1f

    // ── Modulation ────────────────────────────────────────────────────────
    public float Modulation;  // CC1  LFO depth, 0..1
    public float LfoRate;     // CC76 Hz,        default 5f
    public float AfterTouch;  // channel AT,     0..1

    // ── ADSR overrides ────────────────────────────────────────────────────
    public float Attack;      // CC73, -1 = usar default del canal
    public float Release;     // CC72, -1 = usar default del canal
    public float Decay;       // CC75, -1 = usar default del canal
    public float VibratoDepth; // CC77, 64/127 = neutral
    public float VibratoDelay; // CC78, 64/127 = neutral
    public float SoftPedal;    // CC67, 0/1

    // ── Timbre ────────────────────────────────────────────────────────────
    public float SoundVariation;  // CC70 0..1
    public float Resonance;       // CC71 0..1

    // ── Articulación (flags empaquetados en un byte para ahorrar memoria) ─
    private byte _flags;
    private const byte FlagSustain = 1 << 0;
    private const byte FlagSostenuto = 1 << 1;
    private const byte FlagLegato = 1 << 2;
    private const byte FlagPortamento = 1 << 3;

    public bool Sustain
    {
        readonly get => (_flags & FlagSustain) != 0;
        set => _flags = value ? (byte)(_flags | FlagSustain) : (byte)(_flags & ~FlagSustain);
    }
    public bool Sostenuto
    {
        readonly get => (_flags & FlagSostenuto) != 0;
        set => _flags = value ? (byte)(_flags | FlagSostenuto) : (byte)(_flags & ~FlagSostenuto);
    }
    public bool Legato
    {
        readonly get => (_flags & FlagLegato) != 0;
        set => _flags = value ? (byte)(_flags | FlagLegato) : (byte)(_flags & ~FlagLegato);
    }
    public bool PortamentoOn
    {
        readonly get => (_flags & FlagPortamento) != 0;
        set => _flags = value ? (byte)(_flags | FlagPortamento) : (byte)(_flags & ~FlagPortamento);
    }

    // ── Portamento ────────────────────────────────────────────────────────
    public float PortamentoTime;  // CC5, 0..2 s
    public int PortamentoSourceNote; // CC84, -1 = none; consumed by next NoteOn

    // ── Factory — valores default idénticos al MidiStep original ─────────
    public static MidiStep Default() => new()
    {
        NoteCount = 0,
        NoteOnCount = 0,
        NoteOffCount = 0,
        DurationMs = 0f,
        PitchMult = 1f,
        Volume = 1f,
        Pan = 0f,
        Expression = 1f,
        Brightness = 1f,
        Modulation = 0f,
        LfoRate = 5f,
        AfterTouch = 0f,
        Attack = -1f,
        Release = -1f,
        Decay = -1f,
        VibratoDepth = 64f / 127f,
        VibratoDelay = 64f / 127f,
        SoftPedal = 0f,
        SoundVariation = 0f,
        Resonance = 0f,
        PortamentoTime = 0f,
        PortamentoSourceNote = -1,
        _flags = 0,
    };

    // ── Acceso indexado a notas/eventos ──────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly float GetFreq(int i) => _freqs[i];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetFreq(int i, float v) => _freqs[i] = v;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly float GetVelocity(int i) => _velocities[i];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetVelocity(int i, float v) => _velocities[i] = v;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly float GetPolyAT(int i) => _polyAfterTouch[i];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetPolyAT(int i, float v) => _polyAfterTouch[i] = v;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AddNote(float freq, float velocity = 1f, float polyAT = 0f)
    {
        if (NoteCount >= MaxNotes) return false;
        int index = NoteCount++;
        _freqs[index] = freq;
        _velocities[index] = velocity;
        _polyAfterTouch[index] = polyAT;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly int GetOnNote(int i) => _onNotes[i];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly float GetOnVelocity(int i) => _onVelocities[i];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AddNoteOn(int note, float velocity)
    {
        if (NoteOnCount >= MaxNotes) return false;
        int index = NoteOnCount++;
        _onNotes[index] = note;
        _onVelocities[index] = velocity;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly int GetOffNote(int i) => _offNotes[i];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AddNoteOff(int note)
    {
        if (NoteOffCount >= MaxNotes) return false;
        _offNotes[NoteOffCount++] = note;
        return true;
    }

    // Compatibilidad de lectura para código que itera notas
    public readonly float Velocity => NoteCount > 0 ? _velocities[0] : 1f;
}