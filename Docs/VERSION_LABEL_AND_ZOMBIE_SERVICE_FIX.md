# MIDIRift Android 0.11.0-alpha — Baseline 2026.09.06

## Identificación visible
- Se añadió `MidiRiftVersion.cs` como fuente independiente para la versión visible de producto.
- La barra superior muestra `MIDIRift 0.11.0-alpha` y `Build 2026.09.06`.
- La misma identificación se escribe en Logcat al arrancar.
- NO se modificó `ApplicationVersion`, `VersionCode`, `PackageVersion` ni ningún identificador interno del paquete Android.

## Zombie Service Fix
- `MainActivity.OnCreate` usa `base.OnCreate(null)` para impedir que Android intente restaurar estado/fragments incompatibles cuando el foreground service conserva vivo el proceso y la Activity debe reconstruirse.
- No se modificaron Lyra, seek, MediaSession ni el pipeline de audio.

## Baseline
Esta build parte de `MIDIRift.Android(3)` y se considera la baseline conceptual `0.11.0-alpha` para continuar el desarrollo.
