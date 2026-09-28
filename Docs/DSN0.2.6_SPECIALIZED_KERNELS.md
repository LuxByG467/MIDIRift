# DSN0.2.6 — Specialized render kernels

First real-world kernel pass driven by the Performance Monitor.

Hall of the Mountain King showed the sustained hot route as:
Dual VCO + filter + drive, while FM=0 and Sync=0.

Changes:
- Adds a compiled `DriveOnly` voice route.
- Drive-only voices no longer execute FM/sync/use-drive branches per sample.
- Adds a specialized Sustain Dual-VCO + LowPass + Drive kernel.
- Saw+Pulse and Pulse+Saw pairs keep oscillator phases in locals and avoid
  generic waveform dispatch.
- Other LP waveform pairs still use a cheaper drive-only path.
- HP/BP drive-only patches get a separate fallback without generic FM/sync logic.
- Control updates remain chunked at the existing ControlInterval.
- No Patch Bank, gain staging, buffer sizing, Android package version, or MIDI
  semantics changed.

Primary acceptance test:
In The Hall of the Mountain King, compare the same dense passages against
DSN0.2.5c using the Performance Monitor. Watch Render %, Late, Underruns,
Backend fill, and active feature counts.
