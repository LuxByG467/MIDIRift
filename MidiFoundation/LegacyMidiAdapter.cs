namespace MIDIRift;

/// <summary>
/// Convierte la representación MIDI común al formato de steps que espera el
/// sintetizador Classic/Legacy. Esta es la ÚNICA capa que conoce MidiStep.
/// </summary>
public static class LegacyMidiAdapter
{
    public static List<Channel> BuildChannels(CompiledMidiSong song) =>
        BuildChannels(new ChiptuneEngineInput(song));

    public static List<Channel> BuildChannels(ChiptuneEngineInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        CompiledMidiSong song = input.Song;
        var result = new List<Channel>(song.MusicalChannelCount);
        int musicalIndex = 0;

        for (int c = 0; c < song.Channels.Length; c++)
        {
            CompiledMidiChannel compiledChannel = song.Channels[c];
            if (!compiledChannel.IsMusical)
                continue;

            List<MidiStep> steps = ParseTrack(song, compiledChannel.Events);
            if (steps.Count == 0)
                continue;

            ChiptuneChannelSettings settings = input.Channels[musicalIndex++];
            var channel = new Channel
            {
                MidiChannel = compiledChannel.MidiChannel,
                WaveType = settings.WaveType,
                UserGain = settings.UserGain,
                Track = steps,
            };

            if (channel.WaveType == WaveType.Noise ||
                channel.WaveType == WaveType.WhiteNoise ||
                channel.WaveType == WaveType.ChipDrums)
            {
                channel.DefaultAttack = channel.Attack = 0.001;
                channel.DefaultDecay = channel.Decay = 0.08;
                channel.DefaultSustain = channel.Sustain = 0.0;
                channel.DefaultRelease = channel.Release = 0.05;
            }

            result.Add(channel);
        }

        float baseVolume = result.Count > 0 ? 0.6f / result.Count : 0.25f;
        for (int i = 0; i < result.Count; i++)
            result[i].Volume = baseVolume;

        return result;
    }

