# DSN0.2.14 — RPN 0,0 Pitch Bend Sensitivity

Adds stateful MIDI RPN decoding:
- CC101 RPN MSB
- CC100 RPN LSB
- CC6 Data Entry MSB
- CC38 Data Entry LSB

RPN 0,0 controls Pitch Bend Sensitivity. Data Entry MSB supplies semitones and
LSB supplies cents. MIDIRift clamps the resulting range to 0..24 semitones.
The default remains +/-2 semitones when no RPN is present.

The RPN selector state is maintained per runtime MIDI channel while compiling
the song. Selecting RPN Null (127,127) stops Data Entry from changing bend
range. Unsupported RPN Data Entry is consumed but does not mutate synthesis.

Both Lyra and DSN-like use the resulting channel bend range. DSN immediately
propagates a range change to active voices using the current bend ratio.

This checkpoint intentionally implements only RPN 0,0. RPN 0,1 Fine Tuning and
0,2 Coarse Tuning are reserved for later checkpoints.
