# DSN0.2.10 — MIDI CC71 Resonance

Adds MIDI CC71 to DSN-like VCF modulation.

- CC71 value 64 is approximately neutral.
- It is a transient MIDI modulation and never edits the Patch Bank.
- The MIDI control adds an offset around the patch's own resonance.
- Full travel is approximately -0.45..+0.45 resonance, clamped to 0..0.95.
- Active melodic voices react immediately; future voices inherit the current
  channel value.
- Reset returns CC71 to neutral.
- Percussion ignores CC71.
- CC74 Brightness remains independent, so a MIDI can automate cutoff and
  resonance simultaneously.
- Lyra accepts the shared compiled event but intentionally ignores it.

No DSP kernels, Patch Bank recipes, Drum Kit, buffers, gain staging or Android
package version changed.
