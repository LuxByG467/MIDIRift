# MIDIRift Android 0.12.4D4-alpha — Full-app performance audit

Production-oriented performance build based on D3.1.

## Main findings
- D3.1 still contained diagnostic timing in the real-time path: Stopwatch.GetTimestamp per rendered voice plus per-block allocation/timing probes and a large log every 2 seconds. D4 removes all of it.
- Spectrum performed an 8192-point FFT and thousands of logarithms on every VSync, plus channel FFT work. D4 decouples visual DSP from paint: analysis is capped at 20 Hz while drawing can remain at VSync.
- Spectrum power-to-dB no longer calls MathF.Log10 for every FFT bin; it uses a deterministic bit/polynomial log approximation.
- Multichannel Spectrum analysis budget is 1 channel per analysis tick instead of 2.
- Oscilloscope capture/trigger cadence is 25 Hz instead of ~30 Hz. Rendering remains independent.
- Audio producer/writer remain Highest/UrgentAudio and the native AAudio callback remains entirely native.

## Deliberately unchanged
- sample rate 44.1 kHz
- voice capacity/polyphony
- EQ/Bass Restoration behavior
- AAudio adaptive buffering
- scheduler semantics
- Android package version

## Architectural recommendation
The next large step, if managed synthesis still misses deadlines, is moving VoicePool block synthesis into the existing native ARM64 audio library and rendering voices in blocks there. This avoids managed per-sample call/state overhead while keeping MAUI/UI and song scheduling managed. Do not move the whole app native.
