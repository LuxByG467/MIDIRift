using NAudio.Midi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;
using Microsoft.Maui.ApplicationModel;

namespace MIDIRift;

/// <summary>
/// Port del PlaylistController de escritorio a MAUI/Android.
///
/// Diferencias respecto al original (Desktop/PlaylistController.cs):
///   - Persistencia: usa FileSystem.AppDataDirectory (MAUI) en lugar de
///     Environment.SpecialFolder.ApplicationData (no fiable en Android).
///   - Escaneo (StartScan/ScanDirectory/EnumerateMediaFiles): portado 1:1
///     en cuanto a lógica, pero la raíz de búsqueda es el almacenamiento
///     compartido de Android (Android.OS.Environment.ExternalStorageDirectory)
///     en lugar de MyMusic/MyDocuments/Desktop/UserProfile de Windows.
///     Requiere el permiso especial "Acceso a todos los archivos"
///     (MANAGE_EXTERNAL_STORAGE, API 30+) — ver NeedsStorageAccess/
///     RequestStorageAccess. En versiones más viejas de Android no hace
///     falta ese permiso especial.
///   - BuildMp3Track ya no usa NAudio.Wave.Mp3FileReader (requiere Media
///     Foundation de Windows y no funciona en Android). En Android se
///     usa MediaMetadataRetriever para leer la duración del MP3.
///   - Los callbacks que en desktop se despachaban con
///     Avalonia.Threading.Dispatcher.UIThread se reemplazan por
///     MainThread.BeginInvokeOnMainThread donde corresponde.
///
/// El resto de la lógica (biblioteca, playlists reales/virtuales,
/// favoritos, navegación Prev/Next/NextAuto, persistencia JSON) es
/// una migración 1:1 del comportamiento de escritorio.
/// </summary>
public class PlaylistController
{
    // ── Persistencia ──────────────────────────────────────────────────────
    private static readonly string SaveDir = AppDataPaths.Root;
    private static readonly string PlaylistsPath = AppDataPaths.Playlists;
    private static readonly string LibraryPath = AppDataPaths.Library;

    // ── Biblioteca central ────────────────────────────────────────────────
    public List<LibraryTrack> Library { get; private set; } = new();

    // ── Estado ────────────────────────────────────────────────────────────
    public List<Playlist> Playlists { get; private set; } = new();

    private const string FavoritesPlaylistName = "⭐ Favoritos";
    private const string AllMidiPlaylistName = "Todos los MIDIs";
    private const string AllMp3PlaylistName = "Todos los MP3";

    // Nombres y límites de las playlists virtuales
    private const string VirtualRecentName = "🕐 Recientes";
    private const string VirtualTopName = "🔥 Más reproducidos";

    // Públicos porque LibraryPage necesita ubicar estas playlists por nombre
    // (controller.Playlists.FirstOrDefault(p => p.Name == ...)) para mostrar
    // la pestaña "Toda la música" filtrada por formato.
    public const string VirtualMidiLibraryName = "🎵 Biblioteca MIDI";
    public const string VirtualMp3LibraryName = "🎼 Biblioteca MP3";

    // Combinación de VirtualMidiLibraryName + VirtualMp3LibraryName en una
    // sola playlist virtual, para la vista general "Toda la música" de
    // LibraryPage cuando el filtro de biblioteca es "Todo" (ni MIDI ni MP3
    // en particular). Mismo criterio que las otras dos: se reconstruye en
    // RebuildVirtualPlaylists, nunca se persiste, nunca se edita a mano.
    public const string VirtualAllLibraryName = "🎧 Toda la música";

    private const int VirtualMaxEntries = 30;

    public int ActivePlaylistIndex { get; private set; } = -1;
    public int CurrentEntryIndex { get; private set; } = -1;
    internal bool _looping = false;

    public Playlist? ActivePlaylist =>
        ActivePlaylistIndex >= 0 && ActivePlaylistIndex < Playlists.Count
            ? Playlists[ActivePlaylistIndex]
            : null;

    public bool HasPrev =>
        ActivePlaylist != null && CurrentEntryIndex > 0;

    public bool HasNext =>
        ActivePlaylist != null &&
        CurrentEntryIndex < ActivePlaylist.Entries.Count - 1;

    // ── Eventos ───────────────────────────────────────────────────────────
    public event Action? PlaylistsChanged;
    public event Action<LibraryTrack>? EntryChanged;
    public event Action? ScanProgressChanged;

    // ── Escaneo ───────────────────────────────────────────────────────────
    public bool IsScanning { get; private set; } = false;
    public int ScanFound { get; private set; } = 0;
    public bool HasScanned { get; private set; } = false;

    /// <summary>Texto-resumen del último escaneo terminado (para mostrarlo un rato tras completar).</summary>
    public string? LastScanSummary { get; private set; }

    // Progreso reportado en vivo mientras se escanea. _scanCurrentDir es
    // 'volatile' porque lo escribe el hilo de fondo y lo lee la UI; los
    // contadores se tocan con Interlocked por la misma razón.
    private volatile string _scanCurrentDir = "";
    private int _scanDirsVisited;
    private System.Diagnostics.Stopwatch? _scanStopwatch;

    public string ScanCurrentFolder => _scanCurrentDir;
    public int ScanDirsVisited => _scanDirsVisited;
    public TimeSpan ScanElapsed => _scanStopwatch?.Elapsed ?? TimeSpan.Zero;

