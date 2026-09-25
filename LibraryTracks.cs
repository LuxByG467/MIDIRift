using System.Text.Json.Serialization;

namespace MIDIRift;

public enum TrackMediaType
{
    Midi,
    Mp3
}

public class LibraryTrack
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("durationSeconds")]
    public float DurationSeconds { get; set; }

    [JsonPropertyName("trackCount")]
    public int TrackCount { get; set; }

    [JsonPropertyName("bpm")]
    public int Bpm { get; set; }

    [JsonPropertyName("checksum")]
    public string Checksum { get; set; } = "";

    [JsonPropertyName("mediaType")]
    public TrackMediaType MediaType { get; set; } = TrackMediaType.Midi;

    // ── Estadísticas de reproducción ──────────────────────────────────────
    [JsonPropertyName("playCount")]
    public int PlayCount { get; set; } = 0;

    [JsonPropertyName("lastPlayedUtc")]
    public DateTime LastPlayedUtc { get; set; }
}