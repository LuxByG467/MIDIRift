# Oscilloscope Trigger render performance fix — 0.12.4-alpha

Regression introduced in 0.12.0: `DrawTriggerToggle()` allocated Skia objects on every oscilloscope frame. Later button usability work increased this to paints + typeface + font allocations per frame. On dense MIDI passages this GC/render pressure can steal scheduling time from Lyra and expose underruns.

Fix:
- Cache trigger fill/stroke/text `SKPaint` instances.
- Cache bold `SKTypeface` and `SKFont`.
- Use `SKCanvas.DrawRoundRect(SKRect, ...)` directly, avoiding per-frame `SKRoundRect`.
- Per-frame work is now only value-type color updates, font metric read and draw calls.
- Trigger detection/hysteresis, button hit testing, Lyra gain and speed fixes are unchanged.

The 0.11.9 -> 0.12.0 source diff confirms no audio-engine code changed in 0.12.0; the new trigger-button draw path was the only new continuously executed work while the oscilloscope is visible.
