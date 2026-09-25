using Microsoft.Maui.Storage;

namespace MIDIRift;

public enum StartupPlaybackBehavior
{
    EmptyPlayer = 0,
    RestoreLastTrack = 1,
}

public static class StartupPlaybackSettings
{
    private const string BehaviorKey = "startup_playback_behavior";
    private const string LastTrackPathKey = "startup_last_track_path";
    private const string LastPlaylistIdKey = "startup_last_playlist_id";
    private const string LastPlaylistNameKey = "startup_last_playlist_name";
    private const string LastTrackIdKey = "startup_last_track_id";
    private const string LastEntryIndexKey = "startup_last_entry_index";

    public static StartupPlaybackBehavior Behavior
    {
        get
        {
            int value = Preferences.Default.Get(BehaviorKey, (int)StartupPlaybackBehavior.RestoreLastTrack);
            return Enum.IsDefined(typeof(StartupPlaybackBehavior), value)
                ? (StartupPlaybackBehavior)value
                : StartupPlaybackBehavior.RestoreLastTrack;
        }
        set => Preferences.Default.Set(BehaviorKey, (int)value);
    }

    public static string? LastTrackPath
    {
        get
        {
            string value = Preferences.Default.Get(LastTrackPathKey, string.Empty);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                Preferences.Default.Remove(LastTrackPathKey);
            else
                Preferences.Default.Set(LastTrackPathKey, value);
        }
    }


    public static string? LastPlaylistId
    {
        get => GetNullableString(LastPlaylistIdKey);
        set => SetNullableString(LastPlaylistIdKey, value);
    }

    public static string? LastPlaylistName
    {
        get => GetNullableString(LastPlaylistNameKey);
        set => SetNullableString(LastPlaylistNameKey, value);
    }

    public static string? LastTrackId
    {
        get => GetNullableString(LastTrackIdKey);
        set => SetNullableString(LastTrackIdKey, value);
    }

    public static int LastEntryIndex
    {
        get => Preferences.Default.Get(LastEntryIndexKey, -1);
        set
        {
            if (value < 0) Preferences.Default.Remove(LastEntryIndexKey);
            else Preferences.Default.Set(LastEntryIndexKey, value);
        }
    }

    public static void ClearPlaylistContext()
    {
        LastPlaylistId = null;
        LastPlaylistName = null;
        LastTrackId = null;
        LastEntryIndex = -1;
    }

    private static string? GetNullableString(string key)
    {
        string value = Preferences.Default.Get(key, string.Empty);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static void SetNullableString(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) Preferences.Default.Remove(key);
        else Preferences.Default.Set(key, value);
    }
}
