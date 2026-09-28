# DSN-like v0.1.2 — DSP Breakdown Profiler

Adds `PROFILE DSP` to DSN Lab.

The profiler avoids placing Stopwatch calls inside the sample loop. Instead it
runs cumulative diagnostic kernels and reports:
- cumulative milliseconds per block
- incremental delta for each added DSP stage
- percentage of the full diagnostic workload

Stages:
VCO1, VCO2/Mix, ADSR/VCA, SVF, LFO/Control, FM, Hard Sync, Drive, Full overhead.

The delta is diagnostic attribution, not a perfect hardware-counter profile:
CPU/JIT/cache interactions are non-additive. It is intended to identify the
large suspect first.

Hot-path correction made while auditing v0.1.1:
- patch scalars and feature flags are compiled/cached in DsnLikeVoice
- disabled FM no longer computes modulation/multiply/clamp every sample
- disabled hard-sync and drive use cached flags
- waveform/filter/levels/output gain no longer repeatedly dereference patch
  properties inside the sample loop

This follows the Android/Oboe real-time principle of keeping bounded work in
the audio path and avoiding avoidable operations. No allocations were added
to RenderSample.