    // Buffer de resultados encontrados por el hilo de escaneo. En vez de
    // despachar un cambio a la UI por cada carpeta (lo que satura el hilo
    // principal cuando hay miles de carpetas), se acumulan acá y se
    // "vuelcan" a Library/Playlists cada cierto intervalo.
    private readonly List<(LibraryTrack track, TrackMediaType type)> _scanBuffer = new();
    private readonly object _scanBufferLock = new();
    private DateTime _lastScanFlushUtc = DateTime.MinValue;
    private DateTime _lastScanHeartbeatUtc = DateTime.MinValue;
    private static readonly TimeSpan ScanFlushInterval = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan ScanHeartbeatInterval = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// En Android, escanear todo el almacenamiento compartido requiere el
    /// permiso especial "Acceso a todos los archivos" (MANAGE_EXTERNAL_STORAGE,
    /// API 30+). Es distinto de un permiso runtime normal: se otorga desde
    /// una pantalla de Ajustes del sistema, no con un diálogo in-app.
    /// En versiones anteriores a Android 11 no hace falta.
    /// </summary>
    public bool NeedsStorageAccess
    {
        get
        {
#if ANDROID
            return Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.R
                && !Android.OS.Environment.IsExternalStorageManager;
#else
            return false;
#endif
        }
    }

    /// <summary>
    /// Abre la pantalla de ajustes del sistema donde el usuario puede otorgar
    /// "Acceso a todos los archivos" a la app. No hay forma de pedir este
    /// permiso con un diálogo estándar como los demás permisos runtime.
    /// </summary>
    public void RequestStorageAccess()
    {
#if ANDROID
        try
        {
            var context = Android.App.Application.Context;
            var intent = new Android.Content.Intent(
                Android.Provider.Settings.ActionManageAppAllFilesAccessPermission);
            intent.SetData(Android.Net.Uri.Parse("package:" + context.PackageName));
            intent.AddFlags(Android.Content.ActivityFlags.NewTask);
            context.StartActivity(intent);
        }
        catch
        {
            // Algunos fabricantes no implementan esa pantalla específica;
            // como fallback abrimos el detalle de la app a secas.
            try
            {
                var context = Android.App.Application.Context;
                var intent = new Android.Content.Intent(
                    Android.Provider.Settings.ActionApplicationDetailsSettings);
                intent.SetData(Android.Net.Uri.Parse("package:" + context.PackageName));
                intent.AddFlags(Android.Content.ActivityFlags.NewTask);
                context.StartActivity(intent);
            }
            catch { }
        }
#endif
    }

    // ── Constructor ───────────────────────────────────────────────────────
    public PlaylistController()
    {
        Load();
    }

    // ── Tipo de archivo ───────────────────────────────────────────────────

