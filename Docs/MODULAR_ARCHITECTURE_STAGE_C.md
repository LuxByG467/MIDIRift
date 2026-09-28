# Modular Architecture — Stage C

Cumulative checkpoint: A + B + C.

Stage C turns the two built-in MIDI engines into internal modules while preserving
the current Android playback behavior and the ChiptuneEngineKind compatibility API.

## Added
- `IAudioEngineModule : IMidiRiftModule`
- `midirift.engine.lyra`
- `midirift.engine.legacy`

## Migrated
Lyra and Legacy factories now expose ModuleDescriptor and module lifecycle.
ChiptunePlayerFactory registers them in ModuleRegistry and resolves construction
through that registry.

Existing callers using ChiptuneEngineKind continue to work. A new Create overload
accepts a stable module ID.

## Deliberately preserved
- ChiptuneEngineKind / ChiptuneEngineSettings remain compatibility adapters.
- MainPage is unchanged.
- ChiptuneEngineInput, synthesis, TrackerModel construction and backends are unchanged.
- No external engine loading.
- No hot-path DSP changes.

The remaining enum switch now only translates the old persisted selection to a stable
module ID; it is no longer the owner of concrete engine instances.
