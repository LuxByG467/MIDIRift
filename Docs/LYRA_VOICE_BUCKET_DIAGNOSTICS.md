# Lyra Voice Bucket Diagnostics — 0.12.4D2-alpha

Build diagnóstica basada en 0.12.4D. No cambia DSP, buffers ni comportamiento musical.

`MIDIRift.Perf` añade:
- `stage held/rel/drum`: voces melódicas activas, en Release y percusión.
- `bucketMs`: tiempo acumulado durante la ventana de 2 s para Pulse50, Pulse25, Triangle, Saw, BassHybrid, Noise y Drums.
- `voiceCalls`: número de renderizados de voz/segmento por bucket.
- `Skip`: tiempo y llamadas del fast-forward de voces inaudibles.

La instrumentación usa `Stopwatch.GetTimestamp()` y `stackalloc`; no crea objetos por voz ni por muestra. Su objetivo es identificar qué familia consume el presupuesto del productor durante el coro problemático.
