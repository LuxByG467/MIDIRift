using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MIDIRift;

public class Playlist
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Nueva Playlist";

    [JsonPropertyName("entries")]
    public List<PlaylistEntry> Entries { get; set; } = new();

    /// <summary>
    /// Las playlists virtuales se reconstruyen en memoria al arrancar desde
    /// los datos de Library. Nunca se serializan a playlists.json.
    /// </summary>
    [JsonIgnore]
    public bool IsVirtual { get; set; } = false;
}