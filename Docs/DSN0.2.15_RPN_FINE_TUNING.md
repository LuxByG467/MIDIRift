# DSN0.2.15 — RPN 0,1 Channel Fine Tuning

Extends the RPN infrastructure introduced in 0.2.14.

RPN 0,1 now decodes CC6 (Data Entry MSB) and CC38 (Data Entry LSB) as a
14-bit Channel Fine Tuning value:
- 8192 = center / 0 cents
- 0 ~= -100 cents
- 16383 ~= +100 cents

The result is stored as approximately -1..+1 semitone and combined with the
current pitch bend when calculating the channel pitch ratio. Therefore Fine
Tuning and Pitch Bend can coexist rather than overwrite one another.

Both Lyra and DSN-like consume the resulting tuning. DSN propagates the new
combined ratio to already-active melodic voices. Percussion is unaffected.

CC121 Reset All Controllers returns Fine Tuning to center along with the other
transient controller state.

RPN 0,2 Channel Coarse Tuning remains intentionally reserved for the next
checkpoint.
