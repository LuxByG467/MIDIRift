# Corrección de arranque con datos recién borrados

## Problema observado

MIDIRift podía cerrarse o quedar sin interfaz después de borrar los datos de la app. El motor de audio y el render segmentado todavía no participan en ese momento, por lo que el fallo pertenece al flujo de inicialización/persistencia.

## Cambios

- Los archivos JSON iniciales se validan con `JsonDocument` antes de construir `AppShell`.
- Archivos vacíos o JSON inválidos se ponen en cuarentena y se regeneran.
- La escritura atómica incluye fallback para sistemas Android donde `File.Move(..., overwrite: true)` falle.
- La restauración del tema ya no puede tumbar el constructor de `App`.
- En un primer arranque fallido se reconstruyen sólo los archivos generados y se reintenta una vez.
- Con datos existentes no se reconstruye automáticamente, para no ocultar o destruir configuración real.
- Si ambos intentos fallan, se muestra el stack trace en pantalla y se registra en Logcat con la etiqueta `MIDIRift.Startup`.

## Prueba recomendada

1. Compilar e instalar la app.
2. Abrir Ajustes de Android > Aplicaciones > MIDIRift > Almacenamiento.
3. Pulsar `Borrar datos`.
4. Abrir MIDIRift.
5. Repetir el ciclo dos o tres veces.
6. Si aparece la pantalla de error, ejecutar:

   `adb logcat -s MIDIRift.Startup:E`

El texto mostrado o Logcat permitirá localizar cualquier fallo restante sin depender de acceder al almacenamiento privado de la app.
