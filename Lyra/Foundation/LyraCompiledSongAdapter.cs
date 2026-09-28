using LyraChannel = MIDIRift.CleanRoom.Features.Midi.CompiledChannel;
using LyraEvent = MIDIRift.CleanRoom.Features.Midi.CompiledMidiEvent;
using LyraEventBatch = MIDIRift.CleanRoom.Features.Midi.CompiledEventBatch;
using LyraEventKind = MIDIRift.CleanRoom.Features.Midi.CompiledMidiEventKind;
using LyraFeatureFlags = MIDIRift.CleanRoom.Features.Midi.SongFeatureFlags;
using LyraMidiChannelId = MIDIRift.CleanRoom.Features.Midi.MidiChannelId;
using LyraSong = MIDIRift.CleanRoom.Features.Midi.CompiledSong;
using LyraTempoPoint = MIDIRift.CleanRoom.Features.Midi.TempoPoint;
using LyraWaveType = MIDIRift.CleanRoom.Features.Midi.ChiptuneWaveType;

namespace MIDIRift;

/// <summary>
/// Foundation-2.
/// Traduce la entrada MIDI común de MIDIRift al formato interno que utiliza
/// Lyra. No vuelve a abrir ni parsear el archivo MIDI: CompiledMidiSong sigue
/// siendo la única fuente de verdad.
///
/// Esta clase NO cambia el motor activo todavía. Foundation-3 será quien conecte
/// Lyra al contrato de reproducción común.
/// </summary>
public static class LyraCompiledSongAdapter
{
    private const int SampleRate = LyraSong.DefaultSampleRate;

    public static LyraSong Build(ChiptuneEngineInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        CompiledMidiSong source = input.Song;
        var timeConverter = new TempoTimeConverter(source);

        var sourceMusicalChannels = source.Channels
            .Where(channel => channel.IsMusical)
            .ToArray();

        var runtimeIndexByMidiChannel = new Dictionary<int, int>(
            sourceMusicalChannels.Length);

        var lyraChannels = new LyraChannel[sourceMusicalChannels.Length];

        for (int runtimeIndex = 0; runtimeIndex < sourceMusicalChannels.Length; runtimeIndex++)
        {
            CompiledMidiChannel channel = sourceMusicalChannels[runtimeIndex];
            runtimeIndexByMidiChannel[channel.MidiChannel] = runtimeIndex;

            ChiptuneChannelSettings settings = input.Channels[runtimeIndex];

            // El modelo común conserva numeración MIDI 1..16. Lyra trabaja 0..15.
            int zeroBasedChannel = Math.Clamp(channel.MidiChannel - 1, 0, 15);
            bool percussion = channel.MidiChannel == 10;

            lyraChannels[runtimeIndex] = new LyraChannel(
                runtimeIndex,
                new LyraMidiChannelId(0, zeroBasedChannel),
                Math.Clamp(channel.InitialPatch, 0, 127),
                LyraWaveTypeBridge.ToLyra(settings.WaveType),
                GeneralMidiFamily(channel.InitialPatch),
                percussion,
                HasNotes: true,
                TrackNameFor(source, channel.MidiChannel),
                DeviceNameFor(source, channel.MidiChannel));
        }

        var lyraEvents = new List<LyraEvent>(source.Events.Length);
        LyraFeatureFlags features = LyraFeatureFlags.None;

        // RPN state is per runtime MIDI channel. We currently consume RPN 0,0
        // (Pitch Bend Sensitivity) from CC101/100 + Data Entry CC6/38.
        var rpnMsb = Enumerable.Repeat(127, sourceMusicalChannels.Length).ToArray();
        var rpnLsb = Enumerable.Repeat(127, sourceMusicalChannels.Length).ToArray();
        var dataEntryMsb = new int[sourceMusicalChannels.Length];
        var dataEntryLsb = new int[sourceMusicalChannels.Length];

        for (int i = 0; i < source.Events.Length; i++)
        {
            MIDIRift.CompiledMidiEvent midiEvent = source.Events[i];

            if (!runtimeIndexByMidiChannel.TryGetValue(
                    midiEvent.Channel,
                    out int runtimeChannelIndex))
            {
                continue;
            }

            if (midiEvent.Kind == MIDIRift.CompiledMidiEventKind.ControlChange &&
                TryMapRpnControlChange(
                    midiEvent,
                    runtimeChannelIndex,
                    timeConverter,
                    rpnMsb,
                    rpnLsb,
                    dataEntryMsb,
                    dataEntryLsb,
                    out LyraEvent rpnMapped,
                    ref features,
                    out bool consumed))
            {
                if (consumed)
                {
                    // Selector-only RPN messages are consumed without emitting an
                    // audio event; Data Entry for supported RPNs emits one.
                    if (rpnMapped.Kind == LyraEventKind.PitchBendRange)
                        lyraEvents.Add(rpnMapped);
                    continue;
                }
            }

            if (!TryMapEvent(
                    midiEvent,
                    runtimeChannelIndex,
                    timeConverter,
                    out LyraEvent mapped,
                    ref features))
            {
                continue;
            }

            lyraEvents.Add(mapped);
        }

        lyraEvents.Sort(static (left, right) =>
        {
            int sample = left.SamplePosition.CompareTo(right.SamplePosition);
            if (sample != 0) return sample;

            int priority = EventPriority(left.Kind).CompareTo(EventPriority(right.Kind));
            if (priority != 0) return priority;

            return left.SourceOrder.CompareTo(right.SourceOrder);
        });

        LyraEventBatch[] batches = BuildBatches(lyraEvents);
        LyraTempoPoint[] tempoMap = BuildTempoMap(source, timeConverter);

        if (lyraChannels.Any(channel => channel.IsPercussion))
            features |= LyraFeatureFlags.Percussion;
        if (tempoMap.Length > 1)
            features |= LyraFeatureFlags.TempoChanges;

        double musicalDurationSeconds = timeConverter.TickToSeconds(source.LastTick);
        double lastEventSeconds =
            lyraEvents.Count == 0 ? 0.0 : lyraEvents[^1].TimeSeconds;

        double durationSeconds = Math.Max(musicalDurationSeconds, lastEventSeconds) + 1.0;
        long durationSamples = SecondsToSamples(durationSeconds);

        var song = new LyraSong
        {
            SourcePath = source.SourcePath,
            TicksPerQuarterNote = source.TicksPerQuarterNote,
            SampleRate = SampleRate,
            Events = lyraEvents.ToArray(),
            EventBatches = batches,
            TempoMap = tempoMap,
            Channels = lyraChannels,
            Features = features,
            DurationSamples = durationSamples,
            DurationSeconds = durationSeconds,
            SourceEventCount = source.Events.Length + source.Metadata.Length
        };

        MIDIRift.CleanRoom.Features.Midi.CompiledSongValidator.Validate(song);
        return song;
    }

