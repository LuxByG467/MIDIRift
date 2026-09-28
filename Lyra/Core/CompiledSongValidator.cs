namespace MIDIRift.CleanRoom.Features.Midi;

public static class CompiledSongValidator
{
    public static void Validate(CompiledSong song)
    {
        ArgumentNullException.ThrowIfNull(song);

        if (song.SampleRate <= 0)
            throw new InvalidDataException("CompiledSong.SampleRate debe ser mayor que cero.");
        if (song.TicksPerQuarterNote <= 0)
            throw new InvalidDataException("CompiledSong.TicksPerQuarterNote debe ser mayor que cero.");
        if (song.TempoMap.Count == 0 || song.TempoMap[0].Tick != 0)
            throw new InvalidDataException("El mapa de tempo debe comenzar en el tick cero.");
        if (song.DurationSamples < 0 || song.DurationSeconds < 0)
            throw new InvalidDataException("La duración compilada no puede ser negativa.");
        if (song.Channels.Count == 0)
            throw new InvalidDataException("El archivo MIDI no contiene canales con eventos NoteOn válidos.");
        if (!song.Events.Any(compiledEvent => compiledEvent.Kind == CompiledMidiEventKind.NoteOn))
            throw new InvalidDataException("El archivo MIDI no contiene notas reproducibles.");

        var channelIds = new HashSet<MidiChannelId>();
        for (int runtimeIndex = 0; runtimeIndex < song.Channels.Count; runtimeIndex++)
        {
            CompiledChannel channel = song.Channels[runtimeIndex];
            if (channel.RuntimeIndex != runtimeIndex)
                throw new InvalidDataException($"Índice runtime no compacto: esperado {runtimeIndex}, recibido {channel.RuntimeIndex}.");
            if (channel.Id.Port < 0)
                throw new InvalidDataException($"Puerto MIDI inválido: {channel.Id.Port}.");
            if (channel.Id.Channel is < 0 or >= CompiledSong.MidiChannelsPerPort)
                throw new InvalidDataException($"Canal MIDI inválido: {channel.Id.Channel}.");
            if (!channel.HasNotes)
                throw new InvalidDataException($"El canal runtime {runtimeIndex} no contiene NoteOn válidos.");
            if (!channelIds.Add(channel.Id))
                throw new InvalidDataException($"Canal lógico duplicado: {channel.Id}.");
        }

        long previousSample = -1;
        int previousPriority = -1;
        int previousSourceOrder = -1;
        foreach (CompiledMidiEvent compiledEvent in song.Events)
        {
            if (compiledEvent.ChannelIndex < 0 || compiledEvent.ChannelIndex >= song.Channels.Count)
                throw new InvalidDataException($"Índice runtime de evento inválido: {compiledEvent.ChannelIndex}.");
            if (compiledEvent.SamplePosition < previousSample)
                throw new InvalidDataException("Los eventos compilados no están ordenados por muestra.");

            int currentPriority = EventPriority(compiledEvent.Kind);
            if (compiledEvent.SamplePosition == previousSample)
            {
                if (currentPriority < previousPriority)
                    throw new InvalidDataException("Los eventos simultáneos no respetan su prioridad canónica.");
                if (currentPriority == previousPriority && compiledEvent.SourceOrder < previousSourceOrder)
                    throw new InvalidDataException("Los eventos simultáneos perdieron el orden de origen.");
            }

            previousSample = compiledEvent.SamplePosition;
            previousPriority = currentPriority;
            previousSourceOrder = compiledEvent.SourceOrder;
        }

        ValidateEventBatches(song);
    }

    private static void ValidateEventBatches(CompiledSong song)
    {
        int expectedStartIndex = 0;
        long previousBatchSample = -1;

        for (int batchIndex = 0; batchIndex < song.EventBatches.Count; batchIndex++)
        {
            CompiledEventBatch batch = song.EventBatches[batchIndex];
            if (batch.Count <= 0)
                throw new InvalidDataException($"El lote de eventos {batchIndex} está vacío.");
            if (batch.StartIndex != expectedStartIndex)
                throw new InvalidDataException($"El lote {batchIndex} no es contiguo: esperado {expectedStartIndex}, recibido {batch.StartIndex}.");
            if (batch.SamplePosition < previousBatchSample)
                throw new InvalidDataException("Los lotes de eventos no están ordenados por muestra.");
            if (batch.StartIndex < 0 || batch.StartIndex + batch.Count > song.Events.Count)
                throw new InvalidDataException($"El lote {batchIndex} apunta fuera del arreglo de eventos.");

            for (int eventOffset = 0; eventOffset < batch.Count; eventOffset++)
            {
                CompiledMidiEvent compiledEvent = song.Events[batch.StartIndex + eventOffset];
                if (compiledEvent.SamplePosition != batch.SamplePosition)
                    throw new InvalidDataException($"El lote {batchIndex} mezcla eventos de muestras distintas.");
            }

            expectedStartIndex += batch.Count;
            previousBatchSample = batch.SamplePosition;
        }

        if (expectedStartIndex != song.Events.Count)
            throw new InvalidDataException("Los lotes de eventos no cubren todos los eventos compilados.");
    }

    private static int EventPriority(CompiledMidiEventKind kind) => kind switch
    {
        CompiledMidiEventKind.AllNotesOff => 0,
        CompiledMidiEventKind.NoteOff => 1,
        CompiledMidiEventKind.Program => 2,
        CompiledMidiEventKind.Volume => 2,
        CompiledMidiEventKind.Pan => 2,
        CompiledMidiEventKind.Expression => 2,
        CompiledMidiEventKind.Sustain => 2,
        CompiledMidiEventKind.PitchBend => 2,
        CompiledMidiEventKind.Modulation => 2,
        CompiledMidiEventKind.PortamentoTime => 2,
        CompiledMidiEventKind.PortamentoSwitch => 2,
        CompiledMidiEventKind.LegatoSwitch => 2,
        CompiledMidiEventKind.NoteOn => 3,
        _ => 4
    };
}
