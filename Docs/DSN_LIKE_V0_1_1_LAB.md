# DSN-like v0.1.1 — DSN Lab

Adds a `DSN` button to the MainPage top bar beside Spectrum/Oscilloscope.
It swaps the central visual area to a development laboratory without replacing
the current playback engine.

The lab can benchmark 16/32/64/96/128 sustained voices at 256/512/1024-frame
blocks and includes several DSP stress patches. Results report Mean/P95/P99/
Worst render time, deadline utilization, realtime factor, aggregate voice
throughput and current-thread allocations.

The benchmark runs on `Task.Run`; it does not execute inside AAudio's callback.
This checkpoint also fixes the Stage-G `engineBuild` out-of-scope regression in
`RebuildChannelMixer` by resolving gain through `_channelMixer` with the legacy
engine as fallback.

This remains a DSP laboratory. DSN-like is not yet the default playback engine
and is not yet connected to MIDI/audio output.
