# DSN-like v0.1.3 — Hot-path pass

Based on the Poco F3 64-voice / 512-frame measurements from v0.1.2.

Changes:
- Added an explicit `Loop baseline` row to DSP Breakdown. The old VCO1 row
  included loop/accumulation cost and therefore overstated oscillator cost.
- Added a dedicated sustain block renderer. Sustained voices no longer execute
  the ADSR state-machine switch for every sample.
- Kept exact sample-by-sample ADSR semantics during Attack/Decay/Release.
- Cached the MIDI-note base phase increment and Osc2 pitch ratio. Pitch-LFO
  control updates no longer recompute note frequency and Osc2 semitone ratio
  with extra `MathF.Pow` calls.
- Retains the v0.1.2 compiled patch flags for FM, Sync and Drive.

No intentional synthesis-feature removal. DSN-like remains a benchmark lab,
not the default MIDI playback engine.
