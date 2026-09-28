# DSN-like 0.1.7.1 audibility hotfix

The first real playback integration could produce silence. This hotfix hardens the cheap Chamberlin SVF for real MIDI note ranges, uses a conservative 6 kHz integration patch cutoff, and prevents non-finite DSP samples from poisoning the stereo mix. Benchmark/lab architecture remains available.
