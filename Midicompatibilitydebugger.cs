using NAudio.Midi;
using System.Text;

namespace MIDIRift;

/// <summary>
/// Analyzes a MIDI file and produces a compatibility report against whatever
/// features are currently declared in the engine via [SupportedFeature].
///
/// Adding a new feature to Channel / MidiStep / ChiptuneNAudio automatically
/// updates every future report — no changes needed here.
/// </summary>
public static class MidiCompatibilityDebugger
{
    // ── Entry point ───────────────────────────────────────────────────────

    public static void AnalyzeAndSave(string midiPath, string outputDir)
    {
        try
        {
            var report = Analyze(midiPath);
            Directory.CreateDirectory(outputDir);

            string name = Path.GetFileNameWithoutExtension(midiPath);
            string filePath = Path.Combine(outputDir, $"{name}_compat.txt");
            File.WriteAllText(filePath, report, Encoding.UTF8);

            System.Diagnostics.Debug.WriteLine($"[Compat] Report saved: {filePath}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Compat] Error: {ex.Message}");
        }
    }

    public static string Analyze(string midiPath)
    {
        var midiFile = new MidiFile(midiPath, false);
        var events = CollectEvents(midiFile);
        var supported = FeatureRegistry.All;

        return BuildReport(midiPath, midiFile, events, supported);
    }

    // ── Event collection ──────────────────────────────────────────────────

    private sealed record MidiEventSummary(
        string Source,       // "CC7", "PitchWheel", "NoteOn", …
        string Detail,       // human-readable detail
        int Channel,
        long FirstTick,
        int Count);

    private enum IgnoredReason
    {
        ImportantGM,
        OptionalGM,
        PluginOrFineControl
    }

    private static readonly Dictionary<string, IgnoredReason> IgnoredClassification = new()
    {
        // ── Important GM controls ─────────────────────────────
        ["CC68"] = IgnoredReason.ImportantGM, // Legato
        ["CC71"] = IgnoredReason.ImportantGM, // Resonance
        ["CC70"] = IgnoredReason.ImportantGM, // Sound variation
        ["CC67"] = IgnoredReason.OptionalGM,  // Soft pedal
        ["CC69"] = IgnoredReason.OptionalGM,  // Hold 2

        // ── Fine / LSB controls (usually safe to ignore) ─────
        ["CC32"] = IgnoredReason.PluginOrFineControl,
        ["CC33"] = IgnoredReason.PluginOrFineControl,
        ["CC34"] = IgnoredReason.PluginOrFineControl,
        ["CC35"] = IgnoredReason.PluginOrFineControl,
        ["CC36"] = IgnoredReason.PluginOrFineControl,
        ["CC37"] = IgnoredReason.PluginOrFineControl,
        ["CC38"] = IgnoredReason.PluginOrFineControl,
        ["CC39"] = IgnoredReason.PluginOrFineControl,
        ["CC40"] = IgnoredReason.PluginOrFineControl,
        ["CC41"] = IgnoredReason.PluginOrFineControl,
        ["CC42"] = IgnoredReason.PluginOrFineControl,
        ["CC43"] = IgnoredReason.PluginOrFineControl,
        ["CC44"] = IgnoredReason.PluginOrFineControl,
        ["CC45"] = IgnoredReason.PluginOrFineControl,
        ["CC46"] = IgnoredReason.PluginOrFineControl,
        ["CC47"] = IgnoredReason.PluginOrFineControl,
        ["CC48"] = IgnoredReason.PluginOrFineControl,
        ["CC49"] = IgnoredReason.PluginOrFineControl,
        ["CC50"] = IgnoredReason.PluginOrFineControl,
        ["CC51"] = IgnoredReason.PluginOrFineControl,
        ["CC52"] = IgnoredReason.PluginOrFineControl,
        ["CC53"] = IgnoredReason.PluginOrFineControl,
        ["CC54"] = IgnoredReason.PluginOrFineControl,
        ["CC55"] = IgnoredReason.PluginOrFineControl,
        ["CC56"] = IgnoredReason.PluginOrFineControl,
        ["CC57"] = IgnoredReason.PluginOrFineControl,
        ["CC58"] = IgnoredReason.PluginOrFineControl,
        ["CC59"] = IgnoredReason.PluginOrFineControl,
        ["CC60"] = IgnoredReason.PluginOrFineControl,
        ["CC61"] = IgnoredReason.PluginOrFineControl,
        ["CC62"] = IgnoredReason.PluginOrFineControl,
        ["CC63"] = IgnoredReason.PluginOrFineControl,

        // Undefined / general-purpose
        ["CC16"] = IgnoredReason.PluginOrFineControl,
        ["CC19"] = IgnoredReason.PluginOrFineControl,
        ["CC20"] = IgnoredReason.PluginOrFineControl,
        ["CC21"] = IgnoredReason.PluginOrFineControl,
        ["CC22"] = IgnoredReason.PluginOrFineControl,
        ["CC23"] = IgnoredReason.PluginOrFineControl,
        ["CC24"] = IgnoredReason.PluginOrFineControl,
        ["CC26"] = IgnoredReason.PluginOrFineControl,
        ["CC27"] = IgnoredReason.PluginOrFineControl,
        ["CC28"] = IgnoredReason.PluginOrFineControl,
        ["CC29"] = IgnoredReason.PluginOrFineControl,
        ["CC30"] = IgnoredReason.PluginOrFineControl,
        ["CC31"] = IgnoredReason.PluginOrFineControl,
    };

