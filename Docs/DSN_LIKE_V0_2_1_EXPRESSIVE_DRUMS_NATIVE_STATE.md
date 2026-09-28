# DSN-like 0.2.1 — Expressive MIDI, GM Drumkit, native DSN state

This source checkpoint advances the code-only items that do not require device validation yet.

## 1. Expressive MIDI
- Pitch Bend now reaches active DSN melodic voices.
- CC1 Mod Wheel adds control-rate vibrato on top of patch LFO routing.
- CC5 + CC65 Portamento now glide DSN note starts instead of only updating Lyra channel state.
- Legato uses a very short DSN glide when enabled without portamento.
- Sustain keeps D6.2-style FIFO NoteOff semantics: each NoteOff consumes one KeyHeld duplicate; pedal-up releases only SustainHeld voices.
- Program Change updates the patch used by future voices and no longer mutates notes already sounding.

## 2. Dedicated DSN GM drumkit
Channel 10 remains outside melodic VCO patches. DsnDrumKit now explicitly maps the standard GM percussion range 35..81 into synthetic families: kick, snare, clap, rim, toms, closed/open hats, crash/ride, bells, cowbell, shakers, wood blocks, guiro, and triangles. It remains sample-free and allocation-free in the render path.

## 3. Remove shadow Lyra VoicePool from DSN playback
DsnLikeAudioTrackPlayer no longer mirrors NoteOn/NoteOff/release state into `_core.Voices` during normal DSN playback. EOF uses DSN voices/drums only.

Seek reconstruction is now DSN-native: reset scheduler + DSN state, replay MIDI events event-to-event, and advance DSN envelopes/portamento without generating PCM. This removes the old dependency on Lyra voice checkpoints and also avoids restoring a Lyra voice state that the audible DSN synth did not actually share.

The compiled MIDI scheduler and ChannelState remain shared Core infrastructure. That is intentional; only the redundant Lyra synthesis VoicePool dependency was removed.

## Hardware acceptance tests
1. Pitch-bend MIDI audibly bends DSN notes and returns to center.
2. CC1 produces vibrato without changing the selected patch.
3. Portamento MIDI glides between notes and does not strand voices.
4. Sustain with duplicate NoteOns releases one voice per NoteOff and clears pending notes on pedal-up.
5. GM channel 10 exercises notes 35..81 without melodic VCO tones or crashes.
6. Seek repeatedly through long songs; verify correct Program/CC state and no stuck notes.
7. Program Change during a held note changes subsequent notes, not the already sounding note.
8. Compare DSN render telemetry with 0.1.8.1 / 0.2 for regressions.
