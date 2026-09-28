using NAudio.Midi;

namespace MIDIRift;

/// <summary>
/// Único parser de archivo MIDI. Traduce NAudio.Midi a datos neutrales que no
/// dependen de ningún sintetizador.
/// </summary>
public static class MidiCompiler
{
    public static CompiledMidiSong Compile(string midiPath)
    {
        if (string.IsNullOrWhiteSpace(midiPath))
            throw new ArgumentException("La ruta MIDI no puede estar vacía.", nameof(midiPath));

        var midiFile = new MidiFile(midiPath, false);
        int ticksPerQuarter = midiFile.DeltaTicksPerQuarterNote;
        if (ticksPerQuarter <= 0)
            throw new InvalidDataException("El MIDI no declara una resolución PPQ válida.");

        var eventsByChannel = new Dictionary<int, List<CompiledMidiEvent>>();
        var allChannelEvents = new List<CompiledMidiEvent>();
        var firstPatchByChannel = new Dictionary<int, int>();
        var channelsWithNotes = new HashSet<int>();
        var tempoByTick = new Dictionary<long, int>();
        var metadata = new List<CompiledMidiMetaEvent>();

        int sequence = 0;
        long lastTick = 0;

        foreach (var physicalTrack in midiFile.Events)
        {
            foreach (var midiEvent in physicalTrack)
            {
                long tick = Math.Max(0, midiEvent.AbsoluteTime);
                if (tick > lastTick) lastTick = tick;

                if (midiEvent is TempoEvent tempoEvent)
                {
                    tempoByTick[tick] = tempoEvent.MicrosecondsPerQuarterNote;
                    continue;
                }

                bool channelEventAdded = TryCompileChannelEvent(
                    midiEvent,
                    sequence++,
                    eventsByChannel,
                    allChannelEvents,
                    firstPatchByChannel,
                    channelsWithNotes);

                if (!channelEventAdded)
                {
                    metadata.Add(new CompiledMidiMetaEvent(
                        tick,
                        midiEvent.GetType().Name,
                        midiEvent.ToString() ?? string.Empty));
                }
            }
        }

        if (!tempoByTick.ContainsKey(0))
            tempoByTick[0] = 500_000;

        CompiledTempoChange[] tempoMap = tempoByTick
            .OrderBy(pair => pair.Key)
            .Select(pair => new CompiledTempoChange(pair.Key, pair.Value))
            .ToArray();

        var channelNumbers = eventsByChannel.Keys
            .Union(channelsWithNotes)
            .OrderBy(channel => channel)
            .ToArray();

        var channels = new CompiledMidiChannel[channelNumbers.Length];
        for (int i = 0; i < channelNumbers.Length; i++)
        {
            int midiChannel = channelNumbers[i];
            eventsByChannel.TryGetValue(midiChannel, out List<CompiledMidiEvent>? sourceEvents);
            sourceEvents ??= new List<CompiledMidiEvent>();

            CompiledMidiEvent[] ordered = sourceEvents
                .OrderBy(e => e.Tick)
                .ThenBy(e => e.Sequence)
                .ToArray();

            int initialPatch = firstPatchByChannel.TryGetValue(midiChannel, out int patch)
                ? patch
                : 0;

            channels[i] = new CompiledMidiChannel(
                midiChannel,
                initialPatch,
                channelsWithNotes.Contains(midiChannel),
                ordered);
        }

        return new CompiledMidiSong(
            midiPath,
            ticksPerQuarter,
            channels,
            allChannelEvents.OrderBy(e => e.Tick).ThenBy(e => e.Sequence).ToArray(),
            tempoMap,
            metadata.ToArray(),
            lastTick);
    }

    private static bool TryCompileChannelEvent(
        MidiEvent midiEvent,
        int sequence,
        Dictionary<int, List<CompiledMidiEvent>> eventsByChannel,
        List<CompiledMidiEvent> allChannelEvents,
        Dictionary<int, int> firstPatchByChannel,
        HashSet<int> channelsWithNotes)
    {
        CompiledMidiEventKind kind;
        int channel;
        int data1;
        int data2;

        // Las variables se inicializan antes del switch y sólo se asignan en
        // ramas completas. Evita declaraciones/initializers ambiguos dentro de
        // switch que ya nos han dado errores al portar entre runtimes.
        kind = default;
        channel = -1;
        data1 = 0;
        data2 = 0;

        if (midiEvent is NoteOnEvent noteOn)
        {
            channel = noteOn.Channel;
            data1 = noteOn.NoteNumber;
            data2 = noteOn.Velocity;
            if (noteOn.Velocity > 0)
            {
                kind = CompiledMidiEventKind.NoteOn;
                channelsWithNotes.Add(channel);
            }
            else
            {
                kind = CompiledMidiEventKind.NoteOff;
            }
        }
        else if (midiEvent is NoteEvent noteEvent &&
                 noteEvent.CommandCode == MidiCommandCode.NoteOff)
        {
            channel = noteEvent.Channel;
            data1 = noteEvent.NoteNumber;
            data2 = noteEvent.Velocity;
            kind = CompiledMidiEventKind.NoteOff;
        }
        else if (midiEvent is NoteEvent polyAfterTouch &&
                 polyAfterTouch.CommandCode == MidiCommandCode.KeyAfterTouch)
        {
            channel = polyAfterTouch.Channel;
            data1 = polyAfterTouch.NoteNumber;
            data2 = polyAfterTouch.Velocity;
            kind = CompiledMidiEventKind.PolyAfterTouch;
        }
        else if (midiEvent is ControlChangeEvent cc)
        {
            channel = cc.Channel;
            data1 = (int)cc.Controller;
            data2 = cc.ControllerValue;
            kind = CompiledMidiEventKind.ControlChange;
        }
        else if (midiEvent is PitchWheelChangeEvent pitchWheel)
        {
            channel = pitchWheel.Channel;
            data1 = pitchWheel.Pitch;
            kind = CompiledMidiEventKind.PitchWheel;
        }
        else if (midiEvent is ChannelAfterTouchEvent afterTouch)
        {
            channel = afterTouch.Channel;
            data1 = afterTouch.AfterTouchPressure;
            kind = CompiledMidiEventKind.ChannelAfterTouch;
        }
        else if (midiEvent is PatchChangeEvent patchChange)
        {
            channel = patchChange.Channel;
            data1 = patchChange.Patch;
            kind = CompiledMidiEventKind.PatchChange;
            if (!firstPatchByChannel.ContainsKey(channel))
                firstPatchByChannel[channel] = patchChange.Patch;
        }
        else
        {
            return false;
        }

        if (!eventsByChannel.TryGetValue(channel, out List<CompiledMidiEvent>? list))
        {
            list = new List<CompiledMidiEvent>();
            eventsByChannel[channel] = list;
        }

        var compiledEvent = new CompiledMidiEvent(
            midiEvent.AbsoluteTime,
            sequence,
            channel,
            kind,
            data1,
            data2);
        list.Add(compiledEvent);
        allChannelEvents.Add(compiledEvent);
        return true;
    }
}
