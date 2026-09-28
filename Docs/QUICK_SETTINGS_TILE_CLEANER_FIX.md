# MIDIRift Android 0.11.3-alpha — Quick Settings Tile / cleaner fix

## Problema
En algunos dispositivos Xiaomi/POCO, limpiar el historial de aplicaciones puede eliminar la tarea/proceso de MIDIRift mientras SystemUI conserva el Quick Settings Tile. El tile podía quedar visualmente desactualizado y su ruta de apertura dependía demasiado del estado anterior de la Activity.

## Cambio
- El tile obtiene el Intent de lanzamiento desde `PackageManager.GetLaunchIntentForPackage`, equivalente a abrir MIDIRift desde su icono.
- El lanzamiento usa un `PendingIntent` independiente de la instancia anterior de `MainActivity`.
- En Android anteriores a 14 se envía directamente el `PendingIntent`; en Android 14+ se usa `StartActivityAndCollapse(PendingIntent)`.
- `MainActivity.OnStart`, `MainActivity.OnStop` y `MediaPlaybackService.OnTaskRemoved` solicitan a SystemUI refrescar el estado del tile.
- El tile sigue siendo `Active` sólo mientras la Activity está visible e `Inactive` cuando no lo está.

## Prueba objetivo
1. Abrir MIDIRift: tile activo.
2. Usar el limpiador/historial de apps: tile inactivo.
3. Pulsar el tile: MIDIRift debe arrancar desde cero igual que al pulsar su icono.
4. Repetir con reproducción/servicio en segundo plano.

No se modificaron Lyra, seek, DSP ni el versionado interno de Android.
