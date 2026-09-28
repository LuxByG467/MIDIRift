namespace MIDIRift.CleanRoom.Features.Midi;

public readonly record struct TrackerCell(int Note, float Velocity, bool IsAttack)
{
    public static TrackerCell Empty => new(-1, 0, false);
    public bool HasNote => Note >= 0;
}

public sealed record TrackerRow(double TimeSeconds, TrackerCell[] Cells);

public sealed class TrackerTimeline
{
    public required MidiChannelId[] ChannelIds { get; init; }
    public required string[] ChannelLabels { get; init; }
    public required string[] WaveTypeNames { get; init; }
    public required TrackerRow[] Rows { get; init; }

    public int FindRow(double seconds)
    {
        if (Rows.Length == 0) return 0;
        int lo = 0;
        int hi = Rows.Length - 1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            if (Rows[mid].TimeSeconds <= seconds) lo = mid + 1;
            else hi = mid - 1;
        }
        return Math.Clamp(hi, 0, Rows.Length - 1);
    }

    public double FindVisualRow(double seconds)
    {
        if (Rows.Length <= 1) return 0;
        int current = FindRow(seconds);
        if (current >= Rows.Length - 1) return current;
        double from = Rows[current].TimeSeconds;
        double to = Rows[current + 1].TimeSeconds;
        if (to <= from) return current;
        double t = Math.Clamp((seconds - from) / (to - from), 0, 1);
        t = t * t * (3 - 2 * t);
        return current + t;
    }
}

public static class TrackerTimelineBuilder
{
    public static TrackerTimeline Build(CompiledSong song)
    {
        MidiChannelId[] channelIds = song.Channels.Select(channel => channel.Id).ToArray();
        bool showPorts = channelIds.Select(id => id.Port).Distinct().Skip(1).Any();
        string[] labels = channelIds
            .Select(id => showPorts ? $"P{id.Port} CH{id.DisplayChannel:00}" : $"CH{id.DisplayChannel:00}")
            .ToArray();

        var rows = new List<TrackerRow>();
        var active = new int[channelIds.Length];
        var velocities = new float[channelIds.Length];
        Array.Fill(active, -1);

        foreach (IGrouping<long, CompiledMidiEvent> group in song.Events.GroupBy(action => action.SamplePosition))
        {
            double rowTimeSeconds = group.First().TimeSeconds;
            var cells = new TrackerCell[channelIds.Length];
            for (int index = 0; index < cells.Length; index++)
                cells[index] = active[index] >= 0
                    ? new TrackerCell(active[index], velocities[index], false)
                    : TrackerCell.Empty;

            foreach (CompiledMidiEvent action in group)
            {
                int channelIndex = action.ChannelIndex;
                if (channelIndex < 0 || channelIndex >= channelIds.Length)
                    continue;

                switch (action.Kind)
                {
                    case CompiledMidiEventKind.NoteOn:
                        active[channelIndex] = action.Data1;
                        velocities[channelIndex] = action.Value;
                        cells[channelIndex] = new TrackerCell(action.Data1, action.Value, true);
                        break;
                    case CompiledMidiEventKind.NoteOff:
                        if (active[channelIndex] == action.Data1)
                        {
                            active[channelIndex] = -1;
                            velocities[channelIndex] = 0;
                            cells[channelIndex] = TrackerCell.Empty;
                        }
                        break;
                    case CompiledMidiEventKind.AllNotesOff:
                        active[channelIndex] = -1;
                        velocities[channelIndex] = 0;
                        cells[channelIndex] = TrackerCell.Empty;
                        break;
                }
            }

            rows.Add(new TrackerRow(rowTimeSeconds, cells));
        }

        return new TrackerTimeline
        {
            ChannelIds = channelIds,
            ChannelLabels = labels,
            WaveTypeNames = song.Channels
                .Select(channel => ChiptuneWaveTypeMapper.DisplayName(channel.InitialWaveType))
                .ToArray(),
            Rows = rows.ToArray()
        };
    }
}
