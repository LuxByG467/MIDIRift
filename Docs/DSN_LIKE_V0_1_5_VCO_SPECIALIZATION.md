# DSN-like 0.1.5 - VCO specialization and overhead split

- Adds block-level waveform dispatch for the common clean Saw+Pulse+LowPass path.
- Adds a Pulse+Saw+LowPass specialization for another common dual-VCO topology.
- Keeps the generic clean renderer as fallback for all other waveform/filter combinations.
- Removes waveform and filter-mode switches from the specialized per-sample loops.
- Profiler now splits the old Full overhead tail into Output gain, Mix accumulation, and Bookkeeping/residual.
- Diagnostic Drive now uses the same FastDrive primitive as the optimized real sustain path.
- Android ApplicationDisplayVersion/ApplicationVersion are intentionally unchanged.

Benchmark target: 64 voices, 512 frames, Default, directly comparable with 0.1.4.
