# DSN0.2.5b — String escape compile fix

Fixes CS1009 in `DsnLikePanel.cs` lines 158–174. The live-monitor multiline
text accidentally contained a backslash immediately followed by a physical
newline. Those sequences are now normal C# newline escapes (`\n`) inside the
interpolated strings.

No DSP, playback, Patch Bank, telemetry semantics, or Android package version
was changed.
