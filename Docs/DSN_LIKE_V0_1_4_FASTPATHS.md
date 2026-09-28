# DSN-like 0.1.4 — Fast paths + macro profiler

- Compiles a broad voice route in `ApplyPatch` so clean sustained patches bypass FM/sync/drive branches.
- Adds specialized SVF LP/BP/HP entry points.
- Adds a division-free cubic `FastDrive` saturator for the drive hot path. This intentionally changes the exact saturation curve; validate by ear.
- Adds a macro profiler comparing the real `DsnLikeSynth.Render` mean against the synthetic Full diagnostic kernel and reports the unexplained architecture gap.
- Keeps the 0.1.3 sustain kernel and control-rate pitch caching.
- Android package/versionCode values are unchanged.

Primary validation: 64 voices / 512 frames, same preset as 0.1.3. Run BENCHMARK and PROFILE DSP.
