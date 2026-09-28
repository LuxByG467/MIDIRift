namespace MIDIRift.Modules.Themes;

/// <summary>Compatibility adapter over AppThemeSettings/ThemePalette.</summary>
public sealed class MauiThemeService : IThemeService, IDisposable
{
    public MauiThemeService() => ThemePalette.Changed += OnPaletteChanged;

    public string CurrentThemeId => AppThemeSettings.Current switch
    {
        MIDIRiftTheme.Light => "midirift.theme.light",
        MIDIRiftTheme.Dark => "midirift.theme.dark",
        _ => "midirift.theme.system",
    };

    public bool IsLight => ThemePalette.IsLight;
    public event Action? Changed;

    public void Apply(string themeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeId);
        AppThemeSettings.Current = themeId.Trim().ToLowerInvariant() switch
        {
            "midirift.theme.light" => MIDIRiftTheme.Light,
            "midirift.theme.dark" => MIDIRiftTheme.Dark,
            "midirift.theme.system" => MIDIRiftTheme.System,
            _ => throw new KeyNotFoundException($"Unknown MIDIRift theme '{themeId}'."),
        };
    }

    private void OnPaletteChanged(AppTheme _) => Changed?.Invoke();
    public void Dispose() => ThemePalette.Changed -= OnPaletteChanged;
}
