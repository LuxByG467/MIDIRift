namespace MIDIRift.Modules.Library;
public sealed class PlaylistServiceAdapter : IPlaylistService
{
    private readonly PlaylistController _controller;
    public PlaylistServiceAdapter(PlaylistController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _controller.PlaylistsChanged += () => PlaylistsChanged?.Invoke();
    }
    public IReadOnlyList<Playlist> Playlists => _controller.Playlists;
    public Playlist? ActivePlaylist => _controller.ActivePlaylist;
    public int ActivePlaylistIndex => _controller.ActivePlaylistIndex;
    public event Action? PlaylistsChanged;
    public Playlist CreatePlaylist(string? name = null) => _controller.CreatePlaylist(name);
    public void DeletePlaylist(int index) => _controller.DeletePlaylist(index);
    public void SelectPlaylist(int index) => _controller.SelectPlaylist(index);
    public void RenamePlaylist(int index, string name) => _controller.RenamePlaylist(index, name);
    public void SelectEntry(int index) => _controller.SelectEntry(index);
    public void AddEntry(int playlistIndex, string mediaPath) => _controller.AddEntry(playlistIndex, mediaPath);
    public void RemoveEntry(int playlistIndex, int entryIndex) => _controller.RemoveEntry(playlistIndex, entryIndex);
    public void RenameEntry(int playlistIndex, int entryIndex, string title) => _controller.RenameEntry(playlistIndex, entryIndex, title);
}
