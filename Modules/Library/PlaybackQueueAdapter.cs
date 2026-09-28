namespace MIDIRift.Modules.Library;
public sealed class PlaybackQueueAdapter : IPlaybackQueue
{
    private readonly PlaylistController _controller;
    public PlaybackQueueAdapter(PlaylistController controller) => _controller = controller ?? throw new ArgumentNullException(nameof(controller));
    public Playlist? ActivePlaylist => _controller.ActivePlaylist;
    public int CurrentEntryIndex => _controller.CurrentEntryIndex;
    public bool HasPrevious => _controller.HasPrev;
    public bool HasNext => _controller.HasNext;
    public bool IsShuffling => _controller.IsShuffling;
    public void SetLooping(bool looping) => _controller.SetLooping(looping);
    public void SetShuffling(bool shuffling) => _controller.SetShuffling(shuffling);
    public void SyncCurrentEntry(string trackId) => _controller.SyncCurrentEntry(trackId);
    public LibraryTrack? Previous() => _controller.Prev();
    public LibraryTrack? Next() => _controller.Next();
    public LibraryTrack? AdvanceAfterFinished() => _controller.NextAuto();
}