    private static bool TryMapRpnControlChange(
        MIDIRift.CompiledMidiEvent sourceEvent,
        int runtimeChannelIndex,
        TempoTimeConverter timeConverter,
        int[] rpnMsb,
        int[] rpnLsb,
        int[] dataEntryMsb,
        int[] dataEntryLsb,
        out LyraEvent mapped,
        ref LyraFeatureFlags features,
        out bool consumed)
    {
        mapped = default;
        consumed = false;

        int controller = sourceEvent.Data1;
        int ccValue = Math.Clamp(sourceEvent.Data2, 0, 127);

        switch (controller)
        {
            case 101: // RPN MSB
                rpnMsb[runtimeChannelIndex] = ccValue;
                consumed = true;
                return true;
            case 100: // RPN LSB
                rpnLsb[runtimeChannelIndex] = ccValue;
                consumed = true;
                return true;
            case 6:   // Data Entry MSB
                dataEntryMsb[runtimeChannelIndex] = ccValue;
                break;
            case 38:  // Data Entry LSB
                dataEntryLsb[runtimeChannelIndex] = ccValue;
                break;
            default:
                return false;
        }

        // RPN 0,0 = Pitch Bend Sensitivity. MSB is semitones; LSB is cents.
        // Clamp to a practical 0..24 semitone range for MIDIRift engines.
        if (rpnMsb[runtimeChannelIndex] == 0 && rpnLsb[runtimeChannelIndex] == 0)
        {
            float semitones = Math.Clamp(
                dataEntryMsb[runtimeChannelIndex] + dataEntryLsb[runtimeChannelIndex] / 100f,
                0f,
                24f);
            double seconds = timeConverter.TickToSeconds(sourceEvent.Tick);
            mapped = new LyraEvent(
                sourceEvent.Tick,
                SecondsToSamples(seconds),
                seconds,
                LyraEventKind.PitchBendRange,
                runtimeChannelIndex,
                0,
                semitones,
                sourceEvent.Sequence);
            features |= LyraFeatureFlags.PitchBendRange;
            consumed = true;
            return true;
        }

        // RPN 0,1 = Channel Fine Tuning. Data Entry forms a 14-bit value:
        // 8192 is center, 0 is approximately -100 cents and 16383 +100 cents.
        if (rpnMsb[runtimeChannelIndex] == 0 && rpnLsb[runtimeChannelIndex] == 1)
        {
            int raw14 = (dataEntryMsb[runtimeChannelIndex] << 7) |
                        dataEntryLsb[runtimeChannelIndex];
            float semitones = Math.Clamp((raw14 - 8192) / 8192f, -1f, 1f);
            double seconds = timeConverter.TickToSeconds(sourceEvent.Tick);
            mapped = new LyraEvent(
                sourceEvent.Tick,
                SecondsToSamples(seconds),
                seconds,
                LyraEventKind.FineTuning,
                runtimeChannelIndex,
                0,
                semitones,
                sourceEvent.Sequence);
            features |= LyraFeatureFlags.FineTuning;
            consumed = true;
            return true;
        }

        // RPN 0,2 = Channel Coarse Tuning. MIDI defines Data Entry MSB
        // 64 as center; values span -64..+63 semitones. LSB is not used.
        if (rpnMsb[runtimeChannelIndex] == 0 && rpnLsb[runtimeChannelIndex] == 2)
        {
            float semitones = Math.Clamp(dataEntryMsb[runtimeChannelIndex] - 64f, -64f, 63f);
            double seconds = timeConverter.TickToSeconds(sourceEvent.Tick);
            mapped = new LyraEvent(
                sourceEvent.Tick,
                SecondsToSamples(seconds),
                seconds,
                LyraEventKind.CoarseTuning,
                runtimeChannelIndex,
                0,
                semitones,
                sourceEvent.Sequence);
            features |= LyraFeatureFlags.CoarseTuning;
            consumed = true;
            return true;
        }

        // Data Entry for unsupported/Null RPN is consumed rather than being
        // misinterpreted as an unrelated CC.
        consumed = true;
        return true;
    }

