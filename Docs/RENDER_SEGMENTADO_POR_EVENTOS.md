# Render segmentado por eventos

## Implementación

Se modificó `Platforms/Android/ChiptuneAudioTrack.cs` para dividir cada bloque PCM en segmentos que terminan en la siguiente frontera MIDI.

Antes, cada muestra y cada canal comprobaban:

- si `TimeLeft` había vencido;
- si quedaban `MidiStep`;
- si debía liberarse el canal;
- el índice del siguiente evento.

Ahora esas comprobaciones se realizan una vez por segmento mediante:

- `ProcessEventBoundary()`;
- `ComputeFramesToNextEvent()`.

Dentro del segmento se ejecuta únicamente síntesis, mezcla, captura y avance de los estados por muestra.

## Semántica preservada

Se conserva el comportamiento anterior de procesar como máximo un `MidiStep` vencido por canal y por muestra. Si existen varios eventos con duración cero o el mismo timestamp, se genera un segmento de un frame y el evento siguiente se procesa en la muestra posterior. Esto evita volver a concentrar grandes grupos de eventos en una sola iteración.

El cálculo usa `Ceiling(TimeLeft / Speed)`, por lo que un evento nunca se aplica antes de su frontera.

## Vectorización

El soft clip se trasladó a una pasada posterior sobre el bloque y se implementó con `System.Numerics.Vector<float>`.

En ARM64, cuando el runtime ofrece aceleración SIMD, `Vector<float>` puede traducirse a instrucciones NEON sin acoplar el código directamente a intrínsecos `AdvSimd`. Existe una ruta escalar automática para dispositivos o runtimes sin aceleración.

Se vectoriza la aproximación racional usada previamente:

`x * (27 + x²) / (27 + 9x²)`

seguida de clamp a `[-1, 1]`.

## Beneficios esperados

- Menos ramas y accesos al timeline dentro del hot path por muestra.
- Menor coste en MIDIs con eventos separados por tramos largos.
- Soft clip SIMD portable para ARM64 y otras arquitecturas soportadas por el JIT.
- Sin allocations durante reproducción.
- Sin cambiar NoteOn/NoteOff, sustain, sostenuto, legato, portamento, ChipDrums o seek.

## Límites

La síntesis de voces sigue siendo escalar porque ADSR, fase, portamento, LFO, ruido y compactación mutan estado individual por muestra. Vectorizar esa parte correctamente requiere una representación SoA (structure of arrays) o bloques especializados por forma de onda; hacerlo sobre los structs actuales probablemente añadiría más conversiones que rendimiento.

## Pruebas recomendadas

1. Bad Piggies y Spider Dance para eventos densos.
2. MIDI con cambios de tempo y múltiples eventos de duración cero.
3. Sustain, sostenuto y legato.
4. ChipDrums con golpes superpuestos.
5. Seek repetido durante reproducción y pausa.
6. Velocidades mínima, normal y máxima.
7. Segundo plano con pantalla apagada.
8. Comparar `RenderBlock` y underruns antes/después en el mismo dispositivo.
