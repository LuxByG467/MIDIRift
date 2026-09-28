# MIDIRift 0.11.8-alpha — Light Theme Polish

Second light-theme pass based on device testing.

- Light surfaces now use a restrained cool light-gray hierarchy instead of large pure-white areas.
- PurplePrimary is brighter in Light so player identity/buttons do not look transplanted from Dark.
- Spectrum/Oscilloscope toggle button state now uses DynamicResource instead of hard-coded dark colors.
- Channel MIX overlay, MIX/RESET buttons and faders use ThemePalette and refresh on live theme changes.
- Playlist track picker no longer contains a dark-only local palette; it uses the shared theme resources.
- EqualizerCurveView now owns Dark/Light drawing palettes and reacts to ThemePalette.Changed, including background, grid, tracks, thumbs, labels, curve, fill and points.
- No new render loop was introduced. Theme changes invalidate visual state only when needed.

Android package version values remain untouched.
