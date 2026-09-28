# Corrección de semántica NoteOn/NoteOff

Esta versión separa la fotografía de notas usada por TrackerPanel de los eventos MIDI reales usados por el sintetizador.

## Cambios

- `MidiStep.NoteCount` y `Freq0..Freq5` siguen representando las notas activas durante el intervalo para la UI.
- `MidiStep.NoteOnCount`/`NoteOffCount` contienen únicamente los eventos ocurridos al inicio del intervalo.
- `Voice` conserva `MidiNote` y `KeyDown` para asociar correctamente NoteOff, sustain y sostenuto.
- Los cambios de CC, pitch bend, paneo, modulación o aftertouch ya no recrean voces ni reinician ADSR.
- Sustain solo libera voces cuya tecla ya fue soltada.
- Sostenuto captura las teclas activas al pisar el pedal y las libera correctamente al soltarlo.
- Las notas repetidas del mismo tono se emparejan una a una, no mediante eliminación global.

## Prueba recomendada

Comparar Spider Dance y otros MIDI densos con EQ apagado/encendido. La velocidad debe permanecer estable y los ataques no deben repetirse con cada controlador.
