# Modular Architecture — Stage D

Cumulative checkpoint: A + B + C + D.

Stage D establishes the playback boundary without changing the audio render path or
moving queue ownership prematurely.

## Contracts
- `IPlaybackTransport`
- `IPlaybackTimeline`
- `IPlaybackState`
- `IPlaybackCommands`
- `IPlaybackService`

Supporting:
- `PlaybackStatus`
- immutable `PlaybackSnapshot`
- `PlaybackService`
- `MidiPlaybackSessionAdapter`
- `Mp3PlaybackSessionAdapter`

## Compatibility migration
`PlaybackBridge` is now a compatibility facade over `PlaybackService`.
LibraryPage/MainPage source that still uses the old mutable bridge API remains valid,
but every NotifyChanged publishes an immutable PlaybackSnapshot through
IPlaybackState.

The high-level command side now supports PlayTrack, TogglePause, Next, Previous and
SeekTo. MainPage subscribes to these requests as the temporary execution host.

## Important boundary
Stage D does NOT yet move queue ownership or full session ownership out of MainPage.
That is intentional. Stage E introduces IPlaybackQueue and library/playlist
boundaries; moving EOF/NextAuto before that would mix two high-risk migrations in one
checkpoint.

## EOF rule
`IPlaybackTimeline.Finished` belongs to the playback session, never to a visualizer.
The D6.3 Tracker EOF fix remains untouched. The new MIDI/MP3 session adapters expose
the correct future contract without being inserted into the proven runtime path yet.

## Performance
No DSP, synthesis, backend, FFT, oscilloscope or tracker hot path is changed.
