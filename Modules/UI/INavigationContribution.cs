namespace MIDIRift.Modules.UI;

/// <summary>
/// Declarative navigation metadata supplied by a page module.
/// The host decides how that contribution is rendered.
/// </summary>
public interface INavigationContribution
{
    string Route { get; }
    string Title { get; }
    int Order { get; }
    bool IsPrimary { get; }
}
