using Microsoft.Maui.Storage;

namespace MIDIRift;

/// <summary>
/// Preferencia visual independiente del engine. Se aplica inmediatamente y
/// persiste entre arranques sin obligar a reconstruir el reproductor.
/// </summary>
public static class MeasurementLabelsSettings
{
    private const string PreferenceKey = "show_measurement_labels";

    public static event Action<bool>? Changed;

    public static bool Enabled
    {
        get => Preferences.Default.Get(PreferenceKey, true);
        set
        {
            bool previous = Enabled;
            Preferences.Default.Set(PreferenceKey, value);
            if (previous != value) Changed?.Invoke(value);
        }
    }
}
