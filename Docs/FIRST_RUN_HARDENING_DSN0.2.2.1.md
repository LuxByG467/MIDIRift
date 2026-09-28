# First Run / Cold Start Hardening — DSN0.2.2.1

## Goal
No mutable user file is a prerequisite for MIDIRift startup.

## Unified policy
Persistent JSON follows: missing -> safe default; empty/corrupt/incompatible -> quarantine -> safe default.
Writes use `FirstRunInitializer.AtomicWriteAllText`.

## Hardened stores
- EQ presets/current EQ
- WaveType per-track configuration
- DSN user patch/program bank

The DSN Factory Bank remains code-generated. `dsnlike-bank-v2.json` is intentionally optional and is not created on first run unless user state must be saved.

## Recovery
Invalid files are renamed with `.invalid-<UTC timestamp>` where possible. Failure to quarantine falls back to deletion. Store recovery must not prevent the UI from starting.

## Hardware acceptance later
1. Clear app data and launch.
2. Confirm Library/Settings/Playback UI opens.
3. Select Lyra/Legacy/DSN without pre-existing files.
4. Create EQ, WaveType and DSN user state; restart.
5. Corrupt each JSON independently and relaunch.
6. Confirm recovery and `.invalid-*` backup.
