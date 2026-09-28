# Optimizaciones portadas de los paneles Desktop a Android

## Resumen

Se compararon `SpectrumPanel`, `SpectrumRenderer` y `OscilloscopePanel` de Desktop con la versión Android más reciente.

Android ya incluía varias optimizaciones iguales o mejores que Desktop:

- FFT de 2048 puntos en lugar de 4096 para reducir carga móvil.
- Límites de bins visuales logarítmicos precalculados.
- Magnitud en potencia usando `10 * log10(re² + im²)`, sin `sqrt`.
- Una sola FFT de canal por tick, manteniendo MIX a cadencia completa.
- Copia escalonada de dos canales del osciloscopio por tick.
- Trabajo DSP y copia de muestras fuera del hilo de UI.
- Número de puntos de onda limitado al ancho real en píxeles.
- Glow desactivado en Android por su elevado costo.

Estas decisiones se conservaron.

## 1. Doble buffer para resultados FFT

Antes, el hilo de background escribía `MixOut` y `ChOut` mientras `SKGLView` podía leerlos desde el hilo GL. No causaba necesariamente una excepción, pero sí podía producir frames parcialmente actualizados, barras partidas o pequeñas inconsistencias visuales.

Ahora cada salida tiene:

- buffer frontal, sólo para dibujo;
- buffer trasero, sólo para cálculo;
- intercambio atómico de referencias al terminar cada FFT.

El render toma una referencia estable mediante `Volatile.Read`.

## 2. Doble buffer para capturas del osciloscopio

Cada señal MIX/canal usa ahora dos `WaveFrame`:

- `Front`: frame publicado para la GPU;
- `Back`: frame que rellena el hilo de actualización.

Al terminar la copia se intercambian mediante `Interlocked.Exchange`. Esto evita que el render recorra una onda mientras la fuente reemplaza sus muestras.

## 3. Trigger calculado fuera del render

El cruce ascendente por cero se buscaba dentro de `OnPaintSurface` para MIX y para cada canal en cada frame.

Ahora `FindTrigger` se ejecuta una sola vez al capturar una señal y su resultado se guarda junto a las muestras en `WaveFrame.TriggerOffset`.

El hilo GL queda limitado a construir geometría y dibujar.

## 4. Un solo DrawPath para todos los canales

Desktop agrupa las ondas de los canales en un path reutilizable. Android hacía una llamada `DrawPath` por canal.

Ahora Android:

1. reinicia `_channelsPath` una vez;
2. agrega cada onda como un subtrazo independiente;
3. realiza una sola llamada `canvas.DrawPath` para todos los canales.

MIX y canal enfocado también usan paths persistentes reutilizables. No se crean `SKPath` por frame.

## Cambios que no se portaron

### Render custom de Avalonia/GRContext

Desktop usa `ICustomDrawOperation` para entrar directamente al contexto Skia de Avalonia. Android ya usa `SKGLView`, por lo que ese puente no aporta nada.

### Fósforo, burn-in y glow downsampled

Son efectos visuales costosos y no reducen el trabajo necesario para mostrar la señal. El glow ya estaba desactivado en Android deliberadamente.

### Modo XY

Es una característica visual, no una optimización. Requiere captura L/R y una ruta de render distinta.

### Captura de todos los canales cada frame

Desktop puede permitírselo mejor. Android mantiene actualización escalonada para no competir con el motor MIDI en archivos densos.

## Archivos modificados

- `Platforms/Android/SpectrumPanel.cs`
- `Platforms/Android/OscilloscopePanel.cs`

## Pruebas recomendadas

- MIDI con 1, 16, 32 y más canales.
- Doctor Who con FFT y osciloscopio alternados.
- Cambio rápido entre canciones con diferente número de canales.
- Entrar y salir repetidamente de un canal enfocado.
- Arrastrar la seekbar mientras el panel está visible.
- Activar métricas y EQ simultáneamente.
- Dejar el osciloscopio visible durante al menos 30 minutos.

Se debe observar especialmente estabilidad visual, FPS, tiempo de `Tick` y ausencia de `IndexOutOfRangeException` o artefactos de frames parcialmente escritos.
