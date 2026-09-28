namespace MIDIRift.Modules.Themes;

public interface IThemeService
{
    string CurrentThemeId { get; }
    bool IsLight { get; }
    event Action? Changed;
    void Apply(string themeId);
}
