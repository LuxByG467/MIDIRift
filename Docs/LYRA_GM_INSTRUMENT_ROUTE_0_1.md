# Lyra GM Instrument Route 0.1

## Objetivo
Evitar que la mayoría de los programas General MIDI terminen en Pulse50/Square y restaurar Sine como onda nativa real de Lyra.

## Cambios
- `ChiptuneWaveType.Sine` vuelve a existir en el núcleo Lyra.
- `LyraWaveTypeBridge` ya no degrada `WaveType.Sine` a `Pulse50`.
- `WaveGeneratorRegistry` y los fast paths de `VoicePool` sintetizan Sine con `FastAudioMath.Sin01`.
- El séptimo bucket ya reservado por `WaveBucketCount = 7` se usa para completar la distribución de ondas.
- `FromProgram()` ahora distribuye las 16 familias GM entre Pulse25, Triangle, Sine, BassHybrid, Saw y Pulse50.
- Canal MIDI 10 sigue siendo percusión (`midiChannel == 9` dentro de Lyra, porque allí es 0-based).
- `Sin` se expone como nombre de Sine en el selector Lyra.

## Mapa GM Lyra
- 0-7 Piano: Pulse25
- 8-15 Chromatic: Triangle
- 16-23 Organ: Sine
- 24-31 Guitar: Pulse25
- 32-39 Bass: BassHybrid
- 40-47 Strings: Saw
- 48-55 Ensemble: Saw
- 56-63 Brass: Pulse50
- 64-71 Reed: Pulse25
- 72-79 Pipe: Sine
- 80-87 Synth Lead: Saw
- 88-95 Synth Pad: Triangle
- 96-103 Synth FX: Sine
- 104-111 Ethnic: Triangle
- 112-119 Percussive melodic: Pulse25
- 120-127 Sound FX: Pulse50

## Nota
Pulse12 del Compatibility/Common layer todavía se aproxima con Pulse25 en Lyra. Este cambio no toca el package/versionCode de Android.
