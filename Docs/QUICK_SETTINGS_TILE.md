# MIDIRift Android 0.11.1-alpha — Quick Settings Tile

Build: 2026.09.06

## Añadido

- `MidiRiftQuickSettingsTileService`, un TileService nativo de Android disponible desde el editor de Quick Settings.
- El tile funciona como acceso directo permanente a MIDIRift sin requerir una notificación de acceso adicional.
- Al tocarlo abre o reconstruye `MainActivity` con `NewTask | ClearTop | SingleTop`, reutilizando la Activity existente cuando corresponde.
- En Android 14+ se usa `PendingIntent` para cumplir las restricciones modernas de lanzamiento desde TileService.
- En Android 13 e inferiores se conserva la ruta clásica con `Intent`.
- El tile permanece en estado activo porque representa una acción de apertura, no un interruptor de reproducción.

## Icono provisional

Se añadió `Platforms/Android/Resources/drawable/ic_qs_midirift.xml`: un símbolo monocromático que combina una M con una forma de onda/rift. Android aplica automáticamente el color del tema de Quick Settings.

## Versionado

La versión visible pasa de `0.11.0-alpha` a `0.11.1-alpha`. No se modifican `ApplicationDisplayVersion`, `ApplicationVersion`, VersionCode ni otros números internos del paquete Android.

## Prueba recomendada

1. Instalar/actualizar MIDIRift.
2. Abrir el editor de Quick Settings de Android y añadir `MIDIRift`.
3. Con MIDIRift abierto, tocar el tile y confirmar que no crea Activities duplicadas.
4. Reproducir una pista, retirar la app de recientes y confirmar que el servicio continúa según la configuración elegida.
5. Tocar el tile y confirmar que la UI vuelve a abrirse/reconstruirse y se reconecta a la sesión existente.
6. Repetir la prueba sin reproducción activa y con el proceso completamente cerrado.
