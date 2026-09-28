# Oscilloscope trigger: focused hit-test + hysteresis

## 0.12.2-alpha

- Trigger button hit-test now runs before the focused-channel "return to MIX" gesture.
- Releasing a consumed trigger touch cannot propagate as `VisualTapped`.
- Replaced permissive sign-only zero crossing with an adaptive Schmitt-style rising trigger.
- Trigger requires the waveform to pass below a negative threshold and then above a positive threshold.
- Threshold is 8% of the local peak, clamped to 0.006..0.08.
- Once a valid transition is confirmed, rendering starts at the actual rising zero crossing.
- Near-silence (<0.006 peak) does not chase noise.
- Audio/DSP paths are unchanged; this only affects oscilloscope capture alignment and touch handling.
