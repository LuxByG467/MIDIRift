# Interpolación temporal del FFT

## Problema

El render continuo podía ejecutar a 60/90/120 Hz, pero el analizador publicaba un espectro nuevo aproximadamente a 30 Hz. El suavizado exponencial perseguía el objetivo recién publicado, se acercaba rápidamente y después permanecía casi inmóvil hasta la siguiente publicación. Esto hacía visible el escalonado entre ventanas FFT aunque el contador de FPS fuera alto.

## Solución

El panel conserva los dos espectros publicados más recientes junto con sus timestamps. El render trabaja una ventana FFT por detrás y recorre una interpolación `smoothstep` entre ambos usando el intervalo real medido entre publicaciones.

- MIX: latencia visual aproximada de 33 ms.
- Canal enfocado: la misma latencia aproximada.
- Vista multicanal: cada canal interpola según su propia cadencia escalonada.
- Sin allocations por frame.
- El audio no se retrasa; sólo la representación visual.

Este diseño evita intentar adivinar el siguiente espectro. Siempre interpola entre dos estados reales ya calculados.
