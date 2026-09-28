# Modular Architecture — Stage F

Cumulative checkpoint: A + B + C + D + E + F.

Stage F introduces modular UI factories and declarative navigation contributions.

## Contracts
- `IVisualizerModule`
- `IPageModule`
- `INavigationContribution`

## Built-in visualizer modules
Stable IDs:
- `midirift.visualizer.tracker`
- `midirift.visualizer.spectrum`
- `midirift.visualizer.oscilloscope`

`MainPage` no longer constructs TrackerPanel, SpectrumPanel and OscilloscopePanel
directly. It resolves their `IVisualizerModule` factories through `IModuleRegistry`.

The concrete fields remain temporarily typed because Stage G supplies the specialized
capabilities needed by Tracker, telemetry, WaveType and channel mixer. This keeps F
isolatable instead of inventing one giant visualizer interface.

## Built-in page modules
Stable IDs:
- `midirift.page.nowplaying`
- `midirift.page.library`

`AppShell` registers page modules and builds its primary navigation from
`INavigationContribution` metadata (`Route`, `Title`, `Order`, `IsPrimary`).
Now Playing remains eagerly created and Library remains lazy, preserving the
clean-install cold-start optimization.

## Security / loading boundary
All Stage-F modules are compiled into MIDIRift and explicitly registered.
There is still:
- no external DLL loading,
- no repository/package loading,
- no arbitrary reflection discovery,
- no module permission system.

## Preserved
- BottomNavBar behavior and routes
- MainPage visualizer switching semantics
- Spectrum/Oscilloscope attach/detach behavior
- Tracker rendering and callbacks
- audio/DSP hot paths
- D6.3 EOF guard and Stage-E queue ownership

Stage G can now replace the remaining concrete UI knowledge with focused capabilities
instead of making IVisualizerModule a kitchen-sink interface.
