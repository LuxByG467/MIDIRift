# Oscilloscope Triggered mode — 0.12.1-alpha

Restores the user-selectable Triggered mode lost during the Clean Room regression.

## Behavior
- `TRIG ON`: each captured MIX/channel frame is aligned to the first rising zero crossing in the first half of the capture window.
- `TRIG OFF`: the oscilloscope draws from sample 0, free-running.
- The mode can be toggled from the `TRIG ON/OFF` pill in the upper-right corner of the oscilloscope.
- The preference persists through `Microsoft.Maui.Storage.Preferences`.
- Trigger processing stays outside the audio hot path; it operates only on the visualization capture buffers in `OscilloscopePanel.Tick()`.
- No changes to Lyra, Legacy, AAudio/AudioTrack, FFT, EQ or playback.

The current Clean Room lineage already retained the simple rising-zero-crossing detector internally, but it was always enabled and had no selectable Triggered/free-running mode. This patch restores that missing mode around the existing detector rather than modifying the audio engine.


## 0.12.1-alpha — Trigger control usability

- Botón TRIGGER ampliado de 82x28 a 132x46 px de canvas.
- Movido desde la esquina superior derecha a la esquina inferior derecha de la zona MIX.
- En vista de canal enfocado se coloca abajo a la derecha del panel completo.
- El hitbox se calcula desde el mismo rectángulo usado para dibujarlo, evitando discrepancias entre dibujo y toque.
- Ya no compite con las etiquetas PEAK/CLIP de la esquina superior.
- Texto cambiado a TRIGGER ON/OFF y aumentado para mejorar legibilidad.
