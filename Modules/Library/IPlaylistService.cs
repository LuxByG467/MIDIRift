namespace MIDIRift.Modules.Library;
public interface IPlaylistService
{
    IReadOnlyList<Playlist> Playlists { get; }
    Playlist? ActivePlaylist { get; }
    int ActivePlaylistIndex { get; }
    Playlist CreatePlaylist(string? name = null);
    void DeletePlaylist(int index);
    void SelectPlaylist(int index);
    void RenamePlaylist(int index, string name);
    void SelectEntry(int index);
    void AddEntry(int playlistIndex, string mediaPath);
    void RemoveEntry(int playlistIndex, int entryIndex);
    void RenameEntry(int playlistIndex, int entryIndex, string title);
    event Action? PlaylistsChanged;
}
