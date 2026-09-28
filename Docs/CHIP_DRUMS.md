# ChipDrums

`WaveType.ChipDrums` sintetiza percusión sin samples mediante un pool one-shot separado:

- Kick: square con pitch envelope descendente y click de ruido.
- Snare/clap: square corta + white noise.
- Hi-hat: white noise con decay corto o largo.
- Toms: pulse con barrido descendente.
- Cymbals: white noise largo con capa pulse tenue.

El canal MIDI de percusión se asigna a ChipDrums por defecto. El tipo también puede seleccionarse manualmente desde el TrackerPanel y se persiste mediante WaveTypeConfig.
