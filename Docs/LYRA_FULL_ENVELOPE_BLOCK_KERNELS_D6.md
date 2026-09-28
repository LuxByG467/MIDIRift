# MIDIRift Android 0.12.4D6-alpha — Full melodic envelope block kernels

Base: 0.12.4D5-alpha.

## Cambios

- Extiende el fast path melódico de D5 más allá de Sustain a Attack, Decay y Release.
- Mantiene la progresión lineal del ADSR y el anti-pop con el mismo orden por muestra, pero resuelve las fronteras de etapa por chunks en vez de ejecutar el switch del envelope en cada sample.
- Mantiene el control-rate del LFO a 64 frames.
- Cuando Release termina dentro de un bloque, el resto del scratch se limpia a cero y la voz queda inactiva para el bloque siguiente.
- Portamento y percusión conservan el renderer general para evitar cambios de semántica.
- Los kernels siguen especializados por WaveType: Pulse50, Pulse25, Triangle, Saw, BassHybrid y Noise.

## Objetivo

Reducir el costo de secciones con retriggering/polifonía intensa, donde muchas voces todavía están en Attack/Decay/Release y por ello D5 no podía usar su kernel de Sustain.

## Sin cambios

No cambia sample rate, polifonía, ganancia, EQ, Bass Restoration, scheduler, buffering adaptativo ni el versionado interno de Android.
