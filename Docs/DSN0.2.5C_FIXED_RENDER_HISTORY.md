# DSN0.2.5c — Fixed 30 s render history

Replaces the text-character render history with a fixed-height `GraphicsView`.

- 120 samples = 30 seconds at the monitor's 4 Hz refresh rate.
- Fixed 0–200% vertical scale.
- Persistent 100% deadline guide.
- Oldest samples scroll out; the control never grows or wraps.
- Labels show -30s and NOW.
- History clears when no DSN telemetry source is active.

This changes monitor presentation only. DSP, playback, Patch Bank and Android
package versions are unchanged.
