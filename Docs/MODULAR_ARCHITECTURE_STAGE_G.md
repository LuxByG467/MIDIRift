# Modular Architecture — Stage G

Cumulative checkpoint: A + B + C + D + E + F + G.

Stage G adds focused feature capabilities so consumers do not need to know Lyra,
Legacy or TrackerPlayer concrete ownership.

## Contracts
- `IEngineTelemetry`
- `ITrackerTimelineSource`
- `IWaveTypeControl`
- `IChannelMixer`

## Engine capabilities
`ChiptuneEngineBuild` now publishes WaveTypes, Mixer and Telemetry capabilities beside
the compatibility `IChiptunePlayer`. Both Lyra and Legacy therefore expose the same
module-facing controls without adding engine-specific branches.

`ChiptuneCapabilityAdapter` is intentionally outside the audio hot path. It delegates
existing wave/mixer operations and produces a small engine-neutral telemetry snapshot.

## Tracker capability
`TrackerTimelineAdapter` exposes Grid, CurrentRow, IsFinished and FrameAdvanced as a
read-only timeline capability. It does not own EOF or queue advancement.

## MainPage migration
The active MIDI session now retains capability references for WaveType, channel mixer,
telemetry and tracker timeline. WaveType hot-swap and channel gain UI use those focused
capabilities rather than reaching through the concrete player contract.

## Deliberately deferred
Lyra's richer internal EngineSnapshot remains an engine-private implementation detail.
A later telemetry refinement can project active voices/channel details without making
the public capability depend on Lyra types.

No synthesis, DSP, backend, FFT, oscilloscope or D6.3 EOF hot path is changed.