    private static TrackMediaType? ClassifyExtension(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".mid" or ".midi" => TrackMediaType.Midi,
            ".mp3" => TrackMediaType.Mp3,
            _ => null
        };
    }

    // ── Playlists virtuales ───────────────────────────────────────────────

    /// <summary>
    /// Reconstruye las playlists virtuales en memoria a partir de los
    /// contadores que viven en Library (persistidos en library.json).
    /// Se llama al arrancar y cada vez que cambia una reproducción.
    /// </summary>
    private void RebuildVirtualPlaylists()
    {
        string? activeName = ActivePlaylist?.Name;
        int entryBefore = CurrentEntryIndex;

        Playlists.RemoveAll(p => p.IsVirtual);

        var toInsert = new List<Playlist>();

        var midiLibEntries = Library
            .Where(t => t.MediaType == TrackMediaType.Midi)
            .OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
            .Select(t => new PlaylistEntry { TrackId = t.Id })
            .ToList();

        var mp3LibEntries = Library
            .Where(t => t.MediaType == TrackMediaType.Mp3)
            .OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
            .Select(t => new PlaylistEntry { TrackId = t.Id })
            .ToList();

        toInsert.Add(new Playlist { Id = "virtual:midi", Name = VirtualMidiLibraryName, IsVirtual = true, Entries = midiLibEntries });
        toInsert.Add(new Playlist { Id = "virtual:mp3", Name = VirtualMp3LibraryName, IsVirtual = true, Entries = mp3LibEntries });

        // Combinada — mismo criterio de orden (por título) que las dos de
        // arriba, mezclando ambos formatos. Ver comentario de
        // VirtualAllLibraryName.
        var allLibEntries = Library
            .OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
            .Select(t => new PlaylistEntry { TrackId = t.Id })
            .ToList();
        toInsert.Add(new Playlist { Id = "virtual:all", Name = VirtualAllLibraryName, IsVirtual = true, Entries = allLibEntries });

        var topEntries = Library
            .Where(t => t.PlayCount > 0)
            .OrderByDescending(t => t.PlayCount)
            .Take(VirtualMaxEntries)
            .Select(t => new PlaylistEntry { TrackId = t.Id })
            .ToList();

        var recentEntries = Library
            .Where(t => t.LastPlayedUtc != default)
            .OrderByDescending(t => t.LastPlayedUtc)
            .Take(VirtualMaxEntries)
            .Select(t => new PlaylistEntry { TrackId = t.Id })
            .ToList();

        if (topEntries.Count > 0)
            toInsert.Add(new Playlist { Id = "virtual:top", Name = VirtualTopName, IsVirtual = true, Entries = topEntries });

        if (recentEntries.Count > 0)
            toInsert.Add(new Playlist { Id = "virtual:recent", Name = VirtualRecentName, IsVirtual = true, Entries = recentEntries });

        Playlists.InsertRange(0, toInsert);

        if (activeName == null)
        {
            ActivePlaylistIndex = -1;
            return;
        }

        int newIdx = Playlists.FindIndex(p => p.Name == activeName);
        ActivePlaylistIndex = newIdx;
        CurrentEntryIndex = entryBefore;
    }

    // ── Guards de escritura ───────────────────────────────────────────────

    private bool IsEditable(int playlistIndex) =>
        playlistIndex >= 0 &&
        playlistIndex < Playlists.Count &&
        !Playlists[playlistIndex].IsVirtual;

    // ── Resolución de tracks ──────────────────────────────────────────────

    public LibraryTrack? Resolve(PlaylistEntry entry)
    {
        var track = Library.FirstOrDefault(t => t.Id == entry.TrackId);
        if (track == null) return null;

        // En Android los paths que copiamos viven en el caché de la app
        // (FileSystem.CacheDirectory), así que normalmente siguen existiendo
        // mientras no se limpie el caché del sistema. Si el archivo
        // desapareció, no reubicamos por checksum como en desktop —eso
        // implicaba recorrer todo el disco, algo que en Android no es viable
        // por scoped storage— así que simplemente devolvemos null.
        return File.Exists(track.Path) ? track : null;
    }

    public string GetTitle(PlaylistEntry entry)
    {
        if (!string.IsNullOrEmpty(entry.CustomTitle)) return entry.CustomTitle;
        var track = Library.FirstOrDefault(t => t.Id == entry.TrackId);
        return track?.Title ?? "Desconocido";
    }

    public LibraryTrack? GetMeta(PlaylistEntry entry) =>
        Library.FirstOrDefault(t => t.Id == entry.TrackId);

    private static string ComputeChecksum(string path)
    {
        using var md5 = MD5.Create();
        using var stream = File.OpenRead(path);
        var buffer = new byte[Math.Min(8192, stream.Length)];
        stream.Read(buffer, 0, buffer.Length);
        return BitConverter.ToString(md5.ComputeHash(buffer))
            .Replace("-", "").ToLowerInvariant();
    }

    private static IEnumerable<string> EnumerateMediaFiles(string path, TrackMediaType? filter = null)
    {
        IEnumerable<string> files = Enumerable.Empty<string>();

        if (filter == null || filter == TrackMediaType.Midi)
        {
            files = files
                .Concat(Directory.EnumerateFiles(path, "*.mid", SearchOption.TopDirectoryOnly))
                .Concat(Directory.EnumerateFiles(path, "*.midi", SearchOption.TopDirectoryOnly));
        }

        if (filter == null || filter == TrackMediaType.Mp3)
        {
            files = files.Concat(Directory.EnumerateFiles(path, "*.mp3", SearchOption.TopDirectoryOnly));
        }

        return files;
    }

    // ── Biblioteca ────────────────────────────────────────────────────────

    /// <summary>
    /// Agrega un archivo (ya copiado al almacenamiento local de la app,
    /// p.ej. vía FilePicker + CopyToLocalCacheAsync) a la biblioteca.
    /// Si el archivo ya existía (mismo path o mismo checksum) reutiliza esa entrada.
    /// </summary>
    public LibraryTrack? AddToLibrary(string mediaPath)
    {
        var mediaType = ClassifyExtension(mediaPath);
        if (mediaType == null) return null;

        var existing = Library.FirstOrDefault(t => t.Path == mediaPath);
        if (existing != null) return existing;

        string checksum = ComputeChecksum(mediaPath);
        existing = Library.FirstOrDefault(t => t.Checksum == checksum && t.MediaType == mediaType);
        if (existing != null)
        {
            existing.Path = mediaPath;
            SaveLibrary();
            return existing;
        }

        var track = BuildTrack(mediaPath, mediaType.Value, checksum);
        if (track == null) return null;

        Library.Add(track);
        SaveLibrary();
        RebuildVirtualPlaylists();
        PlaylistsChanged?.Invoke();
        return track;
    }

    public bool IsFavorite(string trackId)
    {
        var favIdx = Playlists.FindIndex(p => p.Name == FavoritesPlaylistName);
        if (favIdx < 0) return false;
        return Playlists[favIdx].Entries.Any(e => e.TrackId == trackId);
    }

    public void ToggleFavorite(string trackId)
    {
        var favIdx = Playlists.FindIndex(p => p.Name == FavoritesPlaylistName);
        if (favIdx < 0)
        {
            int insertAt = Playlists.FindLastIndex(p => p.IsVirtual) + 1;
            Playlists.Insert(insertAt, new Playlist { Name = FavoritesPlaylistName });
            favIdx = insertAt;
        }

        var entries = Playlists[favIdx].Entries;
        var existing = entries.FirstOrDefault(e => e.TrackId == trackId);

        if (existing != null) entries.Remove(existing);
        else entries.Add(new PlaylistEntry { TrackId = trackId });

        SavePlaylists();
        PlaylistsChanged?.Invoke();
    }

    // ── Navegación ────────────────────────────────────────────────────────

    public void SetLooping(bool looping) => _looping = looping;

    // ── Shuffle ───────────────────────────────────────────────────────────
    // Port 1:1 del algoritmo de ControlPanel.GetShuffleNext (desktop): elige
    // una entrada al azar entre las que todavía no sonaron en este "ciclo".
    // Cuando se agotan, si el loop de playlist está activo, se resetea el
    // historial y se vuelve a mezclar (sin repetir la actual); si no, se
    // considera que la playlist terminó.
    //
    // A diferencia de desktop (donde este estado vivía en ControlPanel y
    // MainWindow tenía que sincronizarlo a mano con SetPlaylistCount), acá
    // vive directamente en PlaylistController, que ya conoce el tamaño de
    // la playlist activa — un lugar más natural para Android, sin un
    // "ControlPanel" equivalente dibujado a mano.
    public bool IsShuffling { get; private set; } = false;
    private readonly HashSet<int> _shufflePlayedIndices = new();

    /// <summary>
    /// Prende/apaga el shuffle. Al activarlo se limpia el historial y se
    /// marca la canción actual como "ya sonada", igual que en desktop.
    /// </summary>
    public void SetShuffling(bool shuffling)
    {
        IsShuffling = shuffling;
        _shufflePlayedIndices.Clear();
        if (shuffling && CurrentEntryIndex >= 0)
            _shufflePlayedIndices.Add(CurrentEntryIndex);
    }

    /// <summary>
    /// Elige y reproduce una entrada al azar de la playlist activa,
    /// excluyendo la actual y las ya reproducidas en este ciclo. Devuelve
    /// null si no hay ninguna playlist activa o si ya se reprodujeron todas
    /// y el loop de playlist está apagado (no hay "siguiente").
    /// </summary>
    public LibraryTrack? ShuffleNext()
    {
        var playlist = ActivePlaylist;
        if (playlist == null) return null;

        int count = playlist.Entries.Count;
        if (count <= 1) return ResolveAndNotify(CurrentEntryIndex >= 0 ? CurrentEntryIndex : 0);

        var candidates = new List<int>();
        for (int i = 0; i < count; i++)
            if (i != CurrentEntryIndex && !_shufflePlayedIndices.Contains(i))
                candidates.Add(i);

        if (candidates.Count == 0)
        {
            if (_looping)
            {
                _shufflePlayedIndices.Clear();
                for (int i = 0; i < count; i++)
                    if (i != CurrentEntryIndex) candidates.Add(i);
            }
            else
            {
                return null; // no hay más — playlist agotada
            }
        }

        int pick = candidates[Random.Shared.Next(candidates.Count)];
        _shufflePlayedIndices.Add(pick);

        CurrentEntryIndex = pick;
        return ResolveAndNotify(CurrentEntryIndex);
    }

    public LibraryTrack? Prev()
    {
        if (ActivePlaylist == null) return null;

        if (CurrentEntryIndex > 0)
        {
            CurrentEntryIndex--;
            return ResolveAndNotify(CurrentEntryIndex);
        }

        if (_looping)
        {
            CurrentEntryIndex = ActivePlaylist.Entries.Count - 1;
            return ResolveAndNotify(CurrentEntryIndex);
        }

        return null;
    }

    /// <summary>
    /// Sincroniza CurrentEntryIndex con el trackId que se está reproduciendo.
    /// Llamar desde MainPage al cargar cualquier canción, venga de donde venga.
    /// </summary>
    public void SyncCurrentEntry(string trackId)
    {
        if (ActivePlaylist == null) return;

        int idx = ActivePlaylist.Entries.FindIndex(e => e.TrackId == trackId);
        if (idx >= 0)
            CurrentEntryIndex = idx;
    }


    /// <summary>
    /// Restaura la playlist y la posición de una sesión anterior. Prioriza el
    /// identificador estable; usa el nombre sólo como compatibilidad con datos
    /// guardados antes de que las playlists tuvieran Id.
    /// </summary>
    public bool TryRestorePlaybackContext(
        string? playlistId,
        string? playlistName,
        string? trackId,
        int savedEntryIndex)
    {
        int playlistIndex = -1;

        if (!string.IsNullOrWhiteSpace(playlistId))
            playlistIndex = Playlists.FindIndex(p => p.Id == playlistId);

        if (playlistIndex < 0 && !string.IsNullOrWhiteSpace(playlistName))
            playlistIndex = Playlists.FindIndex(p => p.Name == playlistName);

        if (playlistIndex < 0)
            return false;

        var playlist = Playlists[playlistIndex];
        int entryIndex = -1;

        if (!string.IsNullOrWhiteSpace(trackId))
            entryIndex = playlist.Entries.FindIndex(e => e.TrackId == trackId);

        if (entryIndex < 0 && savedEntryIndex >= 0 && savedEntryIndex < playlist.Entries.Count)
            entryIndex = savedEntryIndex;

        if (entryIndex < 0)
            return false;

        ActivePlaylistIndex = playlistIndex;
        CurrentEntryIndex = entryIndex;
        _shufflePlayedIndices.Clear();
        PlaylistsChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Avanza a la siguiente canción. Si el shuffle está activo, delega en
    /// ShuffleNext() — igual que el botón Next de ControlPanel en desktop,
    /// que se vuelve shuffle-aware cuando IsShuffling es true.
    /// </summary>
    public LibraryTrack? Next()
    {
        if (IsShuffling) return ShuffleNext();

        if (ActivePlaylist == null) return null;

        if (CurrentEntryIndex < ActivePlaylist.Entries.Count - 1)
        {
            CurrentEntryIndex++;
            return ResolveAndNotify(CurrentEntryIndex);
        }

        if (_looping)
        {
            CurrentEntryIndex = 0;
            return ResolveAndNotify(CurrentEntryIndex);
        }

        return null;
    }

    /// <summary>
    /// Igual que Next(), pero pensado para el auto-avance al terminar una
    /// canción (también shuffle-aware, igual que OnTrackFinished en desktop).
    /// </summary>
    public LibraryTrack? NextAuto() => IsShuffling ? ShuffleNext() : Next();

    private LibraryTrack? ResolveAndNotify(int index)
    {
        if (ActivePlaylist == null) return null;
        if (index < 0 || index >= ActivePlaylist.Entries.Count) return null;

        var track = Resolve(ActivePlaylist.Entries[index]);
        if (track != null)
        {
            track.PlayCount++;
            track.LastPlayedUtc = DateTime.UtcNow;

            SaveLibrary();
            RebuildVirtualPlaylists();

            EntryChanged?.Invoke(track);
            PlaylistsChanged?.Invoke();
        }

        return track;
    }

    // ── Gestión de playlists ──────────────────────────────────────────────

    public Playlist CreatePlaylist(string? name = null)
    {
        var pl = new Playlist { Name = name ?? $"Playlist {Playlists.Count + 1}" };
        Playlists.Add(pl);
        SavePlaylists();
        PlaylistsChanged?.Invoke();
        return pl;
    }

    public void DeletePlaylist(int index)
    {
        if (!IsEditable(index)) return;

        Playlists.RemoveAt(index);

        if (ActivePlaylistIndex == index)
        {
            ActivePlaylistIndex = -1;
            CurrentEntryIndex = -1;
        }
        else if (ActivePlaylistIndex > index)
        {
            ActivePlaylistIndex--;
        }

        SavePlaylists();
        PlaylistsChanged?.Invoke();
    }

    public void SelectPlaylist(int index)
    {
        if (index < 0 || index >= Playlists.Count) return;

        if (index != ActivePlaylistIndex)
        {
            ActivePlaylistIndex = index;
            CurrentEntryIndex = -1;
        }

        PlaylistsChanged?.Invoke();
    }

    public void RenamePlaylist(int index, string name)
    {
        if (!IsEditable(index)) return;
        Playlists[index].Name = name;
        SavePlaylists();
        PlaylistsChanged?.Invoke();
    }

    // ── Gestión de entradas ───────────────────────────────────────────────

    public void SelectEntry(int index)
    {
        if (ActivePlaylist == null) return;
        if (index < 0 || index >= ActivePlaylist.Entries.Count) return;
        CurrentEntryIndex = index;
        PlaylistsChanged?.Invoke();
    }

    /// <summary>
    /// Agrega un archivo a una playlist real (no virtual), registrándolo
    /// primero en la biblioteca si hace falta.
    /// </summary>
    public void AddEntry(int playlistIndex, string mediaPath)
    {
        if (!IsEditable(playlistIndex)) return;

        var track = AddToLibrary(mediaPath);
        if (track == null) return;

        if (Playlists[playlistIndex].Entries.Any(e => e.TrackId == track.Id))
            return;

        Playlists[playlistIndex].Entries.Add(new PlaylistEntry { TrackId = track.Id });
        SavePlaylists();
        PlaylistsChanged?.Invoke();
    }

    /// <summary>
    /// Reemplaza en lote la pertenencia de una playlist usando ids de la
    /// biblioteca. Conserva el orden relativo de las canciones que ya estaban
    /// presentes y agrega las nuevas al final en el orden de Library.
    /// </summary>
    public void SetPlaylistMembership(int playlistIndex, IReadOnlyCollection<string> selectedTrackIds)
    {
        if (!IsEditable(playlistIndex)) return;

        var selected = new HashSet<string>(
            selectedTrackIds.Where(id => !string.IsNullOrWhiteSpace(id)),
            StringComparer.Ordinal);

        var playlist = Playlists[playlistIndex];
        var next = new List<PlaylistEntry>(selected.Count);
        var alreadyAdded = new HashSet<string>(StringComparer.Ordinal);

        // Mantener primero el orden personalizado que ya tenía la playlist.
        foreach (var entry in playlist.Entries)
        {
            if (selected.Contains(entry.TrackId) && alreadyAdded.Add(entry.TrackId))
                next.Add(entry);
        }

        // Las canciones recién marcadas se anexan siguiendo el orden estable
        // de la biblioteca, no el orden momentáneo del filtro visual.
        foreach (var track in Library)
        {
            if (selected.Contains(track.Id) && alreadyAdded.Add(track.Id))
                next.Add(new PlaylistEntry { TrackId = track.Id });
        }

        playlist.Entries = next;

        if (playlistIndex == ActivePlaylistIndex)
        {
            if (CurrentEntryIndex >= next.Count)
                CurrentEntryIndex = next.Count - 1;
        }

        SavePlaylists();
        PlaylistsChanged?.Invoke();
    }

    public void RemoveEntry(int playlistIndex, int entryIndex)
    {
        if (!IsEditable(playlistIndex)) return;

        var pl = Playlists[playlistIndex];
        if (entryIndex < 0 || entryIndex >= pl.Entries.Count) return;

        pl.Entries.RemoveAt(entryIndex);

        if (playlistIndex == ActivePlaylistIndex)
        {
            if (CurrentEntryIndex >= pl.Entries.Count)
                CurrentEntryIndex = pl.Entries.Count - 1;
        }

        SavePlaylists();
        PlaylistsChanged?.Invoke();
    }

    public void RenameEntry(int playlistIndex, int entryIndex, string title)
    {
        if (!IsEditable(playlistIndex)) return;

        var pl = Playlists[playlistIndex];
        if (entryIndex < 0 || entryIndex >= pl.Entries.Count) return;
        pl.Entries[entryIndex].CustomTitle = title;
        SavePlaylists();
        PlaylistsChanged?.Invoke();
    }

    public void MoveEntryUp(int playlistIndex, int entryIndex)
    {
        if (!IsEditable(playlistIndex)) return;

        var entries = Playlists[playlistIndex].Entries;
        if (entryIndex <= 0 || entryIndex >= entries.Count) return;

        (entries[entryIndex], entries[entryIndex - 1]) =
            (entries[entryIndex - 1], entries[entryIndex]);

        if (playlistIndex == ActivePlaylistIndex)
        {
            if (CurrentEntryIndex == entryIndex) CurrentEntryIndex--;
            else if (CurrentEntryIndex == entryIndex - 1) CurrentEntryIndex++;
        }

        SavePlaylists();
        PlaylistsChanged?.Invoke();
    }

    public void MoveEntryDown(int playlistIndex, int entryIndex)
    {
        if (!IsEditable(playlistIndex)) return;

        var entries = Playlists[playlistIndex].Entries;
        if (entryIndex < 0 || entryIndex >= entries.Count - 1) return;

        (entries[entryIndex], entries[entryIndex + 1]) =
            (entries[entryIndex + 1], entries[entryIndex]);

        if (playlistIndex == ActivePlaylistIndex)
        {
            if (CurrentEntryIndex == entryIndex) CurrentEntryIndex++;
            else if (CurrentEntryIndex == entryIndex + 1) CurrentEntryIndex--;
        }

        SavePlaylists();
        PlaylistsChanged?.Invoke();
    }

    /// <summary>
    /// Aplica de una sola vez un orden preparado por la UI. Se preserva la
    /// entrada actualmente reproducida por referencia, incluso si hay pistas
    /// duplicadas con el mismo TrackId.
    /// </summary>
    public void ReplaceEntryOrder(int playlistIndex, IReadOnlyList<PlaylistEntry> orderedEntries)
    {
        if (!IsEditable(playlistIndex)) return;

        var playlist = Playlists[playlistIndex];
        if (orderedEntries.Count != playlist.Entries.Count) return;

        // Confirmar que la propuesta contiene exactamente las mismas entradas.
        var remaining = new List<PlaylistEntry>(playlist.Entries);
        foreach (var entry in orderedEntries)
        {
            int match = remaining.FindIndex(candidate => ReferenceEquals(candidate, entry));
            if (match < 0) return;
            remaining.RemoveAt(match);
        }

        PlaylistEntry? currentEntry = null;
        if (playlistIndex == ActivePlaylistIndex &&
            CurrentEntryIndex >= 0 && CurrentEntryIndex < playlist.Entries.Count)
            currentEntry = playlist.Entries[CurrentEntryIndex];

        playlist.Entries.Clear();
        playlist.Entries.AddRange(orderedEntries);

        if (currentEntry != null)
            CurrentEntryIndex = playlist.Entries.FindIndex(entry => ReferenceEquals(entry, currentEntry));

        SavePlaylists();
        PlaylistsChanged?.Invoke();
    }

    // ── Escaneo ───────────────────────────────────────────────────────────

    /// <summary>
    /// Recorre el almacenamiento del dispositivo buscando archivos .mid/.midi/.mp3
    /// y los agrega a la biblioteca + a las playlists "Todos los MIDIs" / "Todos
    /// los MP3". Es un port 1:1 de StartScan/ScanDirectory de desktop; lo único
    /// que cambia son las carpetas raíz (almacenamiento compartido de Android en
    /// vez de MyMusic/MyDocuments/Desktop/UserProfile de Windows) y que primero
    /// hay que tener el permiso "Acceso a todos los archivos" en Android 11+
    /// (ver NeedsStorageAccess/RequestStorageAccess).
    /// </summary>
    public void StartScan()
    {
        if (IsScanning) return;
        if (NeedsStorageAccess) return;

        IsScanning = true;
        ScanFound = 0;
        _scanDirsVisited = 0;
        _scanCurrentDir = "";
        LastScanSummary = null;
        _scanStopwatch = System.Diagnostics.Stopwatch.StartNew();

        int midiIdx = Playlists.FindIndex(p => p.Name == AllMidiPlaylistName);
        if (midiIdx >= 0)
            Playlists[midiIdx].Entries.Clear();
        else
        {
            Playlists.Add(new Playlist { Name = AllMidiPlaylistName });
            midiIdx = Playlists.Count - 1;
        }

        int mp3Idx = Playlists.FindIndex(p => p.Name == AllMp3PlaylistName);
        if (mp3Idx >= 0)
            Playlists[mp3Idx].Entries.Clear();
        else
        {
            Playlists.Add(new Playlist { Name = AllMp3PlaylistName });
            mp3Idx = Playlists.Count - 1;
        }

        SavePlaylists();
        PlaylistsChanged?.Invoke();
        ScanProgressChanged?.Invoke();

        // Snapshot de la biblioteca actual: el hilo de escaneo busca
        // duplicados acá (Dictionary, O(1)) en vez de recorrer la List<>
        // real con FirstOrDefault (O(n) y, peor, mutada al mismo tiempo por
        // el hilo de UI — eso podía romperse a mitad de escaneo).
        var byPath = new Dictionary<string, LibraryTrack>();
        var byChecksum = new Dictionary<string, LibraryTrack>();
        foreach (var t in Library)
        {
            byPath[t.Path + "|" + t.MediaType] = t;
            if (!string.IsNullOrEmpty(t.Checksum))
                byChecksum.TryAdd(t.Checksum + "|" + t.MediaType, t);
        }

        lock (_scanBufferLock)
        {
            _scanBuffer.Clear();
            _lastScanFlushUtc = DateTime.MinValue;
            _lastScanHeartbeatUtc = DateTime.MinValue;
        }

        Task.Run(() =>
        {
            try
            {
                foreach (var root in GetScanRoots())
                {
                    if (!Directory.Exists(root)) continue;
                    ScanDirectory(root, byPath, byChecksum, midiIdx, mp3Idx);
                }
            }
            finally
            {
                FlushScanBuffer(midiIdx, mp3Idx, isFinal: true);

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    IsScanning = false;
                    HasScanned = true;
                    _scanStopwatch?.Stop();

                    var elapsed = _scanStopwatch?.Elapsed ?? TimeSpan.Zero;
                    LastScanSummary = ScanFound > 0
                        ? $"Escaneo completo: {ScanFound} archivo(s) encontrado(s) en {FormatElapsed(elapsed)}"
                        : $"Escaneo completo: no se encontraron archivos nuevos ({FormatElapsed(elapsed)})";

                    SaveLibrary();
                    SavePlaylists();
                    RebuildVirtualPlaylists();
                    PlaylistsChanged?.Invoke();
                    ScanProgressChanged?.Invoke();
                });
            }
        });
    }

    private static string FormatElapsed(TimeSpan t)
    {
        int totalSeconds = (int)t.TotalSeconds;
        return $"{totalSeconds / 60:D2}:{totalSeconds % 60:D2}";
    }

    /// <summary>
    /// Raíces de búsqueda. En desktop son las carpetas típicas de Windows
    /// (MyMusic, MyDocuments, Desktop, UserProfile); en Android el equivalente
    /// es la raíz del almacenamiento compartido del dispositivo, que con el
    /// permiso "Acceso a todos los archivos" concedido se puede recorrer entera.
    /// </summary>
    private static IEnumerable<string> GetScanRoots()
    {
#if ANDROID
        var external = Android.OS.Environment.ExternalStorageDirectory?.AbsolutePath;
        if (external != null)
            yield return external;
#else
        yield return Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
#endif
    }

    // Carpetas que conviene no recorrer: son enormes, irrelevantes para
    // música, y en Android 11+ el sistema bloquea el acceso a los datos
    // privados de otras apps dentro de Android/data y Android/obb incluso
    // con el permiso de "acceso a todos los archivos" concedido, así que
    // entrar ahí solo produce excepciones de acceso denegado sin encontrar
    // nada útil.
    private static readonly HashSet<string> ScanSkipDirNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "data", "obb", ".thumbnails", ".trash", "cache",
    };

    private void ScanDirectory(
        string path,
        Dictionary<string, LibraryTrack> byPath,
        Dictionary<string, LibraryTrack> byChecksum,
        int midiPlaylistIdx,
        int mp3PlaylistIdx)
    {
        _scanCurrentDir = path;
        Interlocked.Increment(ref _scanDirsVisited);
        ScanHeartbeat();

        try
        {
            var newTracks = new List<(LibraryTrack track, TrackMediaType type)>();

            foreach (var file in EnumerateMediaFiles(path))
            {
                var mediaType = ClassifyExtension(file);
                if (mediaType == null) continue;

                string pathKey = file + "|" + mediaType;
                if (byPath.TryGetValue(pathKey, out var existing))
                {
                    newTracks.Add((existing, mediaType.Value));
                    continue;
                }

                string checksum = ComputeChecksum(file);
                string checksumKey = checksum + "|" + mediaType;
                if (byChecksum.TryGetValue(checksumKey, out var byChk))
                {
                    byChk.Path = file;
                    byPath[pathKey] = byChk;
                    newTracks.Add((byChk, mediaType.Value));
                    continue;
                }

                var track = BuildTrack(file, mediaType.Value, checksum);
                if (track != null)
                {
                    byPath[pathKey] = track;
                    byChecksum[checksumKey] = track;
                    newTracks.Add((track, mediaType.Value));
                }
            }

            if (newTracks.Count > 0)
                QueueScanResults(newTracks, midiPlaylistIdx, mp3PlaylistIdx);

            foreach (var dir in Directory.EnumerateDirectories(path))
            {
                if (ScanSkipDirNames.Contains(Path.GetFileName(dir))) continue;

                try { ScanDirectory(dir, byPath, byChecksum, midiPlaylistIdx, mp3PlaylistIdx); }
                catch { }
            }
        }
        catch { }
    }

    /// <summary>
    /// Aviso liviano de "sigo vivo" para la UI (tiempo transcurrido, carpeta
    /// actual, carpetas visitadas), independiente de si se encontraron
    /// archivos. Sin esto, si el escaneo pasa mucho tiempo en carpetas sin
    /// coincidencias, la UI no se entera de que sigue avanzando y parece
    /// colgada.
    /// </summary>
    private void ScanHeartbeat()
    {
        var now = DateTime.UtcNow;
        lock (_scanBufferLock)
        {
            if (now - _lastScanHeartbeatUtc < ScanHeartbeatInterval) return;
            _lastScanHeartbeatUtc = now;
        }

        MainThread.BeginInvokeOnMainThread(() => ScanProgressChanged?.Invoke());
    }

    /// <summary>
    /// Encola resultados encontrados y, si ya pasó ScanFlushInterval desde el
    /// último volcado, los aplica a Library/Playlists en el hilo de UI. Esto
    /// reemplaza el despacho anterior (uno por carpeta), que con miles de
    /// carpetas terminaba saturando el hilo principal con reconstrucciones
    /// completas de la lista y era la causa real del "no responde".
    /// </summary>
    private void QueueScanResults(
        List<(LibraryTrack track, TrackMediaType type)> newTracks,
        int midiPlaylistIdx,
        int mp3PlaylistIdx)
    {
        bool shouldFlush;
        lock (_scanBufferLock)
        {
            _scanBuffer.AddRange(newTracks);
            shouldFlush = DateTime.UtcNow - _lastScanFlushUtc >= ScanFlushInterval;
        }

        if (shouldFlush)
            FlushScanBuffer(midiPlaylistIdx, mp3PlaylistIdx, isFinal: false);
    }

    private void FlushScanBuffer(int midiPlaylistIdx, int mp3PlaylistIdx, bool isFinal)
    {
        List<(LibraryTrack track, TrackMediaType type)> batch;
        lock (_scanBufferLock)
        {
            if (_scanBuffer.Count == 0 && !isFinal) return;
            batch = new List<(LibraryTrack, TrackMediaType)>(_scanBuffer);
            _scanBuffer.Clear();
            _lastScanFlushUtc = DateTime.UtcNow;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (midiPlaylistIdx < Playlists.Count && mp3PlaylistIdx < Playlists.Count)
            {
                foreach (var (track, type) in batch)
                {
                    if (!Library.Contains(track))
                        Library.Add(track);

                    int targetIdx = type == TrackMediaType.Midi ? midiPlaylistIdx : mp3PlaylistIdx;

                    if (!Playlists[targetIdx].Entries.Any(e => e.TrackId == track.Id))
                    {
                        Playlists[targetIdx].Entries.Add(
                            new PlaylistEntry { TrackId = track.Id });
                        ScanFound++;
                    }
                }
            }

            // Durante el escaneo solo avisamos "cambió el progreso" (barato:
            // el botón y el texto de estado). El refresco pesado de toda la
            // lista (PlaylistsChanged, que reconstruye el CollectionView)
            // recién se dispara al terminar.
            ScanProgressChanged?.Invoke();
            if (isFinal)
                PlaylistsChanged?.Invoke();
        });
    }

    // ── Metadata ──────────────────────────────────────────────────────────

    private static LibraryTrack? BuildTrack(string path, TrackMediaType mediaType, string? checksum = null)
    {
        return mediaType == TrackMediaType.Midi
            ? BuildMidiTrack(path, checksum)
            : BuildMp3Track(path, checksum);
    }

    private static LibraryTrack? BuildMidiTrack(string path, string? checksum = null)
    {
        try
        {
            var midiFile = new MidiFile(path, false);

            int bpm = 120;
            foreach (var track in midiFile.Events)
                foreach (var ev in track)
                    if (ev is TempoEvent te)
                    {
                        bpm = (int)Math.Round(
                            60_000_000.0 / te.MicrosecondsPerQuarterNote);
                        goto doneBpm;
                    }
        doneBpm:

            int tpq = midiFile.DeltaTicksPerQuarterNote;
            long maxTick = 0;
            foreach (var track in midiFile.Events)
                foreach (var ev in track)
                    if (ev.AbsoluteTime > maxTick)
                        maxTick = ev.AbsoluteTime;

            float duration = (float)(maxTick / (double)tpq * (60.0 / bpm));

            var activeChannels = new HashSet<int>();
            foreach (var track in midiFile.Events)
                foreach (var ev in track)
                    if (ev is NoteOnEvent no && no.Velocity > 0)
                        activeChannels.Add(no.Channel);

            return new LibraryTrack
            {
                Id = Guid.NewGuid().ToString(),
                Path = path,
                Title = Path.GetFileNameWithoutExtension(path),
                DurationSeconds = duration,
                TrackCount = activeChannels.Count,
                Bpm = bpm,
                Checksum = checksum ?? ComputeChecksum(path),
                MediaType = TrackMediaType.Midi
            };
        }
        catch { return null; }
    }

    private static LibraryTrack? BuildMp3Track(string path, string? checksum = null)
    {
        try
        {
            float duration = 0;
            int channels = 2;

#if ANDROID
            // NAudio.Wave.Mp3FileReader depende de Media Foundation/ACM de
            // Windows y no funciona en Android. Usamos el extractor de
            // metadata nativo de Android, que soporta MP3 sin decodificar
            // el archivo entero.
            using (var retriever = new Android.Media.MediaMetadataRetriever())
            {
                retriever.SetDataSource(path);
                var durMsStr = retriever.ExtractMetadata(Android.Media.MetadataKey.Duration);
                if (durMsStr != null && long.TryParse(durMsStr, out var durMs))
                    duration = durMs / 1000f;

                // Android no expone el conteo de canales de audio de forma
                // directa vía MediaMetadataRetriever, así que asumimos
                // estéreo (caso casi universal para MP3 de música).
            }
#endif

            return new LibraryTrack
            {
                Id = Guid.NewGuid().ToString(),
                Path = path,
                Title = Path.GetFileNameWithoutExtension(path),
                DurationSeconds = duration,
                TrackCount = channels,
                Bpm = 0,
                Checksum = checksum ?? ComputeChecksum(path),
                MediaType = TrackMediaType.Mp3
            };
        }
        catch { return null; }
    }

    // ── Persistencia ──────────────────────────────────────────────────────

    public class SaveData
    {
        public List<Playlist> Playlists { get; set; } = new();
    }

    private void SavePlaylists()
    {
        try
        {
            Directory.CreateDirectory(SaveDir);
            var data = new SaveData
            {
                Playlists = Playlists.Where(p => !p.IsVirtual).ToList(),
            };
            var json = JsonSerializer.Serialize(data,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(PlaylistsPath, json);
        }
        catch { }
    }

    private void SaveLibrary()
    {
        try
        {
            Directory.CreateDirectory(SaveDir);
            var json = JsonSerializer.Serialize(Library,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(LibraryPath, json);
        }
        catch { }
    }

    private void Load()
    {
        Directory.CreateDirectory(SaveDir);

        try
        {
            var json = File.ReadAllText(LibraryPath);
            Library = JsonSerializer.Deserialize<List<LibraryTrack>>(json) ?? new();
        }
        catch
        {
            Library = new();
            FirstRunInitializer.Quarantine(LibraryPath);
            FirstRunInitializer.AtomicWriteAllText(LibraryPath, "[]");
        }

        try
        {
            var json = File.ReadAllText(PlaylistsPath);
            var data = JsonSerializer.Deserialize<SaveData>(json);
            Playlists = data?.Playlists ?? new();
        }
        catch
        {
            Playlists = new();
            FirstRunInitializer.Quarantine(PlaylistsPath);
            FirstRunInitializer.AtomicWriteAllText(PlaylistsPath, "{\n  \"Playlists\": []\n}");
        }

        // Datos viejos o JSON editado a mano pueden contener nulls. Nunca
        // deben alcanzar la construcción de LibraryPage/AppShell.
        Library.RemoveAll(t => t == null);
        Playlists.RemoveAll(p => p == null);
        bool assignedMissingPlaylistIds = false;
        foreach (var playlist in Playlists)
        {
            playlist.Entries ??= new();
            if (string.IsNullOrWhiteSpace(playlist.Id))
            {
                playlist.Id = Guid.NewGuid().ToString();
                assignedMissingPlaylistIds = true;
            }
        }

        if (assignedMissingPlaylistIds)
            SavePlaylists();

        RebuildVirtualPlaylists();
    }
}