    private static List<MidiEventSummary> CollectEvents(MidiFile file)
    {
        // key = (source, channel)
        var counts = new Dictionary<(string, int), (string detail, long firstTick, int count)>();

        void Add(string source, string detail, int channel, long tick)
        {
            var key = (source, channel);
            if (counts.TryGetValue(key, out var existing))
                counts[key] = (existing.detail, existing.firstTick, existing.count + 1);
            else
                counts[key] = (detail, tick, 1);
        }

        foreach (var track in file.Events)
        {
            foreach (var e in track)
            {
                switch (e)
                {
                    case NoteOnEvent no when no.Velocity > 0:
                        Add("NoteOn", $"note {no.NoteNumber} vel {no.Velocity}", no.Channel, e.AbsoluteTime);
                        break;

                    case NoteOnEvent no:
                        Add("NoteOff", $"note {no.NoteNumber} (vel-0)", no.Channel, e.AbsoluteTime);
                        break;

                    case NoteEvent ne when ne.CommandCode == MidiCommandCode.NoteOff:
                        Add("NoteOff", $"note {ne.NoteNumber}", ne.Channel, e.AbsoluteTime);
                        break;

                    case PitchWheelChangeEvent pw:
                        Add("PitchWheel", $"value {pw.Pitch}", pw.Channel, e.AbsoluteTime);
                        break;

                    case ChannelAfterTouchEvent at:
                        Add("ChannelAfterTouch", $"pressure {at.AfterTouchPressure}", at.Channel, e.AbsoluteTime);
                        break;

                    case NoteEvent pk when pk.CommandCode == MidiCommandCode.KeyAfterTouch:
                        Add("PolyAfterTouch", $"note {pk.NoteNumber} pressure {pk.Velocity}", pk.Channel, e.AbsoluteTime);
                        break;

                    case PatchChangeEvent pc:
                        Add("PatchChange", $"patch {pc.Patch}", pc.Channel, e.AbsoluteTime);
                        break;

                    case ControlChangeEvent cc:
                        Add($"CC{(int)cc.Controller}", $"value {cc.ControllerValue}", cc.Channel, e.AbsoluteTime);
                        break;

                    case SysexEvent sx:
                        Add("Sysex", "sysex data", 0, e.AbsoluteTime);
                        break;

                    case TempoEvent te:
                        Add("Tempo", $"{60000000.0 / te.MicrosecondsPerQuarterNote:F1} BPM", 0, e.AbsoluteTime);
                        break;

                    case TimeSignatureEvent ts:
                        Add("TimeSignature", $"{ts.Numerator}/{Math.Pow(2, ts.Denominator):F0}", 0, e.AbsoluteTime);
                        break;

                    case KeySignatureEvent ks:
                        Add("KeySignature", $"sharps/flats={ks.SharpsFlats} major={ks.MajorMinor}", 0, e.AbsoluteTime);
                        break;


                }
            }
        }

        return counts
            .Select(kvp => new MidiEventSummary(
                kvp.Key.Item1,
                kvp.Value.detail,
                kvp.Key.Item2,
                kvp.Value.firstTick,
                kvp.Value.count))
            .OrderBy(s => s.Source)
            .ThenBy(s => s.Channel)
            .ToList();
    }

    // ── Report building ───────────────────────────────────────────────────

