using Microsoft.Maui.Storage;

namespace MIDIRift;

public sealed class BassRestorationSettings
{
    public bool Enabled { get; set; }
    public float Intensity { get; set; } = BassRestorationProcessor.DefaultIntensity;
    public float FrequencyHz { get; set; } = BassRestorationProcessor.DefaultFrequencyHz;
    public float Mix { get; set; } = BassRestorationProcessor.DefaultMix;

    public BassRestorationSettings Normalize()
    {
        Intensity = Math.Clamp(Intensity, 0f, 1.5f);
        FrequencyHz = Math.Clamp(FrequencyHz, 45f, 160f);
        Mix = Math.Clamp(Mix, 0f, 1f);
        return this;
    }

    public BassRestorationSettings Clone() => new()
    {
        Enabled = Enabled,
        Intensity = Intensity,
        FrequencyHz = FrequencyHz,
        Mix = Mix,
    };
}

public static class BassRestorationSettingsStore
{
    private const string EnabledKey = "bass_restoration_enabled_v1";
    private const string IntensityKey = "bass_restoration_intensity_v1";
    private const string FrequencyKey = "bass_restoration_frequency_v1";
    private const string MixKey = "bass_restoration_mix_v1";

    public static BassRestorationSettings Load()
    {
        try
        {
            return new BassRestorationSettings
            {
                Enabled = Preferences.Default.Get(EnabledKey, false),
                Intensity = Preferences.Default.Get(IntensityKey, BassRestorationProcessor.DefaultIntensity),
                FrequencyHz = Preferences.Default.Get(FrequencyKey, BassRestorationProcessor.DefaultFrequencyHz),
                Mix = Preferences.Default.Get(MixKey, BassRestorationProcessor.DefaultMix),
            }.Normalize();
        }
        catch
        {
            // Preferences is optional startup state. Defaults are always a
            // valid first-run configuration.
            return new BassRestorationSettings().Normalize();
        }
    }

    public static void Save(BassRestorationSettings settings)
    {
        settings.Normalize();
        Preferences.Default.Set(EnabledKey, settings.Enabled);
        Preferences.Default.Set(IntensityKey, settings.Intensity);
        Preferences.Default.Set(FrequencyKey, settings.FrequencyHz);
        Preferences.Default.Set(MixKey, settings.Mix);
    }
}
