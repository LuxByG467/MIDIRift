using System.Reflection;

namespace MIDIRift;

// ── Feature attribute (user-visible features in MidiStep / Channel) ───────────

/// <summary>
/// Marks a property as a supported, user-visible engine feature derived from a
/// MIDI event. The debugger shows these under "Handled" and counts them toward
/// coverage.
/// </summary>
[AttributeUsage(
    AttributeTargets.Class |
    AttributeTargets.Property |
    AttributeTargets.Field,
    AllowMultiple = true,
    Inherited = false)]
public sealed class SupportedFeatureAttribute : Attribute
{
    public string Name { get; }
    public string MidiSource { get; }
    public string? Description { get; init; }
    public string Group { get; init; } = "General";

    public SupportedFeatureAttribute(string name, string midiSource)
    {
        Name = name;
        MidiSource = midiSource;
    }
}

// ── Infrastructure attribute (parser internals — consumed, not exposed) ────────

/// <summary>
/// Marks a method or class as handling a MIDI event internally — the event is
/// consumed by the parser/engine but does not surface as a MidiStep property.
/// The debugger shows these under "Parser internals" and excludes them from the
/// "Ignored" list so they don't produce false warnings.
/// </summary>
[AttributeUsage(
    AttributeTargets.Class |
    AttributeTargets.Method |
    AttributeTargets.Property,
    AllowMultiple = true,
    Inherited = false)]
public sealed class HandledInternallyAttribute : Attribute
{
    public string MidiSource { get; }
    public string? Description { get; init; }

    public HandledInternallyAttribute(string midiSource, string? description = null)
    {
        MidiSource = midiSource;
        Description = description;
    }
}

// ── Registry ──────────────────────────────────────────────────────────────────

public sealed class SupportedFeatureInfo
{
    public string Name { get; init; } = "";
    public string MidiSource { get; init; } = "";
    public string? Description { get; init; }
    public string Group { get; init; } = "General";
    public string DeclaredOn { get; init; } = "";
}

public sealed class InternalFeatureInfo
{
    public string MidiSource { get; init; } = "";
    public string? Description { get; init; }
    public string DeclaredOn { get; init; } = "";
}

public static class FeatureRegistry
{
    private static IReadOnlyList<SupportedFeatureInfo>? _featureCache;
    private static IReadOnlyList<InternalFeatureInfo>? _internalCache;

    public static IReadOnlyList<SupportedFeatureInfo> All =>
        _featureCache ??= Scan().features;

    public static IReadOnlyList<InternalFeatureInfo> Internals =>
        _internalCache ??= Scan().internals;

    /// <summary>
    /// Union of user-visible and internal MIDI sources — anything in this set
    /// is handled somewhere and must not appear in "Ignored".
    /// </summary>
    public static IReadOnlySet<string> AllHandledSources { get; private set; } =
        new HashSet<string>();

    public static IReadOnlyList<SupportedFeatureInfo> Rescan()
    {
        _featureCache = null;
        _internalCache = null;
        return All;
    }

    // ── Internal ──────────────────────────────────────────────────────────

    private static (List<SupportedFeatureInfo> features, List<InternalFeatureInfo> internals) Scan()
    {
        var assembly = typeof(FeatureRegistry).Assembly;
        var features = new List<SupportedFeatureInfo>();
        var internals = new List<InternalFeatureInfo>();

        foreach (var type in assembly.GetTypes())
        {
            // [SupportedFeature] on class
            foreach (var a in type.GetCustomAttributes<SupportedFeatureAttribute>())
                features.Add(FeatureFrom(a, type.Name));

            // [HandledInternally] on class
            foreach (var a in type.GetCustomAttributes<HandledInternallyAttribute>())
                internals.Add(InternalFrom(a, type.Name));

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                foreach (var a in prop.GetCustomAttributes<SupportedFeatureAttribute>())
                    features.Add(FeatureFrom(a, type.Name));
                foreach (var a in prop.GetCustomAttributes<HandledInternallyAttribute>())
                    internals.Add(InternalFrom(a, type.Name));
            }

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                foreach (var a in method.GetCustomAttributes<SupportedFeatureAttribute>())
                    features.Add(FeatureFrom(a, type.Name));
                foreach (var a in method.GetCustomAttributes<HandledInternallyAttribute>())
                    internals.Add(InternalFrom(a, type.Name));
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                foreach (var a in field.GetCustomAttributes<SupportedFeatureAttribute>())
                    features.Add(FeatureFrom(a, type.Name));
            }
        }

        var dedupedFeatures = features
            .GroupBy(f => f.MidiSource, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(f => f.Group).ThenBy(f => f.Name)
            .ToList();

        var dedupedInternals = internals
            .GroupBy(i => i.MidiSource, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(i => i.MidiSource)
            .ToList();

        AllHandledSources = new HashSet<string>(
            dedupedFeatures.Select(f => f.MidiSource)
            .Concat(dedupedInternals.Select(i => i.MidiSource)),
            StringComparer.OrdinalIgnoreCase);

        _featureCache = dedupedFeatures;
        _internalCache = dedupedInternals;
        return (dedupedFeatures, dedupedInternals);
    }

    private static SupportedFeatureInfo FeatureFrom(SupportedFeatureAttribute a, string on) =>
        new() { Name = a.Name, MidiSource = a.MidiSource, Description = a.Description, Group = a.Group, DeclaredOn = on };

    private static InternalFeatureInfo InternalFrom(HandledInternallyAttribute a, string on) =>
        new() { MidiSource = a.MidiSource, Description = a.Description, DeclaredOn = on };
}