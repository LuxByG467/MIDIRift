# 0.2.23 — Legacy RPN / Tuning Parity

Legacy now decodes the same three Registered Parameter Numbers used by Lyra
and DSN-like:

- RPN 0,0 — Pitch Bend Sensitivity.
  - Data Entry MSB = semitones.
  - Data Entry LSB = cents.
  - Clamped to the same 0..24 semitone range used by the modern engines.
- RPN 0,1 — Channel Fine Tuning.
  - 14-bit Data Entry, center 8192.
  - Approximately -100..+100 cents.
- RPN 0,2 — Channel Coarse Tuning.
  - Data Entry MSB 64 = center.
  - Range -64..+63 semitones.

CC101/CC100 select the RPN and CC6/CC38 update Data Entry. RPN Null 127/127
disables subsequent Data Entry targeting.

Legacy no longer bakes Pitch Wheel directly as a fixed +/-2-semitone ratio.
The adapter stores normalized bend state and computes each MidiStep pitch from:

    bend * bendRange + fineTuning + coarseTuning

The default bend range remains +/-2 semitones, preserving old behaviour when a
file contains no RPN messages.

Legacy seek needs no new snapshot structure: its silent timeline reconstruction
replays the generated MidiSteps from the beginning and therefore reconstructs
the effective pitch state deterministically.

Also fixes the 0.2.22 final-tail CreateStep call so the newly added Decay,
VibratoDepth, VibratoDelay, SoftPedal and PortamentoSource arguments are carried
into a final generated step instead of using the older call signature.
