# Foundation-2 — Lyra Core port

Foundation-2 ports the portable Lyra synthesis core from the Clean Room branch
into the recovered stable MIDIRift Android branch.

- `Lyra/Core` contains the portable synthesis/data layer.
- `LyraCompiledSongAdapter` converts Foundation-1 `ChiptuneEngineInput` into
  Lyra's internal compiled representation.
- The adapter never reopens or reparses the MIDI file.
- `LyraCoreFactory` creates channel state, voice pool, and scheduler.
- Legacy remains the active playback engine in Foundation-2.
- MIDI channel 10 in the common model maps to internal Lyra channel 9 and is
  marked as percussion.
- Poly Aftertouch and Channel Aftertouch remain preserved by Foundation-1 but
  are intentionally ignored by Lyra until Lyra has explicit runtime actions for
  those event types.

Foundation-3 will connect this core to the common playback contract.
