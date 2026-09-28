# DSN0.2.8 — Contextual Drum Kit Editor

Tracker Patch Architecture now distinguishes melodic and percussion channels.

- Melodic DSN headers show `PATCH` and open the existing Patch Editor on the
  channel's current GM program.
- Percussion headers show `DRM` and open the new DSN Drum Kit Editor.
- The editor covers GM percussion notes 35–81.
- Per-piece user controls: tuning, decay scale, gain scale and tone/metallicity.
- Factory drum recipes remain in code and unchanged; user adjustments are
  overlays stored in versioned `dsnlike-drumkit-v1.json`.
- Restore Factory removes only the selected piece's override.
- Saved settings are pushed into the currently running DSN drum synths and
  affect future drum hits without rebuilding the audio engine.
- Existing sounding drum voices keep their current parameters.
- No melodic Patch Bank, specialized kernel, buffer, gain staging, or Android
  package version changed.
