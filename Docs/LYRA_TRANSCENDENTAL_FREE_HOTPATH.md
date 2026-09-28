# 0.12.4D3-alpha — Lyra transcendental-free audio path

## Objetivo
Eliminar operaciones trascendentales del camino de síntesis/DSP de audio y reducir el coste por voz observado por la instrumentación D2, sin bajar sample rate ni polifonía.

## Cambios
- Nuevo `FastAudioMath`: seno de fase normalizada, exp2/pow/log2 auxiliar, exp(-x) y sqrt mediante aproximaciones algebraicas/bit-level. No llama Sin/Exp/Pow/Log/Sqrt/Tanh.
- Lyra VoicePool: LFO, BassHybrid, note->Hz, portamento y pan ya no llaman trascendentales.
- Pan equal-power se calcula al cambiar el CC Pan y se cachea en `ChannelState`; el render sólo multiplica por `PanGainL/R`.
- Drums: se eliminaron Sin y Exp del render por muestra; los low-pass usan la aproximación RT.
- Bass Restoration: coeficientes sin Exp y limiter sin Tanh (aproximación racional).
- Graphic EQ: cálculo de coeficientes sin Pow/Sin/Cos.
- Release fast-path: el criterio de cola inaudible se desacopló de `LyraMasterVoiceGain`. Una release por debajo de ~-84 dB internos puede avanzar por estado sin sintetizar la onda.

## No se cambió
- Sample rate, polifonía máxima, scheduler, ring buffers, AAudio/AudioTrack, ADSR funcional, Trigger ni capturas.
- VersionCode/ApplicationDisplayVersion de Android.

## Nota de calidad
Las aproximaciones son continuas y deterministas, pero BassHybrid, vibrato y capas sinusoidales de batería pueden diferir mínimamente de `MathF.Sin/Pow`. Debe validarse auditivamente junto con el rendimiento.
