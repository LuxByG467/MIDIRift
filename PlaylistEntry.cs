using System.Text.Json.Serialization;

namespace MIDIRift;

public class PlaylistEntry
{
    [JsonPropertyName("trackId")]
    public string TrackId { get; set; } = "";

    // Título personalizado — si está vacío usa el de LibraryTrack
    [JsonPropertyName("customTitle")]
    public string? CustomTitle { get; set; }
}