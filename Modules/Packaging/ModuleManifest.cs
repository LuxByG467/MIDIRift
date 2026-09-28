namespace MIDIRift.Modules.Packaging;

/// <summary>
/// Declarative package manifest. A package describes modules; it never executes
/// installer code.
/// </summary>
public sealed record ModuleManifest
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string MidiriftApi { get; init; }
    public string? Author { get; init; }
    public string? License { get; init; }
    public string? Source { get; init; }
    public string? Sha256 { get; init; }
    public string? Signature { get; init; }
    public ModuleTrustLevel Trust { get; init; } = ModuleTrustLevel.Untrusted;
    public IReadOnlyList<ModuleManifestEntry> Modules { get; init; } =
        Array.Empty<ModuleManifestEntry>();
}
