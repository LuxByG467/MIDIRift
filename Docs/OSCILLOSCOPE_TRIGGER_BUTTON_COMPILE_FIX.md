# Oscilloscope Trigger Button compile fix

Fix for SkiaSharp 4.151.x API compatibility in the enlarged Trigger button.

- Removed obsolete `SKPaint.TextSize`, `SKPaint.Typeface`, and `SKPaint.FontMetrics` usage.
- Text sizing/typeface now live in `SKFont`, matching the rest of MIDIRift Android.
- Vertical centering uses `SKFont.Metrics`.
- Drawing uses the SkiaSharp 4.x `DrawText(..., SKFont, SKPaint)` overload.
- No audio, trigger detection, capture, FFT, or playback behavior changed.
