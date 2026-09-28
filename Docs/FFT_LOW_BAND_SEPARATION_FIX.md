# Separación de bandas graves del FFT

## Problema

El panel MIX fue ampliado a 241 barras, pero `SpectrumRenderer` todavía usaba límites precalculados exclusivamente para 56 bandas.

Para cualquier índice visual mayor a 55, el código terminaba usando el bin 0 como respaldo. El resultado era que decenas o cientos de barras compartían exactamente la misma magnitud y se desplazaban como un bloque sólido, especialmente cerca de los graves.

Además, con una FFT de 2048 muestras a 44.1 kHz, cada bin representa aproximadamente 21.53 Hz. Las primeras bandas logarítmicas de un espectro de 241 barras son más estrechas que un bin entero, por lo que redondear sus límites a índices enteros también produce duplicados.

## Corrección

- Se eliminó el mapa fijo de 56 bandas.
- Se crea y cachea un mapa específico para cada resolución solicitada, actualmente 241 para MIX y 24 para canales.
- Los centros y radios se conservan en coordenadas fraccionales de bins FFT.
- Las magnitudes se obtienen mediante filtros triangulares alrededor de cada centro.
- Las bandas más estrechas que un bin usan interpolación fraccional como respaldo.
- No se generan allocations durante el render estable; los mapas sólo se construyen una vez por cantidad de bandas.

## Resultado esperado

- Ya no existen barras 57-241 enlazadas accidentalmente al bin 0.
- Las barras graves cercanas dejan de tener exactamente la misma altura.
- La transición entre graves, medios y agudos es más continua.
- Se conserva el cursor PCM ligado a la reproducción y el FFT por VSync.

## Límite físico

Una FFT de 2048 puntos no puede resolver 241 frecuencias realmente independientes en la región de 20-100 Hz. La interpolación mejora la representación gráfica y elimina duplicados artificiales, pero varias barras graves seguirán estando correlacionadas porque parten de la misma información espectral.

Para aumentar la resolución física real sería necesario usar una ventana mayor, un banco de filtros de frecuencia constante o un analizador híbrido exclusivo para graves, a cambio de más latencia y CPU.
