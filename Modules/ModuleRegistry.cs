namespace MIDIRift.Modules;

public sealed class ModuleRegistry : IModuleRegistry
{
    private readonly Dictionary<string, IMidiRiftModule> _byId =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IMidiRiftModule> _ordered = new();
    private readonly object _gate = new();

    public IReadOnlyCollection<IMidiRiftModule> Modules
    {
        get
        {
            lock (_gate)
                return _ordered.ToArray();
        }
    }

    public void Register(IMidiRiftModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        string id = module.Descriptor.Id?.Trim()
            ?? throw new InvalidOperationException("A module descriptor must have an Id.");

        if (id.Length == 0)
            throw new InvalidOperationException("A module descriptor Id cannot be empty.");

        lock (_gate)
        {
            if (_byId.ContainsKey(id))
                throw new InvalidOperationException($"Module '{id}' is already registered.");

            _byId.Add(id, module);
            _ordered.Add(module);
        }
    }

    public bool TryGet(string moduleId, out IMidiRiftModule? module)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        lock (_gate)
            return _byId.TryGetValue(moduleId, out module);
    }

    public TModule? Get<TModule>(string moduleId) where TModule : class, IMidiRiftModule
        => TryGet(moduleId, out var module) ? module as TModule : null;
}
