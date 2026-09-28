# DSN0.2.5a — Performance Monitor compile fix

The 0.2.5 source package accidentally embedded literal `\\n` escape text in
`DsnLikePanel.cs` where actual source newlines were required. This broke the
class at line 138 and caused the large cascade of parser/context errors reported
by Visual Studio.

0.2.5a replaces those literal escape sequences with real source lines. No DSP,
telemetry design, package version, or playback behavior was intentionally
changed.
