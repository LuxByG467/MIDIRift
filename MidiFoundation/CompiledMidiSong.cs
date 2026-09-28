namespace MIDIRift;

/// <summary>
/// Representación MIDI común e inmutable. Lyra y Classic/Legacy deben partir de
/// este objeto, no volver a parsear el archivo por separado.
/// </summary>
public sealed class CompiledMidiSong
{
    private const int DefaultTempoUsPerQuarter = 500_000;

    public string SourcePath { get; }
    public int TicksPerQuarterNote { get; }
    public CompiledMidiChannel[] Channels { get; }
    public CompiledMidiEvent[] Events { get; }
    public CompiledTempoChange[] TempoMap { get; }
    public CompiledMidiMetaEvent[] Metadata { get; }
    public long LastTick { get; }

    public int MusicalChannelCount { get; }

    public CompiledMidiSong(
        string sourcePath,
        int ticksPerQuarterNote,
        CompiledMidiChannel[] channels,
        CompiledMidiEvent[] events,
        CompiledTempoChange[] tempoMap,
        CompiledMidiMetaEvent[] metadata,
        long lastTick)
    {
        SourcePath = sourcePath ?? string.Empty;
        TicksPerQuarterNote = ticksPerQuarterNote;
        Channels = channels ?? throw new ArgumentNullException(nameof(channels));
        Events = events ?? throw new ArgumentNullException(nameof(events));
        TempoMap = tempoMap ?? throw new ArgumentNullException(nameof(tempoMap));
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        LastTick = Math.Max(0, lastTick);

        int musical = 0;
        for (int i = 0; i < Channels.Length; i++)
            if (Channels[i].IsMusical)
                musical++;
        MusicalChannelCount = musical;
    }

    /// <summary>
    /// Conversión tick→milisegundos compartida. Conserva fracciones de ms y
    /// aplica cada cambio de tempo sólo hacia delante.
    /// </summary>
    public float TicksToMilliseconds(long startTick, long ticks)
    {
        if (ticks <= 0 || TicksPerQuarterNote <= 0)
            return 0f;

        long currentTick = startTick;
        long endTick = startTick + ticks;
        int currentTempo = GetTempoAtTick(startTick);
        double totalMicroseconds = 0.0;

        for (int i = 0; i < TempoMap.Length; i++)
        {
            CompiledTempoChange change = TempoMap[i];
            if (change.Tick <= startTick)
                continue;
            if (change.Tick >= endTick)
                break;

            long segmentTicks = change.Tick - currentTick;
            totalMicroseconds += segmentTicks * (double)currentTempo / TicksPerQuarterNote;
            currentTick = change.Tick;
            currentTempo = change.MicrosecondsPerQuarterNote;
        }

        totalMicroseconds +=
            (endTick - currentTick) * (double)currentTempo / TicksPerQuarterNote;

        return (float)(totalMicroseconds / 1000.0);
    }

    public int GetTempoAtTick(long tick)
    {
        int tempo = DefaultTempoUsPerQuarter;
        for (int i = 0; i < TempoMap.Length; i++)
        {
            CompiledTempoChange change = TempoMap[i];
            if (change.Tick <= tick)
                tempo = change.MicrosecondsPerQuarterNote;
            else
                break;
        }
        return tempo;
    }
}
