# MIDIRift documentation

This directory contains MIDIRift's development notes, architecture documents,
engine change logs, performance investigations, and compatibility fixes. Many
files are historical snapshots from individual alpha milestones; they are kept
because they explain why parts of the current implementation exist.

## Start here

- `ANDROID_NDK_BUILD.md` — native AAudio/NDK build setup and the historical
  `C:\AndroidNDK` workaround.
- `FOUNDATION_3_COMMON_PLAYBACK.md` — shared playback foundation.
- `FOUNDATION_4_LYRA_BACKEND_SWAP.md` — Lyra Android backend selection and
  AAudio/AudioTrack fallback.
- `FOUNDATION_4_AUDIO_PRESENTATION_CLOCK.md` — presentation timing model.
- `DSN0.2.23.1_FIRST_RUN_UI_STATE_HOTFIX.md` — notes for the current DSN
  snapshot included with MIDIRift Android 0.14.0.
- `MODULE_PACKAGE_MANIFEST.example.json` — example manifest for the module
  system. The `MIT` value inside this example is illustrative metadata for an
  example module; it is **not** the license of MIDIRift itself.

## Project license

MIDIRift itself is licensed under the GNU General Public License v3.0. See the
root `LICENSE` file. Third-party dependencies and independently distributed
modules retain their own licenses.
