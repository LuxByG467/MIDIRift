# Foundation 4 — Audio Presentation Clock

The visual panels no longer follow Lyra's producer/writer cursor.

## Clock source

- AAudio Native RT: native callback FIFO `readIndex` exposed as `PresentedFrames`.
- AudioTrack: Android `PlaybackHeadPosition`, including uint32 wrap handling.
- `LyraAudioTrackPlayer.VirtualSample` = backend presentation epoch + presented frames.

## Visual capture

FFT, oscilloscope and tracker metrics read capture-ring windows ending at
`VirtualSample`, not the newest PCM produced by Lyra. Capture rings are indexed
by absolute song sample.

## EOF

Writing the last PCM block no longer immediately sets `IsFinished`. MIDIRift
waits until the backend presentation clock reaches the end of that block, then
fires the natural completion/next-track path.

This preserves a large anti-underrun PCM cushion without making visuals run
ahead of audible audio.
