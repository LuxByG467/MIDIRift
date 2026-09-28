# Modular Architecture — Stage E

Cumulative checkpoint: A + B + C + D + E.

Stage E introduces explicit boundaries for queue, library, playlists and scanning.

## Contracts
- IPlaybackQueue
- ILibraryService
- IPlaylistService
- ILibraryScanner

## Queue ownership
MainPage now uses IPlaybackQueue for Previous, Next, EOF auto-advance, shuffle,
playlist looping and current-entry synchronization. The D6.3 EOF generation guard
remains intact; after it accepts one EOF, advancement goes through
IPlaybackQueue.AdvanceAfterFinished().

## Compatibility
The four adapters share the existing PlaylistController as backing storage, preserving
JSON formats, virtual playlists, favorites, play statistics and scanning behavior.
LibraryPage/PlaylistPage stay on the compatibility controller for this checkpoint so
queue migration can be isolated from the larger UI migration.

No audio/DSP/render hot path is changed.
