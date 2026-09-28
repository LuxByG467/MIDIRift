# FFT por VSync con ventana PCM deslizante

## Problema

El visualizador anterior calculaba FFT a una cadencia fija y el render mostraba o suavizaba snapshots discretos. Aunque el render alcanzara 60/90/120 FPS, el objetivo sólo cambiaba 30-50 veces por segundo, por lo que seguía percibiéndose un escalonado temporal.

## Nuevo modelo

`SpectrumPanel.OnPaintSurface()` realiza ahora este flujo en cada VSync:

1. Copia las 2048 muestras PCM más recientes desde `IPanelAudioSource`.
2. Aplica ventana Hann.
3. Ejecuta FFT de 2048 puntos.
4. Convierte la potencia a dB y 241 bandas logarítmicas.
5. Aplica ataque/caída asimétricos usando `deltaTime` real.
6. Dibuja usando buffers persistentes de puntos.

A 120 Hz y 44.1 kHz, el audio avanza unas 367 muestras entre frames. Las ventanas de 2048 muestras se solapan ampliamente, de modo que cada FFT representa un instante ligeramente posterior al anterior en vez de una fotografía repetida.

## Vista multicanal

Hacer MIX + todos los canales en cada VSync multiplicaría innecesariamente el coste. Por ello:

- MIX: una FFT nueva por cada frame visual.
- Canal enfocado: una FFT nueva por cada frame visual.
- Vista multicanal: dos canales actualizados por frame en round-robin, con suavizado continuo de todos los canales.

## Eliminado

- Worker de análisis periódico.
- Deadlines de 50 Hz.
- Snapshots front/back del FFT.
- Secuencias de publicación.
- Interpolación entre fotografías espectrales.

## Consideraciones

Este modelo mueve el coste de la FFT al hilo de render GL. En un dispositivo de 120 Hz implica hasta 120 FFT de 2048 puntos por segundo para MIX. Debe medirse en el Poco F3 con Doctor Who, EQ y paneles activos.

Si el render no alcanza VSync, la siguiente adaptación recomendada no es volver a snapshots discretos, sino usar una resolución dinámica: 2048 puntos a 60 Hz y 1024 puntos por encima de 60 Hz, manteniendo una ventana deslizante por frame.
