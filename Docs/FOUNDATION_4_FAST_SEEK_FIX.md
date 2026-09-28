# Foundation 4 - Fast Seek Fix

El seek de Lyra ya no reconstruye la canción generando PCM desde sample 0.

Cambios:
- Activa `SeekCheckpointCache` de Lyra (checkpoint cada 5 s).
- Restaura el checkpoint más cercano mediante búsqueda binaria.
- Avanza de frontera MIDI a frontera MIDI sin mezcla, EQ, Bass Restoration ni capturas.
- Las voces melódicas avanzan envelope/LFO/portamento/fase por bloques.
- Sólo las voces de percusión conservan un avance por muestra para mantener su estado exacto.
- Los checkpoints también se generan durante reproducción normal y durante el propio seek.

La complejidad del seek deja de estar dominada por los 44,100 samples por segundo de audio y pasa a depender principalmente de eventos MIDI, voces activas y un tramo máximo de 5 s desde el checkpoint más cercano.
