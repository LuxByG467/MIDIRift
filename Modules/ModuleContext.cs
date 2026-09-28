namespace MIDIRift.Modules;

public sealed class ModuleContext : IModuleContext
{
    private readonly Dictionary<Type, object> _capabilities = new();
    private readonly object _gate = new();

    public void Provide<T>(T capability) where T : class
    {
        ArgumentNullException.ThrowIfNull(capability);
        lock (_gate)
            _capabilities[typeof(T)] = capability;
    }

    public T GetCapability<T>() where T : class
    {
        if (TryGetCapability<T>(out var capability))
            return capability;

        throw new InvalidOperationException(
            $"MIDIRift capability '{typeof(T).FullName}' is not available.");
    }

    public bool TryGetCapability<T>(out T? capability) where T : class
    {
        lock (_gate)
        {
            if (_capabilities.TryGetValue(typeof(T), out var value) && value is T typed)
            {
                capability = typed;
                return true;
            }
        }

        capability = null;
        return false;
    }
}
