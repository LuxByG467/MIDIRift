# Reimplementación clean-room del FFT Android

El panel FFT fue reescrito desde cero a partir de un contrato de comportamiento observable, sin copiar código, recursos ni estructura interna de reproductores externos.

## Objetivos

- Render fluido a la cadencia de la pantalla.
- Análisis independiente a 50 Hz.
- 241 bandas en MIX.
- Vista multicanal ligera con 24 bandas por canal.
- Canal enfocado con análisis continuo.
- Cero allocations en el render estable.
- Sin interpolación histórica ni retraso artificial de un frame FFT.

## Arquitectura

1. `SpectrumPanel` inicia un worker dedicado al conectarse.
2. El worker captura audio y calcula FFT con deadlines absolutos.
3. Los resultados se publican mediante front/back buffers e intercambio atómico.
4. `SKGLView` renderiza continuamente.
5. Cada barra sigue su objetivo con ataque y caída exponenciales basados en `deltaTime`.
6. La geometría se conserva en arrays de `SKPoint` y se envía con pocas draw calls.

## Decisiones de rendimiento

- MIX: 241 bandas a 50 análisis por segundo.
- Canales pequeños: 24 bandas, dos canales por ciclo en round-robin.
- Canal enfocado: captura y análisis exclusivo a 50 Hz.
- Agrupación por máximo de potencia para conservar transitorios estrechos.
- Sin `MainThread.BeginInvokeOnMainThread` por frame FFT.
- Sin timer externo de `MainPage` para Spectrum.

## Pruebas recomendadas

- Doctor Who con MIX visible.
- Vista multicanal con 16 o más canales.
- Entrar y salir de canal enfocado.
- Pantallas de 60, 90 y 120 Hz.
- Cambio rápido entre MIDI y MP3.
- Abrir/cerrar EQ y volver al FFT.
- Reproducción prolongada en segundo plano y retorno a la app.
