# MIDIRift 0.12.4D-alpha — Lyra performance diagnostic

Esta compilación parte exactamente de 0.12.4 y añade instrumentación de bajo coste. No intenta ocultar starvation aumentando buffers ni cambia el sintetizador.

Cada ~2 s Logcat emite `MIDIRift.Perf` con:
- tiempo medio/máximo de RenderBlock;
- bloques y segmentos por bloque;
- voces activas;
- bytes administrados asignados por segundo en el hilo productor;
- contadores GC Gen0/1/2;
- ocupación del ring administrado;
- frames del backend y buffer adaptativo;
- underruns/starvation reportados por el backend;
- Speed actual.

El objetivo es distinguir CPU sostenida del sintetizador, starvation del productor, GC administrado y falta de colchón del backend.
