# FFT layout y ciclo de vida de voces

## Diseño del FFT

En la vista general, los canales individuales ocupan exactamente el tercio inferior del panel. MIX usa los dos tercios superiores y conserva el protagonismo visual. Las vistas enfocadas continúan usando su comportamiento independiente.

## Acumulación de voces

Se corrigieron dos causas:

1. `Channel.MaxVoices` había aumentado a 16, pero `MidiStep` sólo almacenaba 6 NoteOn/NoteOff por frontera. Los eventos adicionales, especialmente NoteOff, podían perderse y dejar voces retenidas. `MidiStep` ahora usa buffers inline de 16 elementos sin allocations.
2. Al terminar un canal, sustain o sostenuto podían permanecer activos para siempre porque ya no llegaría un evento posterior que levantara el pedal. El final del track ahora libera las teclas, limpia ambos pedales y envía todas las voces activas a Release.

El TrackerPanel continúa mostrando hasta seis notas por fila; esto es sólo una limitación visual y ya no limita la semántica del motor.
