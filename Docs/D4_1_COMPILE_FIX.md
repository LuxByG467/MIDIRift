# MIDIRift Android 0.12.4D4.1-alpha — Compile fix

Fixes CS0133 in `SpectrumPanel.AnalysisIntervalTicks`.

`Stopwatch.Frequency` is a runtime `static readonly` value, not a compile-time constant, so `AnalysisIntervalTicks` must also be `static readonly` rather than `const`.

No DSP, synthesis, FFT cadence, audio backend, buffering, or performance behavior was changed from D4.
