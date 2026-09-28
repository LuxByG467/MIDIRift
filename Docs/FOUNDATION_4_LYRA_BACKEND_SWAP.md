# Foundation 4 - Lyra backend swap

Lyra now uses the Clean Room audio-backend abstraction.

- Default: AAudio Native RT on Android 8+
- Fallback: AudioTrack if AAudio is unavailable or native initialization fails
- Backend is persistent in EngineSettings (`AudioBackend`)
- Settings UI exposes AAudio Native RT / AudioTrack
- Native AAudio callback and FIFO stay entirely in C++ (`libmidirift_rt_audio.so`)
- Seek/reset uses `ResetForSeek()`; AAudio recreates its stream to avoid INVALID_STATE
- Song changes and pause/resume use the same backend contract

The synth core, EQ, Bass Restoration, tracker, FFT and oscilloscope remain unchanged.
