# D6.3 — MIDI auto-advance EOF reliability

## Symptom
A MIDI could finish normally while a playlist still had pending entries, but playback stopped instead of advancing.

## Root architectural weakness
`LyraAudioTrackPlayer.TryFinalizePresentedEnd()` correctly sets `IsFinished = true` and raises `OnStepAdvanced`. `TrackerPlayer.OnEngineStep()` observed that state but only set its private `_finished` flag. The actual `OnFinished` event consumed by `MainPage.OnTrackFinished()` was emitted later by the visual polling Task.

That made queue progression depend on the Tracker render/poll loop. If that loop stopped or faulted before EOF, the audio engine could finish correctly without `MainPage` ever receiving EOF, therefore `PlaylistController.NextAuto()` was never reached.

## Fix
`TrackerPlayer.OnEngineStep()` now dispatches completion immediately when the engine reports `IsFinished`. A new interlocked `NotifyFinishedOnce()` gate guarantees exactly one `OnFinished` notification even if both the engine event and the polling fallback observe EOF.

The polling check remains as a fallback, but is no longer the primary EOF path.

## Scope
No synthesis, scheduler, ADSR, playlist ordering, shuffle, EQ, visual DSP, or Android package version changes.
