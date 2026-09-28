# Corrección de estabilidad del EQ en MIDI denso

## Causa

La implementación anterior recorría las 10 bandas para cada frame estéreo en cuanto una sola banda o el preamp dejaban de estar en 0 dB. En MIDI denso, la síntesis ya consume gran parte del presupuesto del bloque; sumar siempre 10 biquads estéreo podía provocar underruns.

Además, `SetBandGain` recalculaba coeficientes desde el hilo de UI mientras el hilo de audio procesaba muestras, permitiendo leer una estructura de coeficientes durante su actualización.

## Cambios

- Sólo se procesan bandas cuya ganancia sea distinta de 0 dB.
- El bucle ahora es banda -> bloque, manteniendo coeficientes y estados en variables locales.
- Se reemplazó Direct Form I por Direct Form II transpuesta, reduciendo estados y accesos de memoria.
- Los cambios de sliders se publican de forma lock-free y se aplican al principio de `ProcessBlock`/`ProcessInterleaved`.
- Al cambiar coeficientes se reinicia únicamente el estado de esa banda, evitando explosiones por historial incompatible.
- El preamp se aplica en una pasada separada vectorizada con `Vector<float>`.
- El bypass Flat vuelve a ser prácticamente gratuito.

## Coste esperado

Con una banda activa, el EQ hace aproximadamente 1/10 del trabajo de filtrado anterior. Con cinco bandas activas, aproximadamente la mitad. Con las diez activas, la forma II transpuesta y el recorrido por bloque siguen reduciendo accesos y ramas.

## Pruebas recomendadas

- Doctor Who con Flat, una banda, varias bandas y presets completos.
- Mover sliders durante reproducción.
- Activar/desactivar EQ repetidamente.
- MIDI y MP3.
- Preamp sin bandas activas.
- Reproducción prolongada en segundo plano.
