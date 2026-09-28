# DSN0.2.16 — RPN 0,2 Channel Coarse Tuning

Extends the stateful RPN decoder with Channel Coarse Tuning.

RPN 0,2:
- CC6 / Data Entry MSB = coarse tuning.
- 64 = center / 0 semitones.
- 0 = -64 semitones.
- 127 = +63 semitones.
- CC38 / Data Entry LSB is ignored for the coarse amount.

Coarse Tuning is combined with Fine Tuning and Pitch Bend in the channel's
effective pitch ratio rather than overwriting either one:

effective semitones =
    coarse tuning
  + fine tuning
  + normalized pitch bend * bend range

Both Lyra and DSN-like consume the resulting ratio. DSN updates active melodic
voices immediately. Percussion remains unaffected.

CC121 Reset All Controllers returns Coarse Tuning and Fine Tuning to center,
Pitch Bend to center, and Pitch Bend Sensitivity to the default +/-2 semitones.

The three core tuning RPNs are now present:
- RPN 0,0 Pitch Bend Sensitivity
- RPN 0,1 Channel Fine Tuning
- RPN 0,2 Channel Coarse Tuning
