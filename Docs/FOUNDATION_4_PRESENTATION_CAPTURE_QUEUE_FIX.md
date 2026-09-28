# Foundation 4 — Presentation capture / queue follow-up

Fixes regressions introduced by the presentation-clock migration.

## AAudio pacing
The managed writer no longer fills the entire native FIFO. It keeps only a few
blocks/bursts ahead of the callback, while the managed PCM ring remains the main
anti-underrun cushion. This keeps the presentation cursor within the history
retained by FFT/oscilloscope capture rings.

## Capture history
CaptureLength increased from 65536 to 131072 frames and channel capture keepalive
from 250 ms to 1000 ms.

## EOF / automatic queue
The pending end sample is clamped to TotalSamples. Previously the final 1024-frame
block could extend beyond TotalSamples while GetPresentationSample() was clamped
to TotalSamples, making the completion condition impossible.

TrackerPlayer now also polls IChiptunePlayer.IsFinished on its visual loop as a
defensive fallback, so automatic Next does not depend exclusively on an
OnStepAdvanced notification arriving at exactly the right moment.