    private static List<MidiStep> ParseTrack(
        CompiledMidiSong song,
        IReadOnlyList<CompiledMidiEvent> track)
    {
        var notesOnAtTime = new SortedDictionary<long, List<(int note, int velocity)>>();
        var notesOffAtTime = new SortedDictionary<long, List<int>>();
        var polyAtAtTime = new SortedDictionary<long, List<(int note, float pressure)>>();
        var pitchBendAtTime = new SortedDictionary<long, float>();
        var pitchBendRangeAt = new SortedDictionary<long, float>();
        var fineTuningAt = new SortedDictionary<long, float>();
        var coarseTuningAt = new SortedDictionary<long, float>();
        var volumeAtTime = new SortedDictionary<long, float>();
        var panAtTime = new SortedDictionary<long, float>();
        var expressionAtTime = new SortedDictionary<long, float>();
        var sustainAtTime = new SortedDictionary<long, bool>();
        var modulationAtTime = new SortedDictionary<long, float>();
        var afterTouchAtTime = new SortedDictionary<long, float>();
        var attackAtTime = new SortedDictionary<long, float>();
        var releaseAtTime = new SortedDictionary<long, float>();
        var brightnessAtTime = new SortedDictionary<long, float>();
        var portamentoOnAt = new SortedDictionary<long, bool>();
        var portamentoTimeAt = new SortedDictionary<long, float>();
        var lfoRateAt = new SortedDictionary<long, float>();
        var sostenutoAt = new SortedDictionary<long, bool>();
        var legatoAt = new SortedDictionary<long, bool>();
        var soundVarAt = new SortedDictionary<long, float>();
        var resonanceAt = new SortedDictionary<long, float>();
        var decayAtTime = new SortedDictionary<long, float>();
        var vibratoDepthAt = new SortedDictionary<long, float>();
        var vibratoDelayAt = new SortedDictionary<long, float>();
        var softPedalAt = new SortedDictionary<long, float>();
        var portamentoSourceAt = new SortedDictionary<long, int>();

        int rpnMsb = 127;
        int rpnLsb = 127;
        int dataMsb = 2;
        int dataLsb = 0;

        for (int i = 0; i < track.Count; i++)
        {
            CompiledMidiEvent midiEvent = track[i];
            long time = midiEvent.Tick;

            switch (midiEvent.Kind)
            {
                case CompiledMidiEventKind.NoteOn:
                    AddNoteOn(notesOnAtTime, time, midiEvent.Data1, midiEvent.Data2);
                    break;

                case CompiledMidiEventKind.NoteOff:
                    AddNoteOff(notesOffAtTime, time, midiEvent.Data1);
                    break;

                case CompiledMidiEventKind.PolyAfterTouch:
                    if (!polyAtAtTime.TryGetValue(time, out List<(int note, float pressure)>? polyList))
                    {
                        polyList = new List<(int note, float pressure)>();
                        polyAtAtTime[time] = polyList;
                    }
                    polyList.Add((midiEvent.Data1, midiEvent.Data2 / 127f));
                    break;

                case CompiledMidiEventKind.PitchWheel:
                    pitchBendAtTime[time] = Math.Clamp((midiEvent.Data1 - 8192) / 8192f, -1f, 1f);
                    break;

                case CompiledMidiEventKind.ChannelAfterTouch:
                    afterTouchAtTime[time] = midiEvent.Data1 / 127f;
                    break;

                case CompiledMidiEventKind.ControlChange:
                    if (TryApplyRpn(
                        midiEvent,
                        ref rpnMsb,
                        ref rpnLsb,
                        ref dataMsb,
                        ref dataLsb,
                        pitchBendRangeAt,
                        fineTuningAt,
                        coarseTuningAt))
                        break;

                    ApplyControlChange(
                        midiEvent,
                        volumeAtTime,
                        panAtTime,
                        expressionAtTime,
                        sustainAtTime,
                        modulationAtTime,
                        attackAtTime,
                        releaseAtTime,
                        brightnessAtTime,
                        portamentoOnAt,
                        portamentoTimeAt,
                        lfoRateAt,
                        sostenutoAt,
                        legatoAt,
                        soundVarAt,
                        resonanceAt,
                        decayAtTime,
                        vibratoDepthAt,
                        vibratoDelayAt,
                        softPedalAt,
                        portamentoSourceAt);
                    break;

                case CompiledMidiEventKind.PatchChange:
                    // Legacy conserva históricamente el primer patch como timbre
                    // inicial. Los cambios posteriores quedan preservados en el
                    // CompiledMidiSong para Lyra/futuras versiones.
                    break;
            }
        }

        var allTimes = new SortedSet<long>(
            notesOnAtTime.Keys
            .Concat(notesOffAtTime.Keys)
            .Concat(polyAtAtTime.Keys)
            .Concat(pitchBendAtTime.Keys)
            .Concat(pitchBendRangeAt.Keys)
            .Concat(fineTuningAt.Keys)
            .Concat(coarseTuningAt.Keys)
            .Concat(volumeAtTime.Keys)
            .Concat(panAtTime.Keys)
            .Concat(expressionAtTime.Keys)
            .Concat(sustainAtTime.Keys)
            .Concat(modulationAtTime.Keys)
            .Concat(afterTouchAtTime.Keys)
            .Concat(attackAtTime.Keys)
            .Concat(releaseAtTime.Keys)
            .Concat(brightnessAtTime.Keys)
            .Concat(portamentoOnAt.Keys)
            .Concat(portamentoTimeAt.Keys)
            .Concat(lfoRateAt.Keys)
            .Concat(sostenutoAt.Keys)
            .Concat(legatoAt.Keys)
            .Concat(soundVarAt.Keys)
            .Concat(resonanceAt.Keys)
            .Concat(decayAtTime.Keys)
            .Concat(vibratoDepthAt.Keys)
            .Concat(vibratoDelayAt.Keys)
            .Concat(softPedalAt.Keys)
            .Concat(portamentoSourceAt.Keys));

        var steps = new List<MidiStep>();
        var currentNotes = new List<(int note, int velocity)>();
        var currentPolyAt = new Dictionary<int, float>();
        var pendingNoteOns = new List<(int note, int velocity)>();
        var pendingNoteOffs = new List<int>();

        float curPitchBend = 0f;
        float curPitchBendRange = 2f;
        float curFineTuning = 0f;
        float curCoarseTuning = 0f;
        float curVolume = 1f;
        float curPan = 0f;
        float curExpression = 1f;
        float curModulation = 0f;
        float curAfterTouch = 0f;
        float curAttack = -1f;
        float curRelease = -1f;
        float curBrightness = 1f;
        float curPortamentoTime = 0f;
        float curLfoRate = 5f;
        float curSoundVar = 0f;
        float curResonance = 0f;
        float curDecay = -1f;
        float curVibratoDepth = 64f / 127f;
        float curVibratoDelay = 64f / 127f;
        float curSoftPedal = 0f;
        int curPortamentoSource = -1;
        bool curSustain = false;
        bool curPortamentoOn = false;
        bool curSostenuto = false;
        bool curLegato = false;

        long lastTime = 0;

        foreach (long time in allTimes)
        {
            long delta = time - lastTime;
            if (delta > 0)
            {
                MidiStep step = CreateStep(
                    song,
                    lastTime,
                    delta,
                    currentNotes,
                    currentPolyAt,
                    pendingNoteOns,
                    pendingNoteOffs,
                    ComputePitchRatio(curPitchBend, curPitchBendRange, curFineTuning, curCoarseTuning),
                    curVolume,
                    curPan,
                    curExpression,
                    curSustain,
                    curModulation,
                    curAfterTouch,
                    curAttack,
                    curRelease,
                    curBrightness,
                    curPortamentoOn,
                    curPortamentoTime,
                    curLfoRate,
                    curSostenuto,
                    curLegato,
                    curSoundVar,
                    curResonance,
                    curDecay,
                    curVibratoDepth,
                    curVibratoDelay,
                    curSoftPedal,
                    curPortamentoSource);
                steps.Add(step);
                pendingNoteOffs.Clear();
                pendingNoteOns.Clear();
            }

            if (pitchBendAtTime.TryGetValue(time, out float bend)) curPitchBend = bend;
            if (pitchBendRangeAt.TryGetValue(time, out float bendRange)) curPitchBendRange = bendRange;
            if (fineTuningAt.TryGetValue(time, out float fineTuning)) curFineTuning = fineTuning;
            if (coarseTuningAt.TryGetValue(time, out float coarseTuning)) curCoarseTuning = coarseTuning;
            if (volumeAtTime.TryGetValue(time, out float vol)) curVolume = vol;
            if (panAtTime.TryGetValue(time, out float pan)) curPan = pan;
            if (expressionAtTime.TryGetValue(time, out float expression)) curExpression = expression;
            if (sustainAtTime.TryGetValue(time, out bool sustain)) curSustain = sustain;
            if (modulationAtTime.TryGetValue(time, out float modulation)) curModulation = modulation;
            if (afterTouchAtTime.TryGetValue(time, out float afterTouch)) curAfterTouch = afterTouch;
            if (attackAtTime.TryGetValue(time, out float attack)) curAttack = attack;
            if (releaseAtTime.TryGetValue(time, out float release)) curRelease = release;
            if (brightnessAtTime.TryGetValue(time, out float brightness)) curBrightness = brightness;
            if (portamentoOnAt.TryGetValue(time, out bool portamentoOn)) curPortamentoOn = portamentoOn;
            if (portamentoTimeAt.TryGetValue(time, out float portamentoTime)) curPortamentoTime = portamentoTime;
            if (lfoRateAt.TryGetValue(time, out float lfoRate)) curLfoRate = lfoRate;
            if (sostenutoAt.TryGetValue(time, out bool sostenuto)) curSostenuto = sostenuto;
            if (legatoAt.TryGetValue(time, out bool legato)) curLegato = legato;
            if (soundVarAt.TryGetValue(time, out float soundVar)) curSoundVar = soundVar;
            if (resonanceAt.TryGetValue(time, out float resonance)) curResonance = resonance;
            if (decayAtTime.TryGetValue(time, out float decay)) curDecay = decay;
            if (vibratoDepthAt.TryGetValue(time, out float vibratoDepth)) curVibratoDepth = vibratoDepth;
            if (vibratoDelayAt.TryGetValue(time, out float vibratoDelay)) curVibratoDelay = vibratoDelay;
            if (softPedalAt.TryGetValue(time, out float softPedal)) curSoftPedal = softPedal;
            if (portamentoSourceAt.TryGetValue(time, out int portamentoSource)) curPortamentoSource = portamentoSource;

            if (polyAtAtTime.TryGetValue(time, out List<(int note, float pressure)>? polyEvents))
            {
                for (int p = 0; p < polyEvents.Count; p++)
                {
                    (int note, float pressure) = polyEvents[p];
                    currentPolyAt[note] = pressure;
                }
            }

            if (notesOffAtTime.TryGetValue(time, out List<int>? offs))
            {
                for (int o = 0; o < offs.Count; o++)
                {
                    int note = offs[o];
                    pendingNoteOffs.Add(note);
                    int activeIndex = currentNotes.FindIndex(x => x.note == note);
                    if (activeIndex >= 0) currentNotes.RemoveAt(activeIndex);
                    if (!currentNotes.Exists(x => x.note == note))
                        currentPolyAt.Remove(note);
                }
            }

            if (notesOnAtTime.TryGetValue(time, out List<(int note, int velocity)>? ons))
            {
                for (int o = 0; o < ons.Count; o++)
                {
                    (int note, int velocity) = ons[o];
                    pendingNoteOns.Add((note, velocity));
                    currentNotes.Add((note, velocity));
                    currentPolyAt.TryAdd(note, 0f);
                }
            }

            if (notesOnAtTime.ContainsKey(time) && curPortamentoSource >= 0)
                curPortamentoSource = -1;

            lastTime = time;
        }

        if (currentNotes.Count > 0 || pendingNoteOns.Count > 0 || pendingNoteOffs.Count > 0)
        {
            MidiStep step = CreateStep(
                song,
                lastTime,
                song.TicksPerQuarterNote,
                currentNotes,
                currentPolyAt,
                pendingNoteOns,
                pendingNoteOffs,
                ComputePitchRatio(curPitchBend, curPitchBendRange, curFineTuning, curCoarseTuning),
                curVolume,
                curPan,
                curExpression,
                curSustain,
                curModulation,
                curAfterTouch,
                curAttack,
                curRelease,
                curBrightness,
                curPortamentoOn,
                curPortamentoTime,
                curLfoRate,
                curSostenuto,
                curLegato,
                curSoundVar,
                curResonance,
                curDecay,
                curVibratoDepth,
                curVibratoDelay,
                curSoftPedal,
                curPortamentoSource);
            steps.Add(step);
        }

        return steps;
    }

