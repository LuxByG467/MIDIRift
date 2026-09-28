# DSN0.2.18 — CC84 Portamento Control

Adds MIDI CC84 Portamento Control as a one-shot source-note selector.

Semantics:
- CC84 value 0..127 stores the MIDI note from which the next melodic NoteOn
  should glide.
- The source note is consumed by that next melodic NoteOn.
- DSN-like passes the explicit source note into its existing portamento path.
- If CC65 Portamento is enabled, CC5 still controls glide time.
- If CC65 is off but CC84 is present, the next note still receives a short
  5 ms transition rather than silently discarding the explicit source.
- Without CC84, DSN retains its existing previous-note portamento behavior.

The controller is represented in the shared compiled event model and
ChannelState. Lyra records the one-shot source-note state, but this checkpoint
does not pretend its current VoicePool can honor an explicit source pitch;
that requires a dedicated Lyra kernel/API extension.

CC121 clears a pending CC84 source note through ChannelState.ResetControllers().
Percussion does not consume CC84.
