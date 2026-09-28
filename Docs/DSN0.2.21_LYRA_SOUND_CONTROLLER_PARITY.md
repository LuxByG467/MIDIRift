# 0.2.21 — Lyra Sound Controller Parity

Lyra now consumes CC71–CC78 instead of treating the DSN expressive events as no-ops.

- CC71 Resonance: chip-colour resonant edge (not DSN's VCF).
- CC72 Release Time: scales Lyra release behaviour.
- CC73 Attack Time: scales active/new voice attack behaviour.
- CC74 Brightness: lightweight per-voice chip-colour filtering.
- CC75 Decay Time: scales active/new voice decay behaviour.
- CC76 Vibrato Rate: modulates Lyra LFO rate.
- CC77 Vibrato Depth: modulates Lyra pitch-LFO depth.
- CC78 Vibrato Delay: adds delayed vibrato onset.

Neutral value 64/127 preserves the native Lyra character. Brightness/Resonance bypass
their colour filter at neutral and only force the generic voice path when actually used,
preserving the D5/D6 block fast paths for ordinary MIDI.

All eight controller values are now part of ChannelState and ChannelStateSnapshot, so
seek/checkpoint restoration retains them.

This is semantic parity, not DSP homogenization: Lyra remains chip-like and does not
borrow DSN's VCF/patch architecture.
