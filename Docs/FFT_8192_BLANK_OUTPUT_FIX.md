# Corrección del FFT 8192 sin salida visible

## Causa

El analizador de 8192 aplicaba un factor `2 / N` antes de convertir la potencia a dB. Según la escala que entrega la versión de NAudio usada por el proyecto, eso podía volver a normalizar una FFT ya escalada y empujar prácticamente todo el espectro por debajo del piso visual de -80 dB.

El render además atrapaba cualquier excepción de forma silenciosa, por lo que un error dentro del analizador producía únicamente una pantalla negra.

## Cambios

- Se restauró la misma convención de magnitud que usaba el FFT de 2048 funcional: potencia nativa de NAudio, conversión a dB y normalización visual.
- Se añadieron comprobaciones de `NaN` e infinito.
- Los errores del frame se muestran como `FFT ERROR: <tipo>` además de escribirse en Debug.
- Se conserva FFT 8192, ventana Hann, cursor de reproducción y bandas logarítmicas cúbicas.
