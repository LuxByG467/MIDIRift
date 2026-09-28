namespace MIDIRift.Modules.Themes;

public interface IThemeProvider
{
    string Id { get; }
    ThemeDefinition Definition { get; }
}