    private static float ComputePitchRatio(
        float normalizedBend,
        float bendRangeSemitones,
        float fineTuningSemitones,
        float coarseTuningSemitones)
    {
        float semitones =
            Math.Clamp(normalizedBend, -1f, 1f) * Math.Clamp(bendRangeSemitones, 0f, 24f) +
            Math.Clamp(fineTuningSemitones, -1f, 1f) +
            Math.Clamp(coarseTuningSemitones, -64f, 63f);
        return MathF.Pow(2f, semitones / 12f);
    }

    private static bool TryApplyRpn(
        CompiledMidiEvent midiEvent,
        ref int rpnMsb,
        ref int rpnLsb,
        ref int dataMsb,
        ref int dataLsb,
        SortedDictionary<long, float> pitchBendRangeAt,
        SortedDictionary<long, float> fineTuningAt,
        SortedDictionary<long, float> coarseTuningAt)
    {
        int cc = midiEvent.Data1;
        int value = Math.Clamp(midiEvent.Data2, 0, 127);
        long time = midiEvent.Tick;

        switch (cc)
        {
            case 101:
                rpnMsb = value;
                return true;
            case 100:
                rpnLsb = value;
                return true;
            case 6:
                dataMsb = value;
                break;
            case 38:
                dataLsb = value;
                break;
            default:
                return false;
        }

        // RPN Null: Data Entry has no target.
        if (rpnMsb == 127 && rpnLsb == 127)
            return true;

        if (rpnMsb != 0)
            return true;

        switch (rpnLsb)
        {
            case 0:
                pitchBendRangeAt[time] = Math.Clamp(dataMsb + dataLsb / 100f, 0f, 24f);
                break;
            case 1:
            {
                int value14 = (dataMsb << 7) | dataLsb;
                fineTuningAt[time] = Math.Clamp((value14 - 8192) / 8192f, -1f, 1f);
                break;
            }
            case 2:
                coarseTuningAt[time] = Math.Clamp(dataMsb - 64, -64, 63);
                break;
        }
        return true;
    }

