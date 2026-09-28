# Lyra envelope boundary fidelity — 0.12.4D6.1-alpha

D6 removed the remaining Butterflies starvation but exposed an audible rasp on rapid short Square/Pulse notes.

Root cause fixed here: the D6 block ADSR kernel allowed the final sample of Attack/Decay/Release to use the linearly stepped value before applying the exact clamp semantics of `AdvanceEnvelope()`. That could overshoot 1.0 in Attack, undershoot Sustain in Decay, and most importantly produce one negative-envelope waveform sample at the end of Release. Repeated very short square notes made those boundary errors audible as a rasp/click train.

D6.1 keeps the fast block kernels. The hot portion of each ADSR chunk remains branch-light. Only the single boundary sample is handled separately:

- Attack boundary is clamped to exactly 1.0 before waveform generation.
- Decay boundary is clamped to exactly SustainLevel before waveform generation.
- Release boundary emits zero exactly as the original `AdvanceEnvelope()` + `NextSampleKnownWave()` path does, without advancing phase/LFO/anti-pop for that silent terminal frame.
- The remainder of the block is cleared after a voice becomes inactive.

No sample rate, polyphony, gain, EQ, Bass Restoration, scheduler, AAudio buffering, or Android internal package version changes were made.
