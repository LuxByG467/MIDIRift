# Modular Architecture — Stage B

Cumulative baseline: Stage A + Stage B, preserving 0.12.4D6.3-alpha runtime behavior.

## Added contracts
- `IAudioAnalysisSource`: canonical engine/format-neutral PCM analysis capability. `IPanelAudioSource` inherits it, so current Lyra/Legacy/MP3 implementations automatically satisfy it.
- `IThemeProvider` / `IThemeService`: future module-facing theme boundary.
- `ThemeDefinition`: immutable token-based theme model.
- `MauiThemeService`: compatibility adapter over current `AppThemeSettings` + `ThemePalette`.
- `IAppLifecycle`: platform-neutral foreground/background capability.
- `AppLifecycleAdapter`: compatibility adapter over current `AppLifecycleState`.

## Deliberately unchanged
No engine, playback, visualizer, palette consumer, Android lifecycle writer, DSP hot path, external loading, permissions, packages, repositories, sandbox or P2P behavior is migrated in Stage B.

Stage B is intentionally compatibility-first. New contracts exist for later stages while the validated D6.3 runtime paths remain authoritative.
