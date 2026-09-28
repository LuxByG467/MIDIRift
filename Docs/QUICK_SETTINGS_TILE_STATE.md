# MIDIRift Android 0.11.2-alpha — Estado visual del Quick Settings Tile

## Cambio

El Quick Settings Tile ahora conserva siempre su función de abrir MIDIRift, pero refleja visualmente si la interfaz está abierta:

- `TileState.Active` cuando `MainActivity` está visible.
- `TileState.Inactive` cuando la Activity está cerrada o dejó de estar visible, con una apariencia equivalente a un tile como Bluetooth apagado.

## Implementación

`MainActivity.IsVisible` se actualiza en `OnStart()` y `OnStop()`. El `TileService` consulta ese estado en `OnStartListening()`, justo cuando Android muestra/actualiza el panel de Quick Settings.

No se usa la existencia del proceso ni del `MediaPlaybackService` para determinar el estado visual, porque el servicio puede seguir reproduciendo legítimamente después de que la Activity desaparezca.

El estado `Inactive` NO deshabilita el tile: tocarlo sigue lanzando/reconstruyendo `MainActivity`.

## Versión

Versión visible: `0.11.2-alpha`. No se modificó el versionado interno de Android.
