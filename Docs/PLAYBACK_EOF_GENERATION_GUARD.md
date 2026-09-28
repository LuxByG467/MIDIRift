# Playback EOF generation guard — 0.11.6-alpha

## Síntoma
Algunas veces, después de elegir manualmente una canción desde Biblioteca, al terminar la reproducción la cola avanzaba dos canciones en vez de una.

## Auditoría de rutas
- Biblioteca selecciona playlist + entrada y envía `PlaybackBridge.RequestPlay(path)`.
- MainPage recibe la petición y entra por `LoadByExtension`.
- Next/Prev también entran por `LoadByExtension`.
- MIDI detecta EOF mediante `TrackerPlayer.OnFinished`.
- MP3 tenía DOS detectores del mismo EOF: `Mp3AudioPlayer.OnCompleted` y el timer de 250 ms comprobando `IsFinished`. Ambos llamaban independientemente a `OnTrackFinished()`.
- `OnTrackFinished()` ejecuta `PlaylistController.NextAuto()`, por lo que dos llamadas para el mismo EOF avanzaban dos posiciones.

## Corrección
Cada carga ya tenía `_loadGeneration`; ahora el EOF captura esa generación y `OnTrackFinished(int generation)` sólo acepta:
1. la generación que sigue siendo actual; y
2. el primer EOF de esa generación.

`_finishConsumedGeneration` convierte el final de pista en una operación idempotente. Un callback tardío de una pista reemplazada o un segundo detector del mismo EOF se descartan sin modificar la playlist.

Esto cubre selección manual desde Biblioteca, Next, Prev, auto-next, MIDI, MP3 y repeat-track sin eliminar la redundancia útil del polling MP3.
