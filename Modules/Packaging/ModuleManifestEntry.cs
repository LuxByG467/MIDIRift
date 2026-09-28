namespace MIDIRift.Modules.Packaging;

public sealed record ModuleManifestEntry
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required ModuleType Type { get; init; }
    public string? EntryPoint { get; init; }
    public IReadOnlyList<ModulePermission> Permissions { get; init; } =
        Array.Empty<ModulePermission>();
}
