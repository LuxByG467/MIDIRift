# DSN-like 0.1.8.1 - Voice lifecycle / duplicate NoteOff fix

- Ports the D6.2 FIFO duplicate-note pairing semantics from Lyra to DSN-like.
- Each voice now tracks `KeyHeld`, `SustainHeld`, and `StartSequence`.
- A NoteOff consumes exactly one oldest still-key-held duplicate voice.
- A voice already in Release can no longer consume later duplicate NoteOffs.
- Sustain pedal-up releases only sustain-pending voices, not notes still physically held.
- Voice allocation remains a fixed pool: free -> oldest Release -> oldest active.
- Reset clears lifecycle metadata and sequence state.

This fixes the accumulation mode where repeated same-note passages could strand newer duplicates in Sustain indefinitely.
