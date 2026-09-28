namespace MIDIRift.Modules.Packaging;

/// <summary>
/// In-memory Stage-H policy store. Persistence/UI approval can be added later
/// without changing module-facing contracts.
/// </summary>
public sealed class ModulePermissionService : IModulePermissionService
{
    private readonly Dictionary<string, HashSet<ModulePermission>> _grants =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public bool IsGranted(string moduleId, ModulePermission permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        lock (_gate)
            return _grants.TryGetValue(moduleId, out var set) && set.Contains(permission);
    }

    public IReadOnlyCollection<ModulePermission> GetGranted(string moduleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        lock (_gate)
            return _grants.TryGetValue(moduleId, out var set)
                ? set.ToArray()
                : Array.Empty<ModulePermission>();
    }

    public void SetGrant(string moduleId, ModulePermission permission, bool granted)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        lock (_gate)
        {
            if (!_grants.TryGetValue(moduleId, out var set))
                _grants[moduleId] = set = new HashSet<ModulePermission>();

            if (granted) set.Add(permission);
            else set.Remove(permission);

            if (set.Count == 0)
                _grants.Remove(moduleId);
        }
    }

    public void RevokeAll(string moduleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        lock (_gate)
            _grants.Remove(moduleId);
    }
}
