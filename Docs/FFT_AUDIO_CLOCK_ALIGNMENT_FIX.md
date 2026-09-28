# FFT: alineación con el reloj de reproducción

## Causa

Los paneles leían rings alimentados por el productor de audio:

- MIDI publicaba MIX desde `RenderBlock()`, aunque ese hilo puede adelantarse varios bloques respecto a `AudioTrack`.
- MP3 copiaba el PCM al ring antes de `AudioTrack.Write(..., Blocking)`.

El resultado era una secuencia de ráfagas: varios bloques futuros aparecían de golpe y después el FFT repetía la misma ventana mientras el `AudioTrack` los consumía.

## Corrección

- MIDI publica MIX desde `AudioWriterLoop`, después de cada write bloqueante.
- MP3 publica sólo los frames realmente aceptados por `AudioTrack`, después del write.
- `CopyMixSamples()` devuelve ahora las `dest.Length` muestras más recientes. Antes comenzaba en `writePos`, lo que devolvía la mitad más vieja cuando el ring era de 4096 y el FFT pedía 2048.
- Las posiciones de escritura se publican con `Volatile.Write` y se leen con `Volatile.Read`.

## Efecto esperado

El analizador sigue desacoplado del render y conserva smoothing asimétrico, pero sus ventanas avanzan con la cadencia del consumidor de audio en lugar de seguir las ráfagas del sintetizador o decoder.

## Pruebas

- MIDI denso con buffer interno grande.
- MP3 a 1.0x, 1.5x y 2.0x.
- Pausa/reanudación y seek.
- FFT MIX y canal enfocado.
- Comparar ataques de percusión entre audio y visualizador.
