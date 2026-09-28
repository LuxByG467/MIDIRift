namespace MIDIRift.Modules.Library;
public sealed class LibraryServiceAdapter : ILibraryService
{
    private readonly PlaylistController _controller;
    public LibraryServiceAdapter(PlaylistController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _controller.PlaylistsChanged += () => LibraryChanged?.Invoke();
    }
    public IReadOnlyList<LibraryTrack> Tracks => _controller.Library;
    public event Action? LibraryChanged;
    public LibraryTrack? Resolve(PlaylistEntry entry) => _controller.Resolve(entry);
    public LibraryTrack? AddToLibrary(string mediaPath) => _controller.AddToLibrary(mediaPath);
    public bool IsFavorite(string trackId) => _controller.IsFavorite(trackId);
    public void ToggleFavorite(string trackId) => _controller.ToggleFavorite(trackId);
}
