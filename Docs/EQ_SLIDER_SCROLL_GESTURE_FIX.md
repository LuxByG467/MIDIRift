# Corrección de conflicto entre sliders verticales y ScrollView

## Problema

`EqualizerCurveView` vive dentro de un `ScrollView` vertical. Los diez controles de banda también usan arrastre vertical, por lo que Android podía entregar el movimiento al `ScrollView` después de superar el umbral de desplazamiento. El resultado era que la página comenzaba a desplazarse en vez de mover la banda seleccionada.

## Corrección

- Al iniciar o continuar un toque dentro de `EqualizerCurveView`, la vista nativa llama a `RequestDisallowInterceptTouchEvent(true)`.
- El bloqueo se mantiene durante todo el gesto, incluyendo movimientos rápidos que antes activaban el scroll.
- En `Up`, `Cancel`, `EndInteraction` o al desmontar el handler se libera con `RequestDisallowInterceptTouchEvent(false)`.
- El evento nativo no se marca como consumido, por lo que `GraphicsView` conserva sus eventos `StartInteraction`, `DragInteraction` y `EndInteraction`.
- Fuera de la gráfica, el `ScrollView` sigue funcionando normalmente.

## Pruebas recomendadas

1. Arrastrar lentamente cada banda hacia arriba y abajo.
2. Arrastrar rápidamente una banda desde un extremo al otro.
3. Iniciar el gesto sobre la perilla y también sobre la pista vertical.
4. Soltar el dedo fuera de la gráfica.
5. Hacer un gesto cancelado, por ejemplo, al cambiar de aplicación durante el arrastre.
6. Comprobar que la página todavía pueda desplazarse al arrastrar fuera de `EqualizerCurveView`.
