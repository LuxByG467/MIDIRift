# DSN-like 0.1.6 — block hot path

- Default Saw+Pulse+LP and Pulse+Saw+LP sustain routes now cache oscillator phases in locals.
- Control-rate work is chunked at 32-sample boundaries instead of branching every sample.
- Oscillator render/advance calls are removed from these hottest loops.
- Filter semantics and control-rate timing are preserved.
- Generic/FM/Sync/Drive routes are unchanged to keep this iteration low-risk.
- Android package version remains unchanged.

This is intentionally a scalar block-kernel pass before SIMD. The SVF is state-recursive, so measuring the cheaper scalar kernel first avoids vectorizing around the wrong bottleneck.
