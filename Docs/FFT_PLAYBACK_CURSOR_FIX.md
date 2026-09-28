# FFT playback-cursor fix

## Problema
El FFT se calculaba por VSync, pero la ventana PCM terminaba en el puntero de escritura. Ese puntero sólo avanzaba cuando AudioTrack.Write aceptaba un bloque, así que muchos frames visuales analizaban exactamente la misma ventana y luego saltaban un bloque completo.

## Solución
- Historial PCM ampliado a 65536 frames.
- Cada muestra se indexa con un frame absoluto de la sesión.
- CopyMixSamples y CopyChannelSamples terminan la ventana en PlaybackHeadPosition, no en writePos.
- El contador de 32 bits de AudioTrack se desenvuelve.
- Entre actualizaciones del playback head se extrapola con Stopwatch y sample rate, limitado por los frames realmente escritos.
- MIDI, MP3, medidor y canales usan el mismo reloj de reproducción.
- Seek/flush reinician reloj e historial.

Así el FFT por VSync recibe ventanas verdaderamente deslizantes aunque AudioTrack se alimente por bloques.
