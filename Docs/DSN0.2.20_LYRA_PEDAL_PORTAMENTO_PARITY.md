# 0.2.20 — Lyra Pedal + Portamento Parity

Lyra now gives real synthesis semantics to CC66, CC67 and CC84.

- CC66 Sostenuto captures only melodic voices whose keys are held at pedal-down.
  NoteOff does not release captured voices. Pedal-up releases eligible captured
  voices while respecting Sustain.
- CC67 Soft Pedal is a transient chip-like gain treatment (0.72x) applied in
  Lyra's mixer. It affects active and future voices without mutating patches.
- CC84 Portamento Control is consumed by the next melodic NoteOn and initializes
  that voice's glide from the explicit MIDI source note. CC5/CC65 retain their
  existing time/switch roles. With CC84 and CC65 off, the explicit transition
  uses the same short 5 ms compatibility glide as DSN-like.
- CC121 clears controller-held Sustain/Sostenuto voice state as well as ChannelState.
- Percussion ignores Sostenuto capture and does not consume CC84.

This checkpoint deliberately does not add CC71-78 sound-controller parity; that
is the next Lyra parity stage.
