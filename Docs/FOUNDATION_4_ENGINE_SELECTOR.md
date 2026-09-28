# Foundation-4 — Persistent engine selector

Foundation-4 promotes Lyra and Classic to product-level selectable engines.

## Behaviour

- New installs default to **Lyra**.
- Existing installs without an engine preference also resolve to **Lyra**.
- The setting is stored in MAUI `Preferences` as `chiptune_engine_kind`.
- The settings screen exposes two explicit buttons: **Lyra** and **Classic / Legacy**.
- Saving the setting affects the next MIDI load. The currently playing engine is not destroyed mid-song.
- Buffer/block settings still require an app restart.
- The playback status line displays the engine used for the current MIDI during this integration phase.

## Architecture

The setting only changes `ChiptuneEngineSelection.DefaultEngine`. `MainPage` still creates playback through `ChiptunePlayerFactory`, so it remains unaware of concrete engine classes.
