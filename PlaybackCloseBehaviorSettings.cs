using Microsoft.Maui.Storage;

namespace MIDIRift;

public enum PlaybackCloseBehavior
{
    ContinuePlaying = 0,
    StopImmediately = 1,
    ContinueUntilFinished = 2,
}

public static class PlaybackCloseBehaviorSettings
{
    private const string PreferenceKey = "playback_close_behavior";

    public static PlaybackCloseBehavior Current
    {
        get
        {
            int raw = Preferences.Get(PreferenceKey, (int)PlaybackCloseBehavior.ContinueUntilFinished);
            return Enum.IsDefined(typeof(PlaybackCloseBehavior), raw)
                ? (PlaybackCloseBehavior)raw
                : PlaybackCloseBehavior.ContinueUntilFinished;
        }
        set => Preferences.Set(PreferenceKey, (int)value);
    }
}
