# DSN0.2.13 — Correct CC120 / CC123 panic semantics

The common MIDI pipeline previously collapsed CC120 and CC123 into the same
AllNotesOff event. They are now distinct.

- CC120 All Sound Off: immediate hard silence on the addressed channel. DSN
  resets active melodic voices and its drum voices without resetting controller
  state. Lyra now has a channel-specific hard-kill path as well.
- CC123 All Notes Off: graceful note release through the normal release
  envelope, preserving the musical tail behavior already used by the engines.
- CC121 remains Reset All Controllers from 0.2.12.

This is deliberately a small checkpoint before adding more expressive CC/RPN
state, so compile/runtime regressions can be bisected cleanly.