    private static void ApplyControlChange(
        CompiledMidiEvent midiEvent,
        SortedDictionary<long, float> volumeAtTime,
        SortedDictionary<long, float> panAtTime,
        SortedDictionary<long, float> expressionAtTime,
        SortedDictionary<long, bool> sustainAtTime,
        SortedDictionary<long, float> modulationAtTime,
        SortedDictionary<long, float> attackAtTime,
        SortedDictionary<long, float> releaseAtTime,
        SortedDictionary<long, float> brightnessAtTime,
        SortedDictionary<long, bool> portamentoOnAt,
        SortedDictionary<long, float> portamentoTimeAt,
        SortedDictionary<long, float> lfoRateAt,
        SortedDictionary<long, bool> sostenutoAt,
        SortedDictionary<long, bool> legatoAt,
        SortedDictionary<long, float> soundVarAt,
        SortedDictionary<long, float> resonanceAt,
        SortedDictionary<long, float> decayAtTime,
        SortedDictionary<long, float> vibratoDepthAt,
        SortedDictionary<long, float> vibratoDelayAt,
        SortedDictionary<long, float> softPedalAt,
        SortedDictionary<long, int> portamentoSourceAt)
    {
        int controller = midiEvent.Data1;
        int value = midiEvent.Data2;
        long time = midiEvent.Tick;

        switch (controller)
        {
            case 1: modulationAtTime[time] = value / 127f; break;
            case 5: portamentoTimeAt[time] = value / 127f * 2f; break;
            case 7: volumeAtTime[time] = value / 127f; break;
            case 10:
                panAtTime[time] = value < 64
                    ? (value - 64) / 64f
                    : (value - 64) / 63f;
                break;
            case 11: expressionAtTime[time] = value / 127f; break;
            case 64: sustainAtTime[time] = value >= 64; break;
            case 65: portamentoOnAt[time] = value >= 64; break;
            case 66: sostenutoAt[time] = value >= 64; break;
            case 67: softPedalAt[time] = value >= 64 ? 1f : 0f; break;
            case 68: legatoAt[time] = value >= 64; break;
            case 70: soundVarAt[time] = value / 127f; break;
            case 71: resonanceAt[time] = value / 127f; break;
            case 72: releaseAtTime[time] = value / 127f; break;
            case 73: attackAtTime[time] = value / 127f; break;
            case 74: brightnessAtTime[time] = value / 127f; break;
            case 75: decayAtTime[time] = value / 127f; break;
            case 76: lfoRateAt[time] = value / 127f * 20f; break;
            case 77: vibratoDepthAt[time] = value / 127f; break;
            case 78: vibratoDelayAt[time] = value / 127f; break;
            case 84: portamentoSourceAt[time] = Math.Clamp(value, 0, 127); break;
        }
    }

