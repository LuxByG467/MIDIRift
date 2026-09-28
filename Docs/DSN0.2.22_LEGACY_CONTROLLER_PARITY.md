# 0.2.22 — Legacy Controller Parity

The original/Legacy architecture receives the controller set that was still
missing from its precompiled MidiStep model.

Added:
- CC67 Soft Pedal: transient 0.72x channel gain; no patch mutation.
- CC75 Decay Time: normalized controller converted at step boundaries and
  stamped into Legacy ADSR.
- CC77 Vibrato Depth: augments the existing CC1/LFO modulation depth.
- CC78 Vibrato Delay: delays/ramp-enables pitch LFO after a tonal NoteOn.
- CC84 Portamento Control: one-shot explicit source MIDI note for the next
  tonal NoteOn. CC65/CC5 keep their existing switch/time semantics; with CC65
  off an explicit CC84 source uses a 5 ms compatibility glide.

Legacy already had real Sustain, Sostenuto, Legato, Brightness, Resonance,
Attack, Release, Vibrato Rate, Channel Aftertouch and Poly Aftertouch.

This checkpoint intentionally leaves RPN 0,0 / 0,1 / 0,2 for the next Legacy
parity stage. Legacy's historical pitch-bend conversion is still fixed at ±2
semitones here.

Seek remains deterministic because Legacy reconstructs state by silent
fast-forward through its MidiStep timeline rather than Lyra-style checkpoints.