    private static bool TryMapEvent(
        MIDIRift.CompiledMidiEvent sourceEvent,
        int runtimeChannelIndex,
        TempoTimeConverter timeConverter,
        out LyraEvent mapped,
        ref LyraFeatureFlags features)
    {
        LyraEventKind kind;
        byte data1 = 0;
        float value = 0f;

        switch (sourceEvent.Kind)
        {
            case MIDIRift.CompiledMidiEventKind.NoteOn:
                kind = LyraEventKind.NoteOn;
                data1 = (byte)Math.Clamp(sourceEvent.Data1, 0, 127);
                value = Math.Clamp(sourceEvent.Data2 / 127f, 0f, 1f);
                break;

            case MIDIRift.CompiledMidiEventKind.NoteOff:
                kind = LyraEventKind.NoteOff;
                data1 = (byte)Math.Clamp(sourceEvent.Data1, 0, 127);
                value = Math.Clamp(sourceEvent.Data2 / 127f, 0f, 1f);
                break;

            case MIDIRift.CompiledMidiEventKind.PatchChange:
                kind = LyraEventKind.Program;
                data1 = (byte)Math.Clamp(sourceEvent.Data1, 0, 127);
                value = data1;
                features |= LyraFeatureFlags.ProgramChanges;
                break;

            case MIDIRift.CompiledMidiEventKind.PitchWheel:
                kind = LyraEventKind.PitchBend;
                value = Math.Clamp(
                    (sourceEvent.Data1 - 8192) / 8192f,
                    -1f,
                    1f);
                features |= LyraFeatureFlags.PitchBend;
                break;

            case MIDIRift.CompiledMidiEventKind.ControlChange:
                if (!TryMapControlChange(
                        sourceEvent.Data1,
                        sourceEvent.Data2,
                        out kind,
                        out value,
                        ref features))
                {
                    mapped = default;
                    return false;
                }
                break;

            case MIDIRift.CompiledMidiEventKind.PolyAfterTouch:
            case MIDIRift.CompiledMidiEventKind.ChannelAfterTouch:
            default:
                mapped = default;
                return false;
        }

        double seconds = timeConverter.TickToSeconds(sourceEvent.Tick);
        long samplePosition = SecondsToSamples(seconds);

        mapped = new LyraEvent(
            sourceEvent.Tick,
            samplePosition,
            seconds,
            kind,
            runtimeChannelIndex,
            data1,
            value,
            sourceEvent.Sequence);
        return true;
    }

