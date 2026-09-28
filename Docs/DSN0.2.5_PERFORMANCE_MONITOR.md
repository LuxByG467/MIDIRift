# DSN0.2.5 Performance Monitor

The former DSN benchmark panel now doubles as a live playback resource monitor.

It samples the audio engine at 4 Hz and shows render budget, late blocks, active/peak voices, DSP feature occupancy, managed PCM ring fill, AAudio buffered/adaptive frames, underruns, allocation delta and GC collection deltas. A 30-second render-budget sparkline is retained in a pre-bounded queue.

The audio/render path only updates the existing lock-free telemetry counters. UI strings/history are created on the UI timer, never on the audio callback/producer hot path. The synthetic benchmark and DSP profiler remain available below the live monitor.
