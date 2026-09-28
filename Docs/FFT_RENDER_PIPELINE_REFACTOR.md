# Refactor del pipeline FFT de Android

## Objetivo

Separar la frecuencia de análisis de la frecuencia visual y eliminar la reconstrucción de `SKPath` por frame.

## 1. Analizador independiente con cadencia absoluta

- El FFT corre aproximadamente a 30 Hz (`33 ms`).
- `StartPanelTimer` conserva un deadline absoluto.
- El tiempo consumido por el FFT se descuenta del siguiente intervalo.
- Si Android suspende el proceso o se pierde un deadline, se saltan los análisis vencidos en vez de acumularlos.
- El hilo de análisis publica únicamente arrays objetivo mediante doble buffer e intercambio atómico.

## 2. Render continuo e interpolación temporal

- `SpectrumPanel` activa `SKGLView.HasRenderLoop` sólo mientras está conectado y visible.
- El render consume el último FFT disponible sin esperar uno nuevo.
- El suavizado se movió de `SpectrumRenderer` al render.
- Attack y release usan `deltaTime` real:
  - la respuesta visual conserva la misma velocidad a 60, 90 o 120 Hz;
  - un FFT de 30 Hz puede producir animación fluida a la cadencia de pantalla.
- `Tick()` ya no llama `InvalidateSurface` ni cruza al hilo principal.

## 3. Renderer especializado sin paths por frame

- Se eliminaron los rectángulos añadidos a `SKPath` para cada barra.
- Cada barra es una línea vertical almacenada en arrays persistentes de `SKPoint`.
- MIX se dibuja en una llamada `DrawPoints`.
- Todos los canales se empaquetan en un solo buffer y se dibujan en otra llamada.
- El canal enfocado reutiliza el buffer de MIX.
- No se crean arrays, paths ni geometría temporal durante cada frame.

## Cambios en SpectrumRenderer

Se añadió `ProcessRaw`, que calcula exclusivamente el FFT y el mapeo de bandas. El método anterior `Process` se conserva como wrapper compatible para otros callers.

## Pruebas recomendadas

1. Doctor Who con MIX visible durante varios minutos.
2. Vista MIX + todos los canales.
3. Entrar y salir de un canal enfocado.
4. Pantallas de 60, 90 y 120 Hz.
5. Cambio entre MIDI con diferente cantidad de canales.
6. Abrir y cerrar el panel repetidamente para confirmar que el render loop se detiene.
7. Reproducción con EQ y métricas activas.
8. Uso en segundo plano para verificar que el panel desconectado no siga renderizando.

## Nota

El renderer sigue usando Skia/OpenGL mediante `SKGLView`, pero ahora su geometría está especializada para barras. Esto evita introducir una segunda pila gráfica con OpenGL ES directo y conserva compatibilidad con el resto de MAUI.
