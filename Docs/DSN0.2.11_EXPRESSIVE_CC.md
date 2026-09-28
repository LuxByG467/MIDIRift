# DSN0.2.11 — Expressive MIDI CC pass

Added to DSN-like:
- CC72 Release Time
- CC73 Attack Time
- CC75 Decay Time
- CC76 Vibrato Rate
- CC77 Vibrato Depth

All five are transient MIDI modulation and never edit Patch Bank data.
Value 64 is approximately neutral.

Envelope timing controls use a logarithmic scale around each patch's base time:
the full controller range is approximately 0.25x..4x. Active voices are
reconfigured and future voices inherit the current channel values.

CC76 scales the patch LFO rate by approximately 0.5x..2x.
CC77 adds approximately -1..+1 semitone of LFO pitch depth around the patch
routing. CC1 Mod Wheel remains independent and can contribute additional
vibrato depth.

CC71 Resonance and CC74 Brightness remain supported and independent.
Percussion ignores these melodic synthesis controls. Lyra accepts the shared
compiled events but intentionally ignores them.

CC78 Vibrato Delay is intentionally deferred because it needs an actual
per-voice delay/ramp state rather than a fake static mapping.
