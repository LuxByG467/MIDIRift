# Foundation 4 — Seek ANR fix

## Root cause

Lyra's seek reconstruction rendered from sample 0 to the requested target while
holding `_stateLock` for the entire operation. UI paths such as the periodic
WaveType refresh also acquired that lock, so dense/long seeks could block the
Android main thread long enough to trigger ANR.

The seek generation in MainPage prevented stale results from being applied but
did not prevent multiple stale seek jobs from continuing to run.

## Fix

- Lyra reconstructs in chunks of at most 8192 frames per `_stateLock` hold.
- The lock is released/yielded between chunks.
- `GetWaveTypes()` is a lock-free fixed-array snapshot.
- MainPage serializes MIDI seeks with `_midiSeekSerial`.
- Waiting stale seeks are coalesced and never execute.
- Tracker UI polling is suspended while a MIDI seek is in progress.

This keeps the current accurate reconstruction behavior while preventing seek
work from monopolizing the Android UI thread indirectly.
