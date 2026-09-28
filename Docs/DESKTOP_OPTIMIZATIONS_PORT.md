# Port de optimizaciones Desktop a Android

## Resumen

Se comparó el núcleo de síntesis de Desktop (`Channel.cs`, `Voice.cs` y `ChiptuneNAudio.cs`) con el motor Android (`Channel.cs`, `Voice.cs` y `Platforms/Android/ChiptuneAudioTrack.cs`).

Android ya incluía muchas optimizaciones equivalentes o posteriores:

- pools fijos de voces y percusión sin allocations durante playback;
- ADSR e incrementos precalculados;
- portamento exponencial precalculado;
- ganancias de paneo precalculadas;
- tabla senoidal para LFO/oscilador;
- generador XorShift sin `Random` ni locks;
- ruta rápida para canales silenciosos;
- captura visual desacoplada;
- productor y consumidor separados por ring buffer;
- parámetros de bloque y buffer configurables;
- semántica MIDI más completa y ChipDrums, ausentes en el Desktop original analizado.

## Cambio aplicado

### Snapshot contiguo del track para reproducción

Desktop convierte los pasos MIDI a almacenamiento contiguo antes de reproducir. Android mantenía `List<MidiStep>` en el hot path del secuenciador.

Se añadió a `Channel`:

- `MidiStep[] PlaybackTrack`;
- `PlaybackTrackCount`;
- `SealPlaybackTrack()`.

`ChiptuneAudioTrack` sella el track una sola vez al construir el engine y usa el array en:

- cálculo de duración;
- render normal;
- seek/reconstrucción rápida;
- comprobación de final de canal;
- límites del índice.

Beneficios esperados:

- acceso contiguo y más amigable con caché;
- elimina llamadas repetidas a `List<T>.Count` y su indexador en el hilo de síntesis;
- garantiza que el parser/UI no modifique accidentalmente el track que consume el audio;
- sólo una allocation por canal al iniciar una canción, nunca durante reproducción.

No se usó `ArrayPool<MidiStep>` porque cada snapshot vive durante toda la sesión del engine y el ahorro sería mínimo frente a la complejidad de devolver arrays de forma segura al detener, hacer seek, reemplazar motores o sufrir una excepción.

## Optimizaciones Desktop no copiadas literalmente

### AVX2 / `Vector256<float>`

Desktop usa AVX/AVX2 de x86-64. La mayoría de dispositivos Android son ARM64, donde esas instrucciones no existen. Copiar ese código impediría compilar o forzaría una ruta que nunca se ejecutaría.

Port correcto futuro:

1. implementar primero una ruta neutral con `System.Numerics.Vector<float>`;
2. medir si .NET Android la convierte a AdvSimd/NEON en dispositivos reales;
3. sólo después considerar una ruta explícita `System.Runtime.Intrinsics.Arm.AdvSimd`;
4. mantener ruta escalar para ARM32, x64 de emulador y dispositivos sin soporte.

### Render completo por subbloques de 64/128 frames

Desktop puede hacerlo porque su secuenciador original procesa eventos con un modelo más simple. Android tiene actualmente:

- NoteOn/NoteOff corregidos;
- sustain, sostenuto y legato;
- ChipDrums con voces one-shot;
- cambios pendientes desde UI aplicados en frontera de bloque;
- seek que reconstruye estado;
- velocidad variable;
- eventos densos deliberadamente repartidos entre muestras consecutivas.

Sustituir el render muestra-a-muestra por el `RenderBlock` de Desktop perdería o alteraría estas funciones. Debe escribirse un render segmentado: dividir cada bloque en tramos hasta el próximo evento MIDI, procesar cada tramo vectorizable y aplicar el evento exactamente en su frontera.

### `Voice` de 52 bytes completamente `float`

El Desktop analizado elimina campos que Android necesita (`MidiNote`, `KeyDown`, objetivos de portamento y otros estados). Cambiar todos los `double` por `float` también puede alterar afinación, acumulación de fase y portamento en sesiones largas.

Una migración segura requiere benchmarks de:

- deriva de afinación tras varias horas;
- seek repetido;
- pitch bend y vibrato;
- portamento lento;
- MIDIs con muchos NoteOff, sustain y legato.

### Buffers pinned y `Unsafe.InitBlockUnaligned`

El backend Android produce bloques mucho mayores y usa buffers intercalados que después entrega a `AudioTrack`. Fijar permanentemente más buffers en el heap móvil puede empeorar la compactación del GC. Antes de portarlo hay que medir si `Array.Clear`, `Span.Clear` o la sobrescritura completa representan siquiera un porcentaje relevante.

## Siguiente fase recomendada

La mejora de mayor impacto potencial es un **render segmentado por eventos**, no una copia directa del motor Desktop:

- obtener la distancia en frames hasta el siguiente evento de cada canal;
- procesar `min(distanciaEvento, framesRestantes)` como tramo continuo;
- especializar por forma de onda fuera del bucle de voces;
- vectorizar mezcla, envelope y paneo con `Vector<float>`;
- conservar una ruta escalar para portamento, ruido DMG, ChipDrums y tramos muy cortos;
- comparar salida contra el engine actual con tolerancia numérica y MIDI de regresión.

## Validación pendiente

El entorno usado para preparar este ZIP no contiene el SDK de .NET/Android, por lo que no fue posible ejecutar `dotnet build`. La modificación aplicada es pequeña y estructural, pero debe compilarse y probarse en Visual Studio antes de reemplazar la rama principal.
