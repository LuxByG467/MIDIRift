# DSN0.2.17 — CC66 Sostenuto + CC67 Soft Pedal

Adds two MIDI pedal controllers.

## CC66 Sostenuto
DSN-like implements actual sostenuto semantics:
- pedal-down captures only melodic voices that are physically held at that moment;
- notes started after pedal-down are not captured;
- captured voices can survive their NoteOff;
- pedal-up releases captured voices that are no longer physically/sustain-held.

## CC67 Soft Pedal
DSN-like applies a transient 0.72 voice-output multiplier while the pedal is
down. It affects active and future melodic voices without modifying or saving
Patch Bank data. This is deliberately conservative until engine-specific
timbre softening is designed.

Lyra records both controller states in ChannelState so parsing/reset/seek state
remains coherent, but this checkpoint does not fake Lyra voice-level sostenuto
or soft-pedal DSP.

CC121 resets both pedal states. Percussion ignores these melodic pedal effects.
