# Restauración de etiquetas de métricas en FFT

Se restauró en `SpectrumPanel` el mismo bloque compacto de métricas que utiliza
`OscilloscopePanel`:

- RMS
- RMS dB
- PEAK y PEAK dB
- CREST
- PEAK OK / CLIP con retención de 1.5 segundos

Las etiquetas respetan `MeasurementLabelsSettings.Enabled` y utilizan el color
de acento morado del panel FFT. No se añadieron métricas de rendimiento ni
información adicional para evitar saturar la pantalla de Android.

Las métricas se dibujan tanto en MIX como al enfocar un canal.
