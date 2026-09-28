# MIDIRift Android 0.11.4-alpha — Quick Settings launch latency fix

## Problema

Después del arreglo para limpiadores de tareas, arrancar MIDIRift desde cero mediante el Quick Settings Tile podía sentirse lento y el panel de Quick Settings permanecía visible durante el lanzamiento. El launcher normal no presentaba ese retraso.

## Causa

El Tile consultaba `PackageManager.GetLaunchIntentForPackage()` en cada toque y, en Android anteriores a 14, enviaba manualmente un `PendingIntent` seguido de un broadcast `ACTION_CLOSE_SYSTEM_DIALOGS`. Esa ruta añade trabajo innecesario y no ofrece a SystemUI una transición de lanzamiento integrada.

## Cambio

- El Tile construye directamente un `Intent` explícito hacia `MainActivity`.
- Se conservan `NewTask`, `ClearTop` y `SingleTop` para soportar tanto cold start como una tarea existente.
- Android 14+ usa `StartActivityAndCollapse(PendingIntent)`, como exige la API moderna.
- Android 13 e inferiores usan `StartActivityAndCollapse(Intent)`, que lanza la Activity y solicita a SystemUI cerrar el panel como una única operación.
- Se elimina la consulta a `PackageManager`, el `PendingIntent.Send()` manual y el broadcast cosmético `ACTION_CLOSE_SYSTEM_DIALOGS` en Android anteriores a 14.

## Alcance

No se modifican Lyra, seek, MediaSession, reproducción, servicio en segundo plano ni el ZombieServiceFix.

## Pruebas recomendadas

1. Con MIDIRift completamente cerrado, abrir Quick Settings y tocar el Tile. El panel debe cerrarse inmediatamente y MIDIRift debe iniciar.
2. Repetir después de eliminar MIDIRift mediante el limpiador de recientes de HyperOS/MIUI.
3. Con MIDIRift ya abierto, volver al panel y tocar el Tile; debe regresar a la tarea existente sin crear Activities duplicadas.
4. Confirmar que el Tile continúa cambiando entre `Active` e `Inactive` según la visibilidad de `MainActivity`.
