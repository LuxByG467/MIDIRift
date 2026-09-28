# DSN-like Synth v0.1 — Cheap DSP Draft

This is the first MIDIRift DSN-like synthesis prototype. It is inspired by the
public architecture of dual-VCO subtractive synthesizers such as KORG DSN-12,
but it is not a bit-exact emulation and contains no copied proprietary DSP.

## Goal

Prove the cheapest useful DSP shape before integrating it with MIDI playback,
Lyra/Legacy, UI, presets or Android audio.

## Included

- 2 VCO per voice
- Triangle / Saw / Pulse / Noise
- VCO mix
- VCO2 coarse tuning
- cheap linear FM
- hard sync
- ADSR
- LFO: Triangle / Saw / Square / Sample & Hold
- LFO -> pitch / cutoff / pulse width
- envelope -> cutoff
- LP / BP / HP state-variable filter
- cheap soft drive
- polyphonic voice host
- duplicate-note-safe one-at-a-time NoteOff
- deterministic CPU benchmark harness

## Real-time budget choices

Audio-rate:
- oscillator generation
- phase advance
- optional FM
- hard sync
- filter state
- VCA
- optional drive

Control-rate (32 samples):
- LFO
- cutoff coefficient
- pulse-width modulation
- slow pitch modulation

Expensive operations such as `MathF.Pow` are never used in the ordinary sample
loop. The SVF sine coefficient uses a polynomial approximation at control-rate.
No allocations, locks, LINQ, file I/O, network I/O or UI work occur in
`RenderSample()`.

## Deliberately not integrated yet

The prototype is not registered as an audio engine and cannot be selected in
MIDIRift UI yet. That is intentional. First benchmark the DSP draft on the Poco
F3 / Snapdragon 870 and desktop, then decide what deserves promotion into the
real Lyra-based implementation.

## Benchmark

`DsnLikeBenchmark.Run()` creates sustained polyphony, warms the runtime and
returns mean block render time, audio deadline percentage and aggregate
voice-samples/second.

Suggested first matrix:

- 32 voices / 512 frames
- 64 voices / 512 frames
- 96 voices / 512 frames
- 128 voices / 512 frames

Test at least:
1. default dual-VCO patch
2. FM + sync
3. resonant filter + modulation
4. drive enabled
5. all expensive options together

Target for the future real-time engine: ordinary patches comfortably below
40% of the block deadline, with substantial headroom for Android scheduling,
mixing, effects and visual analysis.
