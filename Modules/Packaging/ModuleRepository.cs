namespace MIDIRift.Modules.Packaging;

public sealed class ModuleRepository : IModuleRepository
{
    private readonly IReadOnlyList<ModuleRepositoryEntry> _entries;

    public ModuleRepository(string id, IEnumerable<ModuleRepositoryEntry> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(entries);
        Id = id;
        _entries = entries.ToArray();
    }

    public string Id { get; }
    public IReadOnlyList<ModuleRepositoryEntry> Entries => _entries;

    public ModuleRepositoryEntry? Find(string packageId, string? version = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        return _entries.FirstOrDefault(e =>
            string.Equals(e.PackageId, packageId, StringComparison.OrdinalIgnoreCase) &&
            (version is null || string.Equals(e.Version, version, StringComparison.OrdinalIgnoreCase)));
    }
}
