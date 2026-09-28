# DSN0.2.12 — CC78 Vibrato Delay + CC121 Reset All Controllers

## CC78 Vibrato Delay
A real per-voice delay/ramp was added rather than mapping CC78 to an unrelated
parameter. Value 64 is neutral (no additional delay). Values above neutral add
up to about 2 seconds before pitch-LFO modulation enters, followed by an ~80 ms
ramp to avoid a hard vibrato edge. The state advances during normal playback
and seek reconstruction.

## CC121 Reset All Controllers
Resets transient channel controller state without touching Program/Patch Bank
or user mixer settings. DSN resets pitch bend, modulation, CC71/72/73/74/75/76/
77/78 and portamento/legato/sustain-related channel state to defaults. Active
DSN voices receive the reset immediately. Sustain-pending voices are released
when the controller reset drops sustain.

Lyra accepts CC121 through the common channel-state reset; CC78 remains a
DSN-specific expressive control.

No Patch Bank recipes, Drum Kit recipes, specialized render kernels, buffers,
gain staging, or Android package versions changed.
