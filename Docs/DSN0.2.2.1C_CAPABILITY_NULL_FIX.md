# DSN0.2.2.1c Capability null fix

Fixes the Android NullReferenceException after loading a DSN-like MIDI.

`ChiptuneEngineBuild.WaveTypes` is intentionally nullable. Engines only receive
an `IWaveTypeControl` when they advertise `WaveTypeSelector`. DSN-like does not,
because it uses PatchEditor/PatchBank instead.

MainPage incorrectly dereferenced `engineBuild.WaveTypes.WaveTypes` while loading
the Tracker grid. The Tracker already accepts a nullable wave-type list, so the
call now uses `engineBuild.WaveTypes?.WaveTypes.ToList()`.

Lyra/Legacy continue to receive their wave list. DSN-like receives null and keeps
WaveType editing disabled through its capability provider.
