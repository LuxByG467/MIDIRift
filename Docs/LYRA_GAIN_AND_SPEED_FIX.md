# MIDIRift Android 0.12.3-alpha — Lyra gain + speed fix

## Lyra clipping/headroom

The 0.11.9 gain-parity experiment removed the old `0.18f` attenuation entirely. That restored loudness, but dense/polyphonic MIDIs could exceed 0 dBFS almost continuously before the soft limiter.

Lyra now uses a `LyraMasterVoiceGain` of **0.35** in both VoicePool render paths. This is about +5.8 dB over the original 0.18 Clean Room level, while retaining roughly 9 dB more headroom than the temporary 1.0 gain path. The existing rational soft clip remains as peak protection rather than being forced to act continuously.

## Lyra playback speed

`LyraAudioTrackPlayer.Speed` previously only stored a value. The render scheduler never consumed it, so the UI control had no audible effect.

The Lyra render loop now maintains a fractional musical timeline cursor. MIDI event time advances by `Speed` song samples per real output frame, while oscillators/envelopes still render at 44.1 kHz. This changes tempo without simply pitch-shifting the synthesizer. Event distances are converted from song-sample space to output-frame space.

The PCM ring now records both the musical start and end sample for each generated block. The presentation clock uses these positions and re-anchors when Speed changes, preventing obvious tracker/progress jumps. Seek checkpoints remain expressed in original song-sample coordinates.

### Visual capture clock

FFT/oscilloscope/meter capture remains indexed in real output frames, not musical song samples. This prevents the visual history ring from becoming misaligned when Speed is not 1.0.
