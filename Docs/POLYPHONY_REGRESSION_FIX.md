# Corrección de regresión de polifonía

## Causas encontradas

1. El voice stealing puntuaba las voces por `EnvLevel * Velocity`. Las voces recién creadas empiezan con `EnvLevel = 0`, por lo que durante acordes densos eran consideradas las mejores víctimas y podían sobrescribirse repetidamente antes de producir una sola muestra.
2. CC68/Legato reutilizaba una voz existente aunque el canal tuviera varias teclas sostenidas o el mismo step contuviera un acorde. Eso podía convertir canales polifónicos en monofónicos.
3. El límite fijo de seis voces era demasiado bajo para MIDIs modernos que concentran acompañamiento y melodía en un solo canal.

## Cambios

- Polifonía tonal por canal: 6 -> 16 voces.
- Cada voz recibe un orden monotónico de creación.
- El robo prioriza voces inactivas, soltadas o en Release.
- Si todas las voces siguen sostenidas, se roba la más antigua, no la recién creada con menor envolvente.
- Legato sólo reutiliza una voz cuando hay exactamente una nota nueva y exactamente una voz sostenida activa.

## Pruebas sugeridas

- Acordes de más de seis notas en un único canal.
- Arpegios rápidos con releases largos.
- Varias repeticiones solapadas de la misma nota.
- Sustain y sostenuto.
- CC68 en melodías monofónicas y en canales con acordes.
- Doctor Who, Spider Dance y Bad Piggies.
