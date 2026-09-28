# DSN-like 0.2 — Patch Architecture

This checkpoint separates the classic WaveType workflow from DSN-like synthesis configuration.

## Changes
- DSN-like melodic VCOs now expose Sine, Triangle, Saw and Pulse. Noise remains available internally but is not used by the factory melodic GM bank.
- Added a dedicated 128-program DSN factory bank. General MIDI Program Change selects a complete DSN patch instead of pretending to select one WaveType.
- Added a versioned DSN-only user bank (`midirift.dsn.bank`, schema v1) stored independently from Lyra/Legacy WaveTypeConfig.
- Added a separate `DsnPatchEditorPage` with VCO1/VCO2, levels, pulse widths, tuning, Sync, FM, VCF, ADSR, LFO, Drive and Output controls.
- Factory patches are recoverable. Saving creates a user override; Reset removes only the override.
- The Tracker WaveType dropdown is disabled for DSN-like and its header identifies the synthesis configuration as PATCH. Lyra/Legacy retain the old WaveTypeSelector behavior.
- DSN Program Change now applies the resolved factory/user patch to the channel synth.

## Intentional boundary
The MIDI program number is common semantic input. Lyra/Legacy may interpret it as WaveType-oriented instrumentation; DSN-like interprets it through its own patch bank. This prevents a universal settings object from coupling unrelated synthesis architectures.

## Validation after build
1. Compile Android and open Settings > DSN-like Patch Bank.
2. Verify all 128 GM entries can be selected, edited, saved, and reset.
3. Load a MIDI with DSN-like and confirm Tracker headers say PATCH and do not open the classic WaveType dropdown.
4. Test a MIDI with Program Change events and verify timbre changes without stopping playback.
5. Compare Sine/Triangle/Saw/Pulse-heavy programs in the oscilloscope.
6. Stress-test dense MIDIs for regressions versus DSN 0.1.8.1.

Android package version fields were intentionally left unchanged.
