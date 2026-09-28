using Microsoft.Maui.Controls;

namespace MIDIRift.Modules.UI;

/// <summary>
/// Internal compiled visualizer module. Stage F uses factories rather than
/// reflection/external assemblies, preserving deterministic startup.
/// </summary>
public sealed class BuiltInVisualizerModule : IVisualizerModule
{
    private readonly Func<View> _factory;

    public BuiltInVisualizerModule(
        string id,
        string name,
        Func<View> factory,
        string version = "1.0.0")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        Descriptor = new ModuleDescriptor(id, name, version, ModuleType.Visualizer);
    }

    public ModuleDescriptor Descriptor { get; }

    public void Initialize(IModuleContext context)
        => ArgumentNullException.ThrowIfNull(context);

    public View CreateView() => _factory();

    public void Dispose()
    {
    }
}
