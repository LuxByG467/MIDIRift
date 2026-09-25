namespace MIDIRift;

public enum MIDIRiftTheme
{
    System,
    Light,
    Dark,
}

public static class AppThemeSettings
{
    private const string PreferenceKey = "app_theme";

    public static MIDIRiftTheme Current
    {
        get
        {
            string saved = Preferences.Default.Get(PreferenceKey, nameof(MIDIRiftTheme.System));
            return Enum.TryParse(saved, true, out MIDIRiftTheme value) ? value : MIDIRiftTheme.System;
        }
        set
        {
            Preferences.Default.Set(PreferenceKey, value.ToString());
            Apply(value);
        }
    }

    public static void ApplySaved() => Apply(Current);

    public static void Apply(MIDIRiftTheme theme)
    {
        if (Application.Current is null) return;
        Application.Current.UserAppTheme = theme switch
        {
            MIDIRiftTheme.Light => AppTheme.Light,
            MIDIRiftTheme.Dark => AppTheme.Dark,
            _ => AppTheme.Unspecified,
        };
        ThemePalette.Apply(ResolveEffectiveTheme(theme));
    }

    private static AppTheme ResolveEffectiveTheme(MIDIRiftTheme requested)
    {
        if (requested == MIDIRiftTheme.Light) return AppTheme.Light;
        if (requested == MIDIRiftTheme.Dark) return AppTheme.Dark;
        return Application.Current?.RequestedTheme == AppTheme.Light ? AppTheme.Light : AppTheme.Dark;
    }
}

public static class ThemePalette
{
    public static event Action<AppTheme>? Changed;
    public static AppTheme EffectiveTheme { get; private set; } = AppTheme.Dark;
    public static bool IsLight => EffectiveTheme == AppTheme.Light;
    private static readonly IReadOnlyDictionary<string, string> Dark = new Dictionary<string, string>
    {
        ["PageBackground"]="#0D0D0F", ["CanvasBackground"]="#121212", ["SurfaceBackground"]="#12121A", ["SurfaceOverlay"]="#EE12121A",
        ["CardBackground"]="#1A1A2E", ["ControlBackground"]="#2A2A3A", ["BorderColor"]="#2E2A4A", ["BorderStrong"]="#3A3650",
        ["TextPrimary"]="#E8E0FF", ["TextSecondary"]="#C8C0F0", ["TextMuted"]="#9E9AB8", ["TextSubtle"]="#6D687E",
        ["PurplePrimary"]="#4A3FA0", ["PurpleAccent"]="#8A7AE0", ["PurpleLight"]="#C79BE6",
        ["GreenPrimary"]="#50A060", ["GreenAccent"]="#7FD48F", ["GreenSurface"]="#31543A",
        ["Danger"]="#F87171", ["Warning"]="#E0C060", ["Info"]="#4A90D0",
    };

    private static readonly IReadOnlyDictionary<string, string> Light = new Dictionary<string, string>
    {
        ["PageBackground"]="#E9E6EF", ["CanvasBackground"]="#F0EDF4", ["SurfaceBackground"]="#E2DEE8", ["SurfaceOverlay"]="#F2E2DEE8",
        ["CardBackground"]="#F7F5FA", ["ControlBackground"]="#D6D0DF", ["BorderColor"]="#C2B9CE", ["BorderStrong"]="#A89AB9",
        ["TextPrimary"]="#241C31", ["TextSecondary"]="#453756", ["TextMuted"]="#675A76", ["TextSubtle"]="#7C7089",
        ["PurplePrimary"]="#8068D8", ["PurpleAccent"]="#6E55C5", ["PurpleLight"]="#A879C5",
        ["GreenPrimary"]="#3B8B4C", ["GreenAccent"]="#55A965", ["GreenSurface"]="#D9EEDD",
        ["Danger"]="#C63E48", ["Warning"]="#9A7116", ["Info"]="#2D72B8",
    };

    public static void Apply(AppTheme theme)
    {
        EffectiveTheme = theme;
        if (Application.Current?.Resources is ResourceDictionary resources)
        {
            foreach (var pair in theme == AppTheme.Light ? Light : Dark)
                resources[pair.Key] = Color.FromArgb(pair.Value);
        }
        Changed?.Invoke(theme);
    }

    public static Color Get(string key)
        => Application.Current?.Resources.TryGetValue(key, out object? value) == true && value is Color color
            ? color : Colors.Transparent;
}
