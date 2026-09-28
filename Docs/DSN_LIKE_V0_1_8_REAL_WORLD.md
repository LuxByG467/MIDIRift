# DSN-like 0.1.8 - Real-world pass

- Channel 10/percussion channels now use a dedicated allocation-free synthetic GM drum kit instead of full dual-VCO DSN voices.
- Covers kick, snare, clap, closed/open hats, toms, cymbals and a generic fallback.
- Adds real playback render telemetry: mean/max block render time, late blocks, active/peak voices and backend underruns (Debug output, throttled to ~1 Hz).
- Percussion NoteOff is intentionally ignored; drum voices are one-shots.
- Existing DSN melodic engine, transport, seek, capture, EQ and backend paths remain intact.
