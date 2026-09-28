# DSN0.2.7 — Patch Bank + Tracker header integration

- Tracker `PATCH` headers are now interactive for Patch Architecture engines.
- In DSN-like, tapping a channel header opens the existing DSN Patch Editor on
  that channel's current General MIDI program.
- The editor therefore follows the MIDI's Program Change state rather than a
  hard-coded program or channel number.
- Saving/assigning/restoring a patch refreshes the live channel's patch for
  future notes when that channel is still using the edited GM program.
- Already sounding voices are intentionally not mutated.
- Factory Bank remains immutable; user patches and GM assignments continue to
  live in `dsnlike-bank-v2.json`.
- Lyra/Legacy behavior is unchanged: their headers continue to open the
  WaveType selector.
- No Android package version, DSP kernel, buffer, or gain-staging change.
