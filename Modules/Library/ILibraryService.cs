namespace MIDIRift.Modules.Library;
public interface ILibraryService
{
    IReadOnlyList<LibraryTrack> Tracks { get; }
    LibraryTrack? Resolve(PlaylistEntry entry);
    LibraryTrack? AddToLibrary(string mediaPath);
    bool IsFavorite(string trackId);
    void ToggleFavorite(string trackId);
    event Action? LibraryChanged;
}
