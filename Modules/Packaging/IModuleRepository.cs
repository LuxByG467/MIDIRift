namespace MIDIRift.Modules.Packaging;

/// <summary>
/// Read-only repository index abstraction. Transport is deliberately separate:
/// Git/P2P/mirrors may provide bytes, but the verified index remains authority.
/// </summary>
public interface IModuleRepository
{
    string Id { get; }
    IReadOnlyList<ModuleRepositoryEntry> Entries { get; }
    ModuleRepositoryEntry? Find(string packageId, string? version = null);
}
