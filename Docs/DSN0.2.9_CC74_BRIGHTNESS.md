# DSN0.2.9 — MIDI CC74 Brightness

Adds MIDI CC74 (Brightness) to DSN-like.

Semantics:
- CC74 is transient MIDI modulation, not a Patch Bank edit.
- MIDI value 64 is approximately neutral and preserves the patch cutoff.
- 0..127 maps logarithmically to approximately -2..+2 octaves around the
  patch's base VCF cutoff.
- Existing envelope-to-cutoff and LFO-to-cutoff modulation remain additive in
  the patch domain before the CC74 multiplier.
- Active melodic voices respond to CC74; future voices inherit the current
  channel brightness.
- Percussion ignores CC74.
- Reset restores neutral brightness.
- Lyra parses the shared event but intentionally ignores it for now because
  its current architecture has no equivalent DSN VCF route.

No Patch Bank, Drum Kit, gain staging, specialized kernels, buffers, or Android
package versions changed.