    private static string BuildReport(
        string midiPath,
        MidiFile file,
        List<MidiEventSummary> events,
        IReadOnlyList<SupportedFeatureInfo> supported)
    {
        var sb = new StringBuilder();
        var now = DateTime.Now;
        var sep = new string('─', 60);

        sb.AppendLine(sep);
        sb.AppendLine($"  MIDIRift compatibility report");
        sb.AppendLine($"  {Path.GetFileName(midiPath)}");
        sb.AppendLine($"  Generated {now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine(sep);
        sb.AppendLine();

        // ── File info ─────────────────────────────────────────────────────
        int trackCount = file.Events.Count();
        int ppq = file.DeltaTicksPerQuarterNote;
        sb.AppendLine($"Tracks: {trackCount}   PPQ: {ppq}");
        sb.AppendLine();

        // ── Supported feature list (from registry) ────────────────────────
        sb.AppendLine("Supported features (engine version at scan time)");
        sb.AppendLine(new string('-', 48));

        var byGroup = supported.GroupBy(f => f.Group).OrderBy(g => g.Key);
        foreach (var group in byGroup)
        {
            sb.AppendLine($"  [{group.Key}]");
            foreach (var f in group)
            {
                sb.AppendLine($"    ✓  {f.Name,-30} {f.MidiSource}");
                if (f.Description != null)
                    sb.AppendLine($"         {f.Description}");
            }
        }
        sb.AppendLine();

        // ── Events found in the file ──────────────────────────────────────
        // AllHandledSources = user-visible features + parser internals
        var allHandled = FeatureRegistry.AllHandledSources;
        var internals = FeatureRegistry.Internals;

        var userFeatures = events.Where(e => supported.Any(f =>
            string.Equals(f.MidiSource, e.Source, StringComparison.OrdinalIgnoreCase))).ToList();

        var parserInternals = events.Where(e =>
            !userFeatures.Contains(e) &&
            internals.Any(i => string.Equals(i.MidiSource, e.Source, StringComparison.OrdinalIgnoreCase))).ToList();

        var ignored = events
            .Where(e => !allHandled.Contains(e.Source))
            .ToList();

        sb.AppendLine("Events found in file");
        sb.AppendLine(new string('-', 48));

        // User-visible features
        sb.AppendLine("  Features (exposed in MidiStep / Channel):");
        if (userFeatures.Count == 0)
        {
            sb.AppendLine("    (none)");
        }
        else
        {
            foreach (var e in userFeatures)
            {
                var feature = supported.First(f =>
                    string.Equals(f.MidiSource, e.Source, StringComparison.OrdinalIgnoreCase));
                sb.AppendLine($"    ✓  {e.Source,-14} ch{e.Channel,-3} ×{e.Count,-6} {feature.Name}");
            }
        }

        // Parser internals
        sb.AppendLine();
        sb.AppendLine("  Parser internals (consumed, not in MidiStep):");
        if (parserInternals.Count == 0)
        {
            sb.AppendLine("    (none found in this file)");
        }
        else
        {
            foreach (var e in parserInternals)
            {
                var info = internals.FirstOrDefault(i =>
                    string.Equals(i.MidiSource, e.Source, StringComparison.OrdinalIgnoreCase));
                string desc = info?.Description ?? "";
                sb.AppendLine($"    ~  {e.Source,-14} ch{e.Channel,-3} ×{e.Count,-6} {desc}");
            }
        }

        // Truly unhandled
        sb.AppendLine(sep);
        sb.AppendLine();
        sb.AppendLine("  Missing important GM controls:");

        var importantMissing = ignored
            .Where(e =>
                IgnoredClassification.TryGetValue(e.Source, out var reason) &&
                reason == IgnoredReason.ImportantGM)
            .ToList();

        if (importantMissing.Count == 0)
        {
            sb.AppendLine("    (none)");
        }
        else
        {
            foreach (var e in importantMissing)
                sb.AppendLine($"    ⚠  {e.Source,-14} ch{e.Channel,-3} ×{e.Count,-6} {e.Detail}");
        }

        sb.AppendLine(new string('-', 48));

        sb.AppendLine("  Ignored by design (plugin / fine controls):");

        var benignIgnored = ignored
            .Where(e =>
                !IgnoredClassification.TryGetValue(e.Source, out var reason) ||
                reason != IgnoredReason.ImportantGM)
            .ToList();

        if (benignIgnored.Count == 0)
        {
            sb.AppendLine("    (none)");
        }
        else
        {
            foreach (var e in benignIgnored)
                sb.AppendLine($"    ·  {e.Source,-14} ch{e.Channel,-3} ×{e.Count,-6} {e.Detail}");
        }

        sb.AppendLine();

        // ── Coverage summary ──────────────────────────────────────────────
        int total = events.Count;
        int handled = userFeatures.Count + parserInternals.Count;
        double pct = total > 0 ? 100.0 * handled / total : 100.0;

        sb.AppendLine(sep);
        sb.AppendLine($"  Coverage: {handled}/{total} event types handled ({pct:F0}%)");
        sb.AppendLine($"    {userFeatures.Count} as user features, {parserInternals.Count} as parser internals");

        var heavy = importantMissing
            .Where(ev => ev.Count > 10)
            .OrderByDescending(ev => ev.Count)
            .Take(5);

        foreach (var ev in heavy)
            sb.AppendLine($"  ⚠  {ev.Source} used {ev.Count}× — consider implementing");

        sb.AppendLine(sep);

        return sb.ToString();
    }
}