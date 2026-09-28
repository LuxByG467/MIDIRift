# MIDIRift Android 0.12.4D5-alpha — Lyra block voice kernels

## Objetivo
Reducir el costo sostenido de VoicePool en MIDIs de polifonía alta sin reducir sample rate, polifonía, calidad de onda ni semántica de mezcla.

## Cambio principal
Las voces melódicas que ya están en Sustain, con anti-pop completado y sin portamento activo, usan un kernel por bloques especializado por WaveType.

El kernel:
- saca ADSR/anti-pop y comprobaciones invariantes fuera del loop por muestra;
- mantiene el LFO a su control-rate existente de 64 frames;
- resuelve WaveType una vez por chunk, no por sample;
- usa caminos dedicados Pulse50, Pulse25, Triangle, Saw, BassHybrid y Noise;
- evita MathF.Floor por sample para el phase wrap normal (el incremento MIDI válido es < 1 ciclo/sample);
- conserva el mezclado ARM64 NEON ya existente después de generar el bloque.

Attack, Decay, Release, portamento y percusión siguen usando el camino general para evitar alterar transitorios o comportamiento musical.

## Seguridad de regresión
No cambia:
- 44.1 kHz;
- límite de polifonía;
- AAudio/adaptive buffer;
- EQ/Bass Restoration;
- ganancia Lyra;
- scheduler MIDI;
- versión interna Android.

Butterflies and Hurricanes debe conservarse como prueba de regresión y An Enigmatic Encounter como prueba de estrés.
