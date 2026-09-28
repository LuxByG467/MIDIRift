namespace MIDIRift.Modules.Library;
public interface IPlaybackQueue
{
    Playlist? ActivePlaylist { get; }
    int CurrentEntryIndex { get; }
    bool HasPrevious { get; }
    bool HasNext { get; }
    bool IsShuffling { get; }
    void SetLooping(bool looping);
    void SetShuffling(bool shuffling);
    void SyncCurrentEntry(string trackId);
    LibraryTrack? Previous();
    LibraryTrack? Next();
    LibraryTrack? AdvanceAfterFinished();
}