    private static MidiStep CreateStep(
        CompiledMidiSong song,
        long startTick,
        long delta,
        List<(int note, int velocity)> currentNotes,
        Dictionary<int, float> currentPolyAt,
        List<(int note, int velocity)> pendingNoteOns,
        List<int> pendingNoteOffs,
        float curPitch,
        float curVolume,
        float curPan,
        float curExpression,
        bool curSustain,
        float curModulation,
        float curAfterTouch,
        float curAttack,
        float curRelease,
        float curBrightness,
        bool curPortamentoOn,
        float curPortamentoTime,
        float curLfoRate,
        bool curSostenuto,
        bool curLegato,
        float curSoundVar,
        float curResonance,
        float curDecay,
        float curVibratoDepth,
        float curVibratoDelay,
        float curSoftPedal,
        int curPortamentoSource)
    {
        MidiStep step = MidiStep.Default();
        step.DurationMs = song.TicksToMilliseconds(startTick, delta);
        step.PitchMult = curPitch;
        step.Volume = curVolume;
        step.Pan = curPan;
        step.Expression = curExpression;
        step.Sustain = curSustain;
        step.Modulation = curModulation;
        step.AfterTouch = curAfterTouch;
        step.Attack = curAttack;
        step.Release = curRelease;
        step.Brightness = curBrightness;
        step.PortamentoOn = curPortamentoOn;
        step.PortamentoTime = curPortamentoTime;
        step.LfoRate = curLfoRate;
        step.Sostenuto = curSostenuto;
        step.Legato = curLegato;
        step.SoundVariation = curSoundVar;
        step.Resonance = curResonance;
        step.Decay = curDecay;
        step.VibratoDepth = curVibratoDepth;
        step.VibratoDelay = curVibratoDelay;
        step.SoftPedal = curSoftPedal;
        step.PortamentoSourceNote = curPortamentoSource;

        for (int i = 0; i < currentNotes.Count; i++)
        {
            (int note, int velocity) = currentNotes[i];
            float polyAt = currentPolyAt.TryGetValue(note, out float pressure) ? pressure : 0f;
            step.AddNote(MapNote(note), velocity / 127f, polyAt);
        }

        for (int i = 0; i < pendingNoteOffs.Count; i++)
            step.AddNoteOff(pendingNoteOffs[i]);
        for (int i = 0; i < pendingNoteOns.Count; i++)
        {
            (int note, int velocity) = pendingNoteOns[i];
            step.AddNoteOn(note, velocity / 127f);
        }

        return step;
    }

    private static void AddNoteOn(
        SortedDictionary<long, List<(int note, int velocity)>> target,
        long time,
        int note,
        int velocity)
    {
        if (!target.TryGetValue(time, out List<(int note, int velocity)>? list))
        {
            list = new List<(int note, int velocity)>();
            target[time] = list;
        }
        list.Add((note, velocity));
    }

    private static void AddNoteOff(
        SortedDictionary<long, List<int>> target,
        long time,
        int note)
    {
        if (!target.TryGetValue(time, out List<int>? list))
        {
            list = new List<int>();
            target[time] = list;
        }
        list.Add(note);
    }

    private static float MapNote(int midiNote) =>
        (float)(440.0 * Math.Pow(2.0, (midiNote - 69) / 12.0));
}
