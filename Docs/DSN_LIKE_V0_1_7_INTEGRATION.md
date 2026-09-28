# DSN-like 0.1.7 — playback integration

DSN-like is now registered as a built-in `IAudioEngineModule` (`midirift.engine.dsnlike`) and can be selected from Settings alongside Lyra and Classic/Legacy.

The first integration deliberately reuses Lyra's mature MIDI scheduler, transport, presentation clock, seek/checkpoint infrastructure, AudioTrack/AAudio backend plumbing, EQ, bass restoration, panel capture, and EOF handling. Audible synthesis is produced by one `DsnLikeSynth` per MIDI channel and mixed using the existing channel Volume/Expression/UserGain/Pan state.

Implemented in this checkpoint:
- NoteOn / NoteOff
- per-channel volume, expression and pan through the shared channel state
- sustain-safe release (conservative ReleaseAll on pedal-up for the DSN draft)
- AllNotesOff and EOF release
- existing speed/transport/backend infrastructure
- FFT/oscilloscope capture through the inherited panel source
- selectable engine UI and module registration

Known draft limitations:
- DSN-like patch is currently the v0.1 Default patch for every MIDI channel.
- ProgramChange updates transport/tracker state but does not yet map GM programs to DSN patches.
- Pitch bend/modulation/portamento state is tracked by the transport but is not yet applied to DSN oscillator pitch/modulation.
- Seeking reuses Lyra checkpoint transport state; DSN envelopes are reconstructed conservatively and are not sample-exact yet.
- Percussion currently passes through the same DSN tonal patch rather than a dedicated drum patch.

This checkpoint is intended to make the optimized DSP audible in real MIDI playback before deeper patch mapping and controller fidelity work.
