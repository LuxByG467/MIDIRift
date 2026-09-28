# DSN0.2.19 — MIDI State / Seek Parity

This checkpoint hardens the shared Lyra/DSN ChannelState checkpoint model before
engine-specific parity work continues.

## Problem
ChannelState had grown beyond the old ChannelStateSnapshot. A seek checkpoint
stored only the cached PitchBendRatio and omitted newer semantic state. After a
seek, later controller events could therefore continue from the wrong state.

## Snapshot now preserves
- Program and WaveType
- Volume, Expression and Pan
- cached PitchBendRatio (diagnostic/backward context)
- Pitch Bend Range
- normalized Pitch Bend position
- Fine Tuning
- Coarse Tuning
- Sustain
- Sostenuto
- Soft Pedal
- Modulation
- Portamento enabled
- Legato enabled
- Portamento time
- pending CC84 Portamento Source Note

Restore recalculates PitchBendRatio from bend position + bend range + fine/coarse
tuning instead of trusting the cached ratio. This is essential because future
Pitch Bend messages after a seek need the original semantic components.

UserGain / Mute / Solo remain intentionally outside the MIDI snapshot; the
Android player already preserves and reapplies those user mixer choices around
checkpoint restoration.

This checkpoint does not add new synthesis behavior. It establishes a lossless
shared MIDI-state checkpoint before Lyra and Legacy controller parity work.
