# Restauración de sesión de reproducción

La opción «recordar última canción» ahora conserva también el contexto de reproducción:

- Id estable y nombre de la playlist activa.
- TrackId de la canción actual.
- Índice de la entrada dentro de la playlist.
- Ruta del archivo como fallback.

## Comportamiento al iniciar

1. Se valida que la última ruta todavía exista.
2. Se busca la playlist por su Id estable.
3. Para migrar preferencias antiguas, si no hay coincidencia por Id se intenta por nombre.
4. La posición se reconstruye por TrackId; el índice guardado se usa sólo como fallback.
5. Se carga la canción y se conserva el contexto para Next, Prev, repetición y autoavance.
6. Si la playlist o la entrada ya no existen, se restaura únicamente la canción y se limpia el contexto inválido.

Las playlists reales ahora tienen un `id` persistente en playlists.json. Las playlists virtuales usan identificadores deterministas (`virtual:all`, `virtual:midi`, etc.). Los archivos antiguos se migran automáticamente al iniciar.