    private static bool TryMapControlChange(
        int controller,
        int controllerValue,
        out LyraEventKind kind,
        out float value,
        ref LyraFeatureFlags features)
    {
        int ccValue = Math.Clamp(controllerValue, 0, 127);
        float normalized = ccValue / 127f;

        switch (controller)
        {
            case 1:
                kind = LyraEventKind.Modulation;
                value = normalized;
                features |= LyraFeatureFlags.Modulation;
                return true;

            case 5:
                kind = LyraEventKind.PortamentoTime;
                value = normalized;
                features |= LyraFeatureFlags.Portamento;
                return true;

            case 7:
                kind = LyraEventKind.Volume;
                value = normalized;
                features |= LyraFeatureFlags.Volume;
                return true;

            case 10:
                kind = LyraEventKind.Pan;
                value = ccValue < 64
                    ? (ccValue - 64) / 64f
                    : (ccValue - 64) / 63f;
                features |= LyraFeatureFlags.Pan;
                return true;

            case 11:
                kind = LyraEventKind.Expression;
                value = normalized;
                features |= LyraFeatureFlags.Expression;
                return true;

            case 64:
                kind = LyraEventKind.Sustain;
                value = ccValue >= 64 ? 1f : 0f;
                features |= LyraFeatureFlags.Sustain;
                return true;

            case 66:
                kind = LyraEventKind.Sostenuto;
                value = ccValue >= 64 ? 1f : 0f;
                features |= LyraFeatureFlags.Sostenuto;
                return true;

            case 67:
                kind = LyraEventKind.SoftPedal;
                value = ccValue >= 64 ? 1f : 0f;
                features |= LyraFeatureFlags.SoftPedal;
                return true;

            case 65:
                kind = LyraEventKind.PortamentoSwitch;
                value = ccValue >= 64 ? 1f : 0f;
                features |= LyraFeatureFlags.Portamento;
                return true;

            case 68:
                kind = LyraEventKind.LegatoSwitch;
                value = ccValue >= 64 ? 1f : 0f;
                features |= LyraFeatureFlags.Legato;
                return true;

            case 71:
                // MIDI Sound Controller 2 / Harmonic Content (Resonance).
                // 64 is neutral; DSN-like applies it as a non-destructive
                // offset around the patch's own VCF resonance.
                kind = LyraEventKind.Resonance;
                value = normalized;
                features |= LyraFeatureFlags.Resonance;
                return true;

            case 72:
                kind = LyraEventKind.ReleaseTime;
                value = normalized;
                features |= LyraFeatureFlags.ReleaseTime;
                return true;

            case 73:
                kind = LyraEventKind.AttackTime;
                value = normalized;
                features |= LyraFeatureFlags.AttackTime;
                return true;

            case 74:
                // MIDI Sound Controller 5 / Brightness. DSN-like interprets
                // this as a non-destructive cutoff offset around the patch's
                // own VCF cutoff: 64 ~= neutral.
                kind = LyraEventKind.Brightness;
                value = normalized;
                features |= LyraFeatureFlags.Brightness;
                return true;

            case 75:
                kind = LyraEventKind.DecayTime;
                value = normalized;
                features |= LyraFeatureFlags.DecayTime;
                return true;

            case 76:
                kind = LyraEventKind.VibratoRate;
                value = normalized;
                features |= LyraFeatureFlags.VibratoRate;
                return true;

            case 77:
                kind = LyraEventKind.VibratoDepth;
                value = normalized;
                features |= LyraFeatureFlags.VibratoDepth;
                return true;

            case 78:
                kind = LyraEventKind.VibratoDelay;
                value = normalized;
                features |= LyraFeatureFlags.VibratoDelay;
                return true;

            case 84:
                kind = LyraEventKind.PortamentoControl;
                value = ccValue;
                features |= LyraFeatureFlags.PortamentoControl;
                return true;

            case 121:
                kind = LyraEventKind.ResetAllControllers;
                value = 0f;
                features |= LyraFeatureFlags.ResetAllControllers;
                return true;

            case 120:
                kind = LyraEventKind.AllSoundOff;
                value = 0f;
                features |= LyraFeatureFlags.AllSoundOff;
                return true;

            case 123:
                kind = LyraEventKind.AllNotesOff;
                value = 0f;
                features |= LyraFeatureFlags.AllNotesOff;
                return true;

            default:
                kind = default;
                value = 0f;
                return false;
        }
    }

    private static LyraEventBatch[] BuildBatches(List<LyraEvent> events)
    {
        if (events.Count == 0)
            return Array.Empty<LyraEventBatch>();

        var batches = new List<LyraEventBatch>();
        int start = 0;
        long sample = events[0].SamplePosition;

        for (int index = 1; index <= events.Count; index++)
        {
            bool end = index == events.Count;
            if (!end && events[index].SamplePosition == sample)
                continue;

            batches.Add(new LyraEventBatch(sample, start, index - start));

            if (!end)
            {
                start = index;
                sample = events[index].SamplePosition;
            }
        }

        return batches.ToArray();
    }

