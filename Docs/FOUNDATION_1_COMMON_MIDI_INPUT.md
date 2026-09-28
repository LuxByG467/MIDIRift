# Foundation-1 — Common MIDI input

## Goal
Separate MIDI interpretation from synthesis. From this phase onward, MIDIRift has
one engine-neutral representation: `CompiledMidiSong`.

Pipeline:

`MIDI file -> MidiCompiler -> CompiledMidiSong -> LegacyMidiAdapter -> Legacy engine`

Lyra will consume the same `CompiledMidiSong` in the next foundation phases.

## Guarantees
- NAudio types stop at `MidiCompiler`.
- Tempo map is global and shared.
- Channel events are merged by MIDI logical channel, not physical track.
- Only channels with valid NoteOn velocity > 0 are rendered by Legacy.
- Non-audio metadata is preserved separately and never creates visible/audio channels.
- Stable event ordering is preserved with `Sequence` for same-tick events.
- Later PatchChange events are preserved even though Legacy intentionally keeps its
  historical first-patch behavior.
- Legacy `MidiStep` generation is isolated in `LegacyMidiAdapter`.
- `MidiTranslator` remains only as a compatibility facade.

## Regression rule
Foundation-1 is not allowed to change Legacy's sound. Its adapter intentionally
reproduces the previous `MidiTranslator.ParseTrack` rules, including controller
scaling, tempo timing, NoteOff-before-NoteOn behavior and final-step duration.

## Next
Foundation-2 can bring Lyra Core into this branch and consume `CompiledMidiSong`
without introducing a second MIDI parser.

## Common engine contract
`ChiptuneEngineInput` contains only `CompiledMidiSong` plus generic per-channel
WaveType/UserGain settings. `IChiptuneEngineFactory` accepts this input and returns a
player plus tracker model. The current default implementation is
`LegacyChiptuneEngineFactory`; Lyra can implement the same interface without changing
MainPage's MIDI loading path.
