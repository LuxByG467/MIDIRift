# D6.2 - Duplicate NoteOff voice pairing

Base: 0.12.4D6.1-alpha.

## Problem

`VoicePool.ReleaseNote()` previously called `HandleNoteOff()` for every active voice matching the same channel and MIDI note. Overlapping NoteOn instances therefore collapsed into Release together when only one NoteOff arrived. This can create an audible grouped release/cut artifact, especially with Pulse50/Square material.

## Fix

A NoteOff now consumes exactly one still-`KeyHeld` voice for the matching channel/note. Pairing is FIFO using the smallest `StartSequence`, matching the oldest outstanding NoteOn first. Voices already released or sustain-held are skipped, so later NoteOff events consume later outstanding instances.

Sustain semantics remain intact: the selected voice becomes sustain-held when the pedal is active; `ReleasePending()` releases all voices whose individual NoteOff has already been consumed when sustain is lifted.

No allocations, collections, logging, audio-buffer changes, DSP changes, or Android package-version changes were added. D6/D6.1 block kernels are unchanged.
