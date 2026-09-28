# FFT visual de 8192 puntos y bandas logarítmicas integradas

## Problema

Con 2048 puntos a 44.1 kHz, cada bin cubre aproximadamente 21.53 Hz. En la zona grave hay muy pocos valores FFT independientes para representar 241 barras, por lo que varias barras terminan altamente correlacionadas y forman escalones visuales.

## Cambios

- MIX y canal enfocado usan un FFT visual de 8192 puntos (aprox. 5.38 Hz/bin).
- El bloque de síntesis y el backend de AudioTrack no cambian.
- La ventana de 8192 muestras se toma del historial PCM alineado al cursor de reproducción.
- Como se calcula por VSync, las ventanas se solapan normalmente mucho más del 50%.
- Las miniaturas multicanal conservan FFT de 2048 para controlar el costo.
- Cada barra representa un intervalo logarítmico entre 20 Hz y 16 kHz.
- El valor de una barra se obtiene integrando varias muestras dentro del intervalo.
- Las posiciones fraccionales entre bins se evalúan con interpolación cúbica Catmull-Rom.
- Se usa ponderación triangular suave dentro de cada banda para reducir discontinuidades entre barras vecinas.
- La magnitud se normaliza según el tamaño del FFT para conservar niveles comparables entre 2048 y 8192.

## Resolución aproximada

- FFT 2048: 44100 / 2048 = 21.53 Hz/bin.
- FFT 4096: 44100 / 4096 = 10.77 Hz/bin.
- FFT 8192: 44100 / 8192 = 5.38 Hz/bin.

## Pruebas recomendadas

1. Pistas con bajo sostenido y barridos de subgrave.
2. Polar Express y otros ejemplos donde aparecían bloques en graves.
3. Doctor Who con MIX visible a 60, 90 y 120 Hz.
4. Canal enfocado y vista multicanal.
5. Vigilar FPS y tiempo de render, especialmente a 120 Hz.

Si 8192 por VSync reduce demasiado los FPS, la adaptación prevista es calcular MIX a 60 Hz máximo manteniendo el render a VSync, no reducir otra vez la resolución de graves.
