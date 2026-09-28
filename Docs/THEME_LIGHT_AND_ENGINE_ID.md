# 0.11.7-alpha — Engine identity + light visual panels

- The MIDI engine shown to the user comes from `_currentMidiEngineKind`, assigned only after `ChiptunePlayerFactory.Create(...)` successfully creates the engine for the current MIDI. The Now Playing status now names `Lyra` or `Classic / Legacy`; the Library mini-player already receives the same value through PlaybackBridge.
- ThemePalette now publishes the effective theme and a Changed event.
- MainPage updates top-bar icon assets on a live theme change.
- The Settings gear is now a centered SVG instead of a font glyph, avoiding clipping/baseline differences across Android fonts.
- Tracker, FFT/Spectrum and Oscilloscope have explicit light palettes. Tracker invalidates its bitmap/header caches when the palette changes; FFT/Oscilloscope invalidate their next frame.
- Hidden visual panels are not given new timers or render loops. A theme change only changes palette state/invalidation, so the active Now Playing panel repaints with the correct palette when it is shown.
