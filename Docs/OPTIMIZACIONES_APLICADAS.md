# Optimizaciones aplicadas al motor Chiptune Android

## 1. Portamento precalculado
- Se añadieron `PortamentoTarget`, `PortamentoMultiplier` y `PortamentoActive` por voz.
- `Math.Pow(...)` ahora se ejecuta al configurar una transición, no por voz y por muestra.
- El hot path solo multiplica la frecuencia y comprueba si alcanzó el objetivo.

## 2. LFO mediante tabla lookup
- Tabla senoidal estática de 2048 muestras.
- El LFO usa indexación por fase y elimina `Math.Sin(...)` del bucle principal.
- No se usa interpolación porque para vibrato una tabla de 2048 puntos ya ofrece resolución suficiente y evita trabajo adicional.

## 3. Soft clipping rápido
- Se reemplazó `Math.Tanh(...)` por una aproximación racional:
  `x * (27 + x²) / (27 + 9x²)` con clamp final a `[-1, 1]`.
- Mantiene una saturación suave con un coste muy inferior.
- Puede haber una diferencia tímbrica pequeña frente a `tanh`; conviene comparar con audífonos y archivos densos.

## 4. EQ por bloque y captura separada
- La síntesis primero llena el bloque completo.
- El EQ se procesa una sola vez por bloque de 2048 frames.
- La captura de mezcla post-EQ se realiza en una pasada separada.
- Los buffers de captura de canales y mezcla ahora tienen posiciones de escritura independientes.

## Candidato adicional detectado
El pan todavía calcula `sin/cos` por canal y por muestra. Conviene precalcular `PanGainL/PanGainR` cuando cambia `CcPan`; probablemente sea la siguiente ganancia clara después de medir estas cuatro.

## Validación pendiente
Este entorno no incluye el SDK `dotnet`, así que no fue posible compilar aquí. Los cambios fueron revisados estructuralmente, pero deben compilarse en Visual Studio y probarse con:
- EQ Flat y presets con boosts.
- MIDI con portamento ascendente y descendente.
- LFO lento y rápido.
- Canciones densas de muchos canales.
- Seek y final de pista.

## Segunda ronda: hot path de voces y captura visual

- La captura por canal solo se mantiene activa mientras SpectrumPanel u OscilloscopePanel la solicitan.
- La onda senoidal tonal usa la misma tabla lookup de 2048 entradas, con interpolación lineal.
- Triangle y Saw ya no ejecutan `Math.Floor` por voz y muestra.
- El duty efectivo de Square se precalcula al cambiar CC71.
- Las voces de ruido inactivas se eliminan sin recorrer/compactar el pool completo.
- Los tiempos ADSR solo se reconstruyen cuando Attack, Decay o Release realmente cambian.
- `SoundVariation`, `Resonance` y `Legato` se aplican desde `MidiStep`; antes eran traducidos pero no llegaban al canal.
