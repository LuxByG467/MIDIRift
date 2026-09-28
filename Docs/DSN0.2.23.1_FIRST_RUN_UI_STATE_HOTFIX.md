# 0.2.23.1 — First Run + UI State Hotfix

This stabilization checkpoint intentionally precedes 0.2.24 Aftertouch parity.

## Resume status
`ResumeCurrent()` now restores the same Playing status used by a fresh MIDI/MP3
start. The play/pause icon and the textual status no longer have separate visible
states after Pause -> Resume.

## Contextual DSN GM program selection
Opening the Patch Editor from a tracker channel now has two distinct operations:

- Changing **Programa GM** changes that live channel immediately and selects the
  Factory/User assignment for future notes. It does not persist anything and does
  not create a User Patch.
- **Guardar / Guardar como** remain patch-authoring operations. They persist synth
  parameter edits and may create a User Patch.

`DsnLikeAudioTrackPlayer.SetChannelProgram()` owns the runtime program change.

## First-run / Clear Data hardening
The clean-start path was audited again after the post-0.2.2 feature growth.

- Optional Preferences-based Bass Restoration state now falls back to defaults
  instead of being able to abort MainPage field initialization.
- Startup playback/session restore is isolated: failure to read clean Preferences
  cannot kill Now Playing construction.
- DSN Program Bank and Drum Kit stores are explicitly exercised during bootstrap.
- DSN mutable files now use the canonical `AppDataPaths.Root` instead of mixing
  root-level `FileSystem.AppDataDirectory` files with MIDIRift's own storage tree.
- Existing legacy DSN bank location is still read as a migration source.
- Corrupt drum-kit state is quarantined; missing state is valid.
- DSN drum writes use the common atomic writer.

Acceptance test for hardware:
1. Android Settings -> MIDIRift -> Clear storage/data.
2. Cold-launch MIDIRift.
3. Main UI must appear with no pre-existing files or Preferences.
4. Open DSN engine and Patch Editor without creating a User Bank.
5. Pause a MIDI, resume it, verify status returns to Reproduciendo.
6. Change a channel's GM Program in the contextual Patch Editor; sound for future
   notes must change immediately and no User Patch should be created until Save.
