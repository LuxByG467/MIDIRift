namespace MIDIRift.Modules;

public sealed class ModuleManager : IModuleManager
{
    private readonly IModuleContext _context;
    private readonly List<IMidiRiftModule> _initialized = new();
    private bool _disposed;

    public ModuleManager(IModuleRegistry registry, IModuleContext context)
    {
        Registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public IModuleRegistry Registry { get; }
    public bool IsInitialized { get; private set; }

    public void Register(IMidiRiftModule module)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsInitialized)
            throw new InvalidOperationException(
                "Stage-A ModuleManager does not allow registration after initialization.");

        Registry.Register(module);
    }

    public void InitializeAll()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsInitialized)
            return;

        try
        {
            foreach (var module in Registry.Modules)
            {
                module.Initialize(_context);
                _initialized.Add(module);
            }

            IsInitialized = true;
        }
        catch
        {
            DisposeInitializedModules();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        DisposeInitializedModules();
        IsInitialized = false;
    }

    private void DisposeInitializedModules()
    {
        for (int i = _initialized.Count - 1; i >= 0; i--)
        {
            try
            {
                _initialized[i].Dispose();
            }
            catch
            {
                // Best-effort cleanup: one broken module must not block the rest.
            }
        }

        _initialized.Clear();
    }
}
