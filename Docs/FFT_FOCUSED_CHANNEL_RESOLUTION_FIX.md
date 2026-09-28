# Corrección de resolución del canal enfocado

## Síntoma

Al pasar de la vista general a un canal enfocado, el espectro se convertía en
unas pocas columnas muy anchas y escalonadas, aunque MIX conservara sus 241
barras finas.

## Causa

La vista general usa deliberadamente 24 bandas por canal para que la rejilla
multicanal sea barata. La vista enfocada calculaba el FFT de 8192 muestras,
pero escribía el resultado en `_channelTarget[focused]`, cuyo tamaño seguía
siendo de 24 bandas. Después esas 24 alturas se estiraban a todo el ancho de la
pantalla.

## Corrección

- Se añadieron buffers exclusivos de 241 bandas para el canal enfocado.
- El analizador visual de 8192 muestras escribe ahora en `_focusedTarget`.
- El suavizado usa `_focusedDisplay` y una geometría propia de 241 barras.
- Los buffers enfocados se limpian al entrar, salir o cambiar de foco, evitando
  arrastrar la forma del canal anterior.
- Las miniaturas multicanal conservan 24 bandas y su menor costo.

No se modificó el motor de audio, el cursor de reproducción ni el mapeo
logarítmico del FFT.