    private static LyraTempoPoint[] BuildTempoMap(
        CompiledMidiSong song,
        TempoTimeConverter timeConverter)
    {
        var result = new LyraTempoPoint[song.TempoMap.Length];

        for (int i = 0; i < song.TempoMap.Length; i++)
        {
            CompiledTempoChange tempo = song.TempoMap[i];
            double seconds = timeConverter.TickToSeconds(tempo.Tick);
            result[i] = new LyraTempoPoint(
                tempo.Tick,
                SecondsToSamples(seconds),
                seconds,
                tempo.MicrosecondsPerQuarterNote);
        }

        return result;
    }

    private static int EventPriority(LyraEventKind kind) => kind switch
    {
        LyraEventKind.AllNotesOff => 0,
        LyraEventKind.NoteOff => 1,

        LyraEventKind.Program or
        LyraEventKind.Volume or
        LyraEventKind.Pan or
        LyraEventKind.Expression or
        LyraEventKind.Sustain or
        LyraEventKind.PitchBend or
        LyraEventKind.Modulation or
        LyraEventKind.PortamentoTime or
        LyraEventKind.PortamentoSwitch or
        LyraEventKind.LegatoSwitch => 2,

        LyraEventKind.NoteOn => 3,
        _ => 4,
    };

    /// <summary>
    /// Convierte ticks a segundos usando prefijos del mapa de tempo.
    /// Cada consulta es O(log cambios de tempo), en lugar de recorrer todo
    /// TempoMap por cada evento del MIDI.
    /// </summary>
    private sealed class TempoTimeConverter
    {
        private readonly long[] _ticks;
        private readonly double[] _secondsAtTick;
        private readonly int[] _tempoUs;
        private readonly int _ppq;

        public TempoTimeConverter(CompiledMidiSong song)
        {
            _ppq = Math.Max(1, song.TicksPerQuarterNote);

            int count = Math.Max(1, song.TempoMap.Length);
            _ticks = new long[count];
            _secondsAtTick = new double[count];
            _tempoUs = new int[count];

            if (song.TempoMap.Length == 0)
            {
                _ticks[0] = 0;
                _tempoUs[0] = 500_000;
                return;
            }

            double seconds = 0.0;
            long previousTick = song.TempoMap[0].Tick;
            int previousTempo = song.TempoMap[0].MicrosecondsPerQuarterNote;

            for (int i = 0; i < song.TempoMap.Length; i++)
            {
                CompiledTempoChange point = song.TempoMap[i];

                if (i > 0)
                {
                    long deltaTicks = point.Tick - previousTick;
                    seconds += deltaTicks * (double)previousTempo /
                               _ppq / 1_000_000.0;
                }

                _ticks[i] = point.Tick;
                _secondsAtTick[i] = seconds;
                _tempoUs[i] = point.MicrosecondsPerQuarterNote;

                previousTick = point.Tick;
                previousTempo = point.MicrosecondsPerQuarterNote;
            }
        }

        public double TickToSeconds(long tick)
        {
            if (tick <= 0)
                return 0.0;

            int index = Array.BinarySearch(_ticks, tick);
            if (index < 0)
                index = ~index - 1;
            if (index < 0)
                index = 0;

            long deltaTicks = tick - _ticks[index];
            return _secondsAtTick[index] +
                   deltaTicks * (double)_tempoUs[index] /
                   _ppq / 1_000_000.0;
        }
    }

    private static long SecondsToSamples(double seconds)
        => Math.Max(0L, (long)Math.Round(seconds * SampleRate));

    private static string GeneralMidiFamily(int program)
    {
        string[] families =
        [
            "Piano", "Chromatic Percussion", "Organ", "Guitar",
            "Bass", "Strings", "Ensemble", "Brass",
            "Reed", "Pipe", "Synth Lead", "Synth Pad",
            "Synth Effects", "Ethnic", "Percussive", "Sound Effects"
        ];

        return families[Math.Clamp(program, 0, 127) / 8];
    }

    private static string? TrackNameFor(CompiledMidiSong song, int midiChannel)
        => MetadataFor(song, midiChannel);

    private static string? DeviceNameFor(CompiledMidiSong song, int midiChannel)
        => MetadataFor(song, midiChannel);

    private static string? MetadataFor(CompiledMidiSong song, int midiChannel)
    {
        // Foundation-1 preserva metadata pero aún no la asocia a un canal
        // concreto. No inventamos esa relación durante Foundation-2.
        _ = song;
        _ = midiChannel;
        return null;
    }
}
