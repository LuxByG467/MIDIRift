namespace MIDIRift.Modules;

/// <summary>Stable identity and metadata for a MIDIRift module.</summary>
public sealed record ModuleDescriptor(
    string Id,
    string Name,
    string Version,
    ModuleType Type);
