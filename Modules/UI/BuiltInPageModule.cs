using Microsoft.Maui.Controls;

namespace MIDIRift.Modules.UI;

/// <summary>
/// Internal compiled page module with optional navigation contribution.
/// </summary>
public sealed class BuiltInPageModule : IPageModule, INavigationContribution
{
    private readonly Func<Page> _factory;

    public BuiltInPageModule(
        string id,
        string name,
        string route,
        string title,
        int order,
        bool isPrimary,
        Func<Page> factory,
        string version = "1.0.0")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        Descriptor = new ModuleDescriptor(id, name, version, ModuleType.Page);
        Route = route;
        Title = title;
        Order = order;
        IsPrimary = isPrimary;
    }

    public ModuleDescriptor Descriptor { get; }
    public string Route { get; }
    public string Title { get; }
    public int Order { get; }
    public bool IsPrimary { get; }

    public void Initialize(IModuleContext context)
        => ArgumentNullException.ThrowIfNull(context);

    public Page CreatePage() => _factory();

    public void Dispose()
    {
    }
}
