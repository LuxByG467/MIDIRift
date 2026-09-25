using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Dispatching;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MIDIRift;

/// <summary>Fila de la lista de playlists (pestaña "Playlists"). Ver LibraryPage.xaml.</summary>
public class PlaylistListRow
{
    /// <summary>Índice real dentro de PlaylistController.Playlists (no el índice filtrado en pantalla).</summary>
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public string Subtitle { get; set; } = "";
}

/// <summary>
/// Pantalla "Biblioteca" — nueva navegación inferior estilo Spotify.
///
/// Reutiliza PlaylistController tal cual (compartido con MainPage vía
/// AppShell, ver PlaybackBridge.cs) y el patrón de fila/plantilla que ya
/// existía en PlaylistPage.xaml.cs (PlaylistEntryRow, FormatSubtitle) para
/// no duplicar esa lógica.
///
/// Las tres "bibliotecas" (MP3 / MIDI / Toda la música) no son datos
/// distintos — son un filtro sobre las mismas playlists y la misma
/// Library. La pestaña "Toda la música" en particular no lista una
/// colección propia: apunta a una de las tres playlists virtuales que ya
/// mantiene PlaylistController (VirtualMidiLibraryName / VirtualMp3LibraryName /
/// VirtualAllLibraryName), reutilizando el mismo camino de reproducción
/// (SelectPlaylist/SelectEntry/Resolve) que una playlist real — así
/// Anterior/Siguiente en "Reproduciendo ahora" también funciona sobre ellas.
/// </summary>
public partial class LibraryPage : ContentPage
{
    private enum LibraryFilter { All, Midi, Mp3 }
    private enum SubTab { Playlists, AllMusic }

    private readonly PlaylistController _controller;
    private readonly PlaybackBridge _bridge;

    private LibraryFilter _filter = LibraryFilter.All;
    private SubTab _subTab = SubTab.Playlists;

    /// <summary>
    /// Índice (en PlaylistController.Playlists) de la playlist que muestra
    /// TrackEntriesView ahora mismo — sea una playlist real abierta desde
    /// la lista, o una de las virtuales de "Toda la música". Null = se está
    /// mostrando la lista de playlists, no un detalle.
    /// </summary>
    private int? _openPlaylistIndex;

    private string _searchText = "";

    private IDispatcherTimer? _miniPlayerTimer;
    private int _miniPlayerRotationIndex;

    // Último CurrentTrackId visto en el bridge. UpdateProgress en MainPage
    // llama a NotifyChanged() ~1 vez por segundo mientras algo suena (para
    // que el mini reproductor actualice el tiempo transcurrido), así que
    // OnBridgeStateChanged se dispara constantemente aunque la canción
    // activa no haya cambiado. Reasignar ItemsSource en cada una de esas
    // notificaciones era lo que reseteaba el scroll de TrackEntriesView al
    // principio de la lista cada vez que avanzaba un chunk del tracker:
    // ahora solo se reconstruye la lista (para mover el ícono "▶") cuando
    // este valor efectivamente cambia.
    private string? _lastKnownPlayingTrackId;
    private CancellationTokenSource? _hideScanStatusCts;

    // El reordenamiento es transaccional: los drops modifican esta copia y
    // solo Confirmar la vuelca al PlaylistController. Cancelar la descarta.
    private bool _isReorderMode;
    private int? _reorderPlaylistIndex;
    private List<PlaylistEntry>? _pendingEntryOrder;
    private int _dragSourceIndex = -1;

    public LibraryPage(PlaylistController controller, PlaybackBridge bridge)
    {
        InitializeComponent();
        _controller = controller;
        _bridge = bridge;

        // La página vive toda la vida de la app como pestaña persistente del
        // Shell (ver AppShell.xaml.cs), así que nos suscribimos una sola vez
        // acá y no nos desuscribimos en OnDisappearing como hacía la vieja
        // PlaylistPage modal — ambas fuentes (_controller, _bridge) también
        // viven toda la vida de la app, no hay nada que limpiar.
        _controller.PlaylistsChanged += OnControllerChanged;
        _controller.ScanProgressChanged += OnScanProgressChanged;
        _bridge.StateChanged += OnBridgeStateChanged;

        RefreshContent();
        RefreshScanStatus();
        RefreshMiniPlayer();

        // El timer del mini reproductor arranca en OnAppearing (no acá):
        // esta página es una pestaña de Shell que puede construirse en un
        // momento temprano/frágil del arranque de la app (ver comentario en
        // AppShell.xaml.cs), y de cualquier forma no tiene sentido rotar el
        // texto del mini reproductor mientras la Biblioteca ni siquiera se
        // está mostrando — mismo criterio que ya usa MainPage con sus
        // paneles de visualización al pasar a segundo plano.
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        RefreshMiniPlayer();

        if (_miniPlayerTimer == null)
            StartMiniPlayerTimer();
        else
            _miniPlayerTimer.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _miniPlayerTimer?.Stop();
    }

    protected override bool OnBackButtonPressed()
    {
        // Si estamos con una playlist real abierta, el botón físico "atrás"
        // vuelve a la lista en vez de salir de la app — igual sensación que
        // el PopModalAsync que hacía esto antes.
        if (_subTab == SubTab.Playlists && _openPlaylistIndex != null)
        {
            _openPlaylistIndex = null;
            RefreshContent();
            return true;
        }

        return base.OnBackButtonPressed();
    }

    // ── Filtro de biblioteca (MP3 / MIDI / Toda la música) ─────────────────

    private async void OnLibraryNameClicked(object sender, EventArgs e)
    {
        string choice = await DisplayActionSheet("Biblioteca", "Cancelar", null, "Toda la música", "MIDI", "MP3");

        LibraryFilter? newFilter = choice switch
        {
            "Toda la música" => LibraryFilter.All,
            "MIDI" => LibraryFilter.Midi,
            "MP3" => LibraryFilter.Mp3,
            _ => null,
        };
        if (newFilter == null || newFilter == _filter) return;

        _filter = newFilter.Value;

        // Si estábamos viendo "Toda la música", hay que re-apuntar a la
        // playlist virtual del nuevo formato. Si estábamos con una playlist
        // real abierta, la dejamos como está: el filtro no cambia su
        // contenido, solo qué playlists aparecen listadas.
        if (_subTab == SubTab.AllMusic)
            _openPlaylistIndex = FindVirtualPlaylistIndex(_filter);

        RefreshContent();
    }

    private static string FilterLabel(LibraryFilter filter) => filter switch
    {
        LibraryFilter.Midi => "MIDI",
        LibraryFilter.Mp3 => "MP3",
        _ => "Toda la música",
    };

    private int? FindVirtualPlaylistIndex(LibraryFilter filter)
    {
        string name = filter switch
        {
            LibraryFilter.Midi => PlaylistController.VirtualMidiLibraryName,
            LibraryFilter.Mp3 => PlaylistController.VirtualMp3LibraryName,
            _ => PlaylistController.VirtualAllLibraryName,
        };
        int idx = _controller.Playlists.FindIndex(p => p.Name == name);
        return idx >= 0 ? idx : (int?)null;
    }

    // ── Segunda barra: Playlists / Toda la música / Escanear ───────────────

    private void OnPlaylistsTabClicked(object sender, EventArgs e)
    {
        if (_isReorderMode) EndReorderMode(refresh: false);
        _subTab = SubTab.Playlists;
        _openPlaylistIndex = null;
        RefreshContent();
    }

    private void OnAllMusicTabClicked(object sender, EventArgs e)
    {
        if (_isReorderMode) EndReorderMode(refresh: false);
        _subTab = SubTab.AllMusic;
        _openPlaylistIndex = FindVirtualPlaylistIndex(_filter);
        RefreshContent();
    }

    private async void OnScanClicked(object sender, EventArgs e)
    {
        if (_controller.IsScanning) return;

        if (_controller.NeedsStorageAccess)
        {
            bool goToSettings = await DisplayAlert(
                "Permiso necesario",
                "Para buscar archivos MIDI y MP3 en todo el dispositivo, MIDIRift necesita el permiso \"Acceso a todos los archivos\". Se te va a abrir la pantalla de Ajustes para activarlo.",
                "Ir a Ajustes", "Cancelar");

            if (goToSettings)
                _controller.RequestStorageAccess();

            return;
        }

        _controller.StartScan();
        RefreshScanStatus();
    }

    private void OnScanProgressChanged() =>
        MainThread.BeginInvokeOnMainThread(RefreshScanStatus);

    private void RefreshScanStatus()
    {
        if (_controller.IsScanning)
        {
            _hideScanStatusCts?.Cancel();

            ScanButton.Text = "…";
            ScanButton.IsEnabled = false;

            ScanStatusPanel.IsVisible = true;
            ScanActivityIndicator.IsRunning = true;

            var elapsed = _controller.ScanElapsed;
            int totalSeconds = (int)elapsed.TotalSeconds;
            string time = $"{totalSeconds / 60:D2}:{totalSeconds % 60:D2}";

            string folder = "";
            var currentDir = _controller.ScanCurrentFolder;
            if (!string.IsNullOrEmpty(currentDir))
            {
                folder = Path.GetFileName(currentDir.TrimEnd('/'));
                if (string.IsNullOrEmpty(folder)) folder = currentDir;
            }

            ScanStatusLabel.Text =
                $"Escaneando… {time} · {_controller.ScanFound} encontrados · " +
                $"{_controller.ScanDirsVisited} carpetas revisadas" +
                (string.IsNullOrEmpty(folder) ? "" : $" · {folder}");
        }
        else
        {
            ScanButton.Text = _controller.HasScanned ? "↺ Escanear" : "⌕ Escanear";
            ScanButton.IsEnabled = true;
            ScanActivityIndicator.IsRunning = false;

            if (_controller.LastScanSummary != null)
            {
                ScanStatusPanel.IsVisible = true;
                ScanStatusLabel.Text = _controller.LastScanSummary;
                ScheduleHideScanStatus();
            }
            else
            {
                ScanStatusPanel.IsVisible = false;
            }
        }
    }

    private void ScheduleHideScanStatus()
    {
        _hideScanStatusCts?.Cancel();
        var cts = new CancellationTokenSource();
        _hideScanStatusCts = cts;

        Task.Run(async () =>
        {
            try { await Task.Delay(4000, cts.Token); }
            catch (TaskCanceledException) { return; }

            if (cts.Token.IsCancellationRequested) return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (!_controller.IsScanning)
                    ScanStatusPanel.IsVisible = false;
            });
        });
    }

    // ── Buscar ───────────────────────────────────────────────────────────

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = e.NewTextValue ?? "";
        RefreshContent();
    }

    // ── Crear / renombrar / eliminar playlist ───────────────────────────────

    private async void OnCreatePlaylistClicked(object sender, EventArgs e)
    {
        string? name = await DisplayPromptAsync("Nueva playlist", "Nombre:", "Crear", "Cancelar");
        if (name == null) return;

        var playlist = _controller.CreatePlaylist(string.IsNullOrWhiteSpace(name) ? null : name);
        int idx = _controller.Playlists.IndexOf(playlist);
        if (idx < 0) return;

        _controller.SelectPlaylist(idx);

        // Abrimos la playlist recién creada directo, para poder agregarle
        // canciones enseguida con el botón 📂 del header de detalle.
        _subTab = SubTab.Playlists;
        _openPlaylistIndex = idx;
        RefreshContent();
    }

    private async void OnPlaylistMenuClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn || btn.CommandParameter is not PlaylistListRow row) return;

        string choice = await DisplayActionSheet(row.Name, "Cancelar", null, "Renombrar", "Eliminar");
        switch (choice)
        {
            case "Renombrar":
                string? newName = await DisplayPromptAsync("Renombrar playlist", "Nombre:", "Guardar", "Cancelar", initialValue: row.Name);
                if (!string.IsNullOrWhiteSpace(newName))
                    _controller.RenamePlaylist(row.Index, newName);
                break;

            case "Eliminar":
                bool confirm = await DisplayAlert("Borrar playlist", $"¿Borrar \"{row.Name}\"? Esto no borra los archivos, solo la lista.", "Borrar", "Cancelar");
                if (confirm)
                {
                    _controller.DeletePlaylist(row.Index);
                    if (_openPlaylistIndex == row.Index) _openPlaylistIndex = null;
                }
                break;
        }
    }

    // ── Abrir / cerrar detalle de una playlist ──────────────────────────────

    private void OnPlaylistRowTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is not PlaylistListRow row) return;
        _controller.SelectPlaylist(row.Index);
        _openPlaylistIndex = row.Index;
        RefreshContent();
    }

    private void OnBackFromDetailClicked(object sender, EventArgs e)
    {
        if (_isReorderMode) EndReorderMode(refresh: false);
        _openPlaylistIndex = null;
        RefreshContent();
    }

    private async void OnAddFileClicked(object sender, EventArgs e)
    {
        if (_openPlaylistIndex is not int idx || idx < 0 ||
            idx >= _controller.Playlists.Count || _controller.Playlists[idx].IsVirtual)
        {
            await DisplayAlert("Elegí una playlist",
                "Para administrar canciones, primero elegí o creá una playlist editable.", "OK");
            return;
        }

        await Navigation.PushModalAsync(new PlaylistTrackPickerPage(_controller, idx));
    }

    // ── Reproducir / favorito / quitar una canción ──────────────────────────

    private void OnTrackEntryTapped(object sender, TappedEventArgs e)
    {
        if (_isReorderMode) return;
        if (e.Parameter is not PlaylistEntryRow row) return;
        if (_openPlaylistIndex is not int idx || idx < 0 || idx >= _controller.Playlists.Count) return;

        var playlist = _controller.Playlists[idx];
        if (row.EntryIndex < 0 || row.EntryIndex >= playlist.Entries.Count) return;

        _controller.SelectPlaylist(idx);
        _controller.SelectEntry(row.EntryIndex);

        var track = _controller.Resolve(playlist.Entries[row.EntryIndex]);
        if (track == null)
        {
            DisplayAlert("Archivo no disponible", "No se pudo encontrar este archivo. Puede que se haya movido o borrado.", "OK");
            return;
        }

        _bridge.RequestPlay(track.Path);
        _ = Shell.Current.GoToAsync("//nowplaying");
    }

    private void OnFavoriteClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn || btn.CommandParameter is not PlaylistEntryRow row) return;
        _controller.ToggleFavorite(row.TrackId);
    }

    private void OnRemoveClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn || btn.CommandParameter is not PlaylistEntryRow row) return;
        if (!row.CanRemove) return;
        if (_openPlaylistIndex is not int idx) return;

        _controller.RemoveEntry(idx, row.EntryIndex);
    }

    private void OnTrackDragStarting(object sender, DragStartingEventArgs e)
    {
        if (sender is not DragGestureRecognizer drag ||
            drag.BindingContext is not PlaylistEntryRow row ||
            _openPlaylistIndex is not int playlistIndex ||
            playlistIndex < 0 || playlistIndex >= _controller.Playlists.Count ||
            _controller.Playlists[playlistIndex].IsVirtual)
        {
            e.Cancel = true;
            return;
        }

        // Una lista filtrada no tiene posiciones contiguas fiables para ordenar.
        // Al iniciar el gesto mostramos la playlist completa.
        if (!_isReorderMode)
        {
            _searchText = "";
            SearchPlaylistBar.Text = "";
            BeginReorderMode(playlistIndex);
        }

        if (_reorderPlaylistIndex != playlistIndex || _pendingEntryOrder == null)
        {
            e.Cancel = true;
            return;
        }

        _dragSourceIndex = row.EntryIndex;
        e.Data.Properties["MIDIRiftPlaylistEntryIndex"] = row.EntryIndex;

        try
        {
            HapticFeedback.Default.Perform(HapticFeedbackType.LongPress);
        }
        catch
        {
            // Algunos dispositivos o configuraciones bloquean la vibración.
        }
    }

    private void OnTrackDropped(object sender, DropEventArgs e)
    {
        if (!_isReorderMode || _pendingEntryOrder == null ||
            sender is not DropGestureRecognizer drop ||
            drop.BindingContext is not PlaylistEntryRow targetRow)
            return;

        int sourceIndex = _dragSourceIndex;
        if (e.Data.Properties.TryGetValue("MIDIRiftPlaylistEntryIndex", out object? rawIndex) &&
            rawIndex is int transferredIndex)
            sourceIndex = transferredIndex;

        int targetIndex = targetRow.EntryIndex;
        if (sourceIndex < 0 || sourceIndex >= _pendingEntryOrder.Count ||
            targetIndex < 0 || targetIndex >= _pendingEntryOrder.Count ||
            sourceIndex == targetIndex)
            return;

        PlaylistEntry moved = _pendingEntryOrder[sourceIndex];
        _pendingEntryOrder.RemoveAt(sourceIndex);

        // Tras quitar el origen, los índices posteriores se desplazan uno.
        if (sourceIndex < targetIndex) targetIndex--;
        targetIndex = Math.Clamp(targetIndex, 0, _pendingEntryOrder.Count);
        _pendingEntryOrder.Insert(targetIndex, moved);

        _dragSourceIndex = targetIndex;
        RefreshTrackEntries();
        TrackEntriesView.ScrollTo(targetIndex, position: ScrollToPosition.Center, animate: false);
    }

    private void OnTrackDropCompleted(object sender, DropCompletedEventArgs e)
    {
        _dragSourceIndex = -1;
    }

    private void BeginReorderMode(int playlistIndex)
    {
        var playlist = _controller.Playlists[playlistIndex];
        if (playlist.IsVirtual) return;

        _isReorderMode = true;
        _reorderPlaylistIndex = playlistIndex;
        _pendingEntryOrder = new List<PlaylistEntry>(playlist.Entries);
        _dragSourceIndex = -1;

        NormalLibraryActions.IsVisible = false;
        ReorderActions.IsVisible = true;
        LibraryNameButton.IsEnabled = false;
        SearchPlaylistBar.IsEnabled = false;
        DetailPlaylistNameLabel.Text = $"{playlist.Name} · arrastrá para ordenar";
        RefreshTrackEntries();
    }

    private void OnConfirmReorderClicked(object sender, EventArgs e)
    {
        if (_reorderPlaylistIndex is int playlistIndex && _pendingEntryOrder != null)
            _controller.ReplaceEntryOrder(playlistIndex, _pendingEntryOrder);

        EndReorderMode(refresh: true);
    }

    private void OnCancelReorderClicked(object sender, EventArgs e) =>
        EndReorderMode(refresh: true);

    private void EndReorderMode(bool refresh)
    {
        _isReorderMode = false;
        _reorderPlaylistIndex = null;
        _pendingEntryOrder = null;
        _dragSourceIndex = -1;

        NormalLibraryActions.IsVisible = true;
        ReorderActions.IsVisible = false;
        LibraryNameButton.IsEnabled = true;
        SearchPlaylistBar.IsEnabled = true;

        if (refresh) RefreshContent();
    }

    // ── Mini reproductor flotante ────────────────────────────────────────

    private void StartMiniPlayerTimer()
    {
        _miniPlayerTimer = Dispatcher.CreateTimer();
        _miniPlayerTimer.Interval = TimeSpan.FromSeconds(2.2);
        _miniPlayerTimer.Tick += (_, _) =>
        {
            _miniPlayerRotationIndex++;
            RefreshMiniPlayer();
        };
        _miniPlayerTimer.Start();
    }

    private void OnBridgeStateChanged()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            RefreshMiniPlayer();

            // El ícono "▶" de sonando-ahora en las filas depende de
            // _bridge.CurrentTrackId — pero ese id cambia solo cuando
            // arranca una canción distinta, no en cada tick de progreso.
            // Comparar antes de refrescar evita reasignar ItemsSource (y
            // así resetear el scroll) en cada chunk del tracker.
            if (_bridge.CurrentTrackId == _lastKnownPlayingTrackId) return;
            _lastKnownPlayingTrackId = _bridge.CurrentTrackId;

            if (TrackEntriesView.IsVisible) RefreshTrackEntries();
        });
    }

    private void OnMiniPlayerTapped(object sender, TappedEventArgs e) =>
        _ = Shell.Current.GoToAsync("//nowplaying");

    private void RefreshMiniPlayer()
    {
        MiniPlayerCard.IsVisible = _bridge.HasTrack;
        if (!_bridge.HasTrack) return;

        MiniPlayerTitle.Text = string.IsNullOrEmpty(_bridge.Title) ? "Reproduciendo" : _bridge.Title;

        bool isMidi = _bridge.Format == "MIDI";
        MiniPlayerCover.Text = isMidi ? "🎵" : "🎧";

        var lines = new List<string>();
        if (!string.IsNullOrEmpty(_bridge.Engine)) lines.Add(_bridge.Engine);
        lines.Add($"{FormatTime(_bridge.ElapsedSeconds)} / {FormatTime(_bridge.TotalSeconds)}");
        if (_bridge.Kbps is int kbps) lines.Add($"{kbps} kbps");
        if (_bridge.SampleRate is int sr) lines.Add($"{sr} Hz");
        if (!string.IsNullOrEmpty(_bridge.Format)) lines.Add(_bridge.Format);

        if (lines.Count == 0)
        {
            MiniPlayerSubtitle.Text = "";
            return;
        }

        int idx = ((_miniPlayerRotationIndex % lines.Count) + lines.Count) % lines.Count;
        MiniPlayerSubtitle.Text = lines[idx];
    }

    private static string FormatTime(float seconds)
    {
        if (seconds < 0) seconds = 0;
        int s = (int)seconds;
        return $"{s / 60:D2}:{s % 60:D2}";
    }

    // ── Refresco general ─────────────────────────────────────────────────

    private void OnControllerChanged()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // La playlist que teníamos abierta pudo haber sido borrada
            // (o el índice corrido por un borrado de otra) — si ya no es
            // válida, volvemos a la lista en vez de mostrar basura.
            if (_openPlaylistIndex is int idx && (idx < 0 || idx >= _controller.Playlists.Count))
                _openPlaylistIndex = _subTab == SubTab.AllMusic ? FindVirtualPlaylistIndex(_filter) : null;

            RefreshContent();
        });
    }

    private void RefreshContent()
    {
        bool showList = _subTab == SubTab.Playlists && _openPlaylistIndex == null;

        PlaylistsListView.IsVisible = showList;
        TrackEntriesView.IsVisible = !showList;
        DetailHeaderGrid.IsVisible = _subTab == SubTab.Playlists && _openPlaylistIndex != null;

        if (showList) RefreshPlaylistsList();
        else RefreshTrackEntries();

        LibraryNameButton.Text = FilterLabel(_filter) + "  ▾";
        SearchPlaylistBar.Placeholder = showList ? "Buscar playlist" : "Buscar canción";

        bool onPlaylistsTab = _subTab == SubTab.Playlists;
        ApplyTabTheme(PlaylistsTabButton, onPlaylistsTab);
        ApplyTabTheme(AllMusicTabButton, !onPlaylistsTab);
    }

    private void RefreshPlaylistsList()
    {
        var rows = _controller.Playlists
            .Select((p, i) => (playlist: p, index: i))
            .Where(t => !t.playlist.IsVirtual)
            .Where(t => IsCompatibleWithFilter(t.playlist))
            .Where(t => string.IsNullOrWhiteSpace(_searchText) ||
                        t.playlist.Name.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0)
            .Select(t => new PlaylistListRow
            {
                Index = t.index,
                Name = t.playlist.Name,
                Subtitle = $"{t.playlist.Entries.Count} canción{(t.playlist.Entries.Count == 1 ? "" : "es")}",
            })
            .ToList();

        PlaylistsListView.ItemsSource = rows;
    }

    private bool IsCompatibleWithFilter(Playlist playlist)
    {
        if (_filter == LibraryFilter.All) return true;
        if (playlist.Entries.Count == 0) return true; // vacía: visible en cualquier filtro hasta que tenga contenido

        var wanted = _filter == LibraryFilter.Midi ? TrackMediaType.Midi : TrackMediaType.Mp3;
        return playlist.Entries.Any(entry => _controller.GetMeta(entry)?.MediaType == wanted);
    }

    private void RefreshTrackEntries()
    {
        if (_openPlaylistIndex is not int idx || idx < 0 || idx >= _controller.Playlists.Count)
        {
            TrackEntriesView.ItemsSource = null;
            DetailPlaylistNameLabel.Text = "";
            return;
        }

        var playlist = _controller.Playlists[idx];
        bool editable = !playlist.IsVirtual;
        bool showingPendingOrder = _isReorderMode && _reorderPlaylistIndex == idx && _pendingEntryOrder != null;
        IReadOnlyList<PlaylistEntry> entries = showingPendingOrder ? _pendingEntryOrder! : playlist.Entries;
        DetailPlaylistNameLabel.Text = showingPendingOrder
            ? $"{playlist.Name} · arrastrá para ordenar"
            : playlist.Name;

        var rows = new List<PlaylistEntryRow>();
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            string title = _controller.GetTitle(entry);

            if (!string.IsNullOrWhiteSpace(_searchText) &&
                title.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            var meta = _controller.GetMeta(entry);
            string subtitle = meta == null ? "Archivo no encontrado" : PlaylistPage.FormatSubtitle(meta);

            rows.Add(new PlaylistEntryRow
            {
                EntryIndex = i,
                TrackId = entry.TrackId,
                Title = title,
                Subtitle = subtitle,
                IsPlaying = entry.TrackId == _bridge.CurrentTrackId,
                IsFavorite = _controller.IsFavorite(entry.TrackId),
                CanRemove = editable,
                CanMoveUp = false,
                CanMoveDown = false,
                IsReorderMode = showingPendingOrder,
            });
        }

        TrackEntriesView.ItemsSource = rows;
    }

    private static void ApplyTabTheme(Button button, bool active)
    {
        button.SetDynamicResource(Button.BackgroundColorProperty, active ? "PurplePrimary" : "ControlBackground");
        if (active)
        {
            button.RemoveDynamicResource(Button.TextColorProperty);
            button.TextColor = Colors.White;
        }
        else
        {
            button.SetDynamicResource(Button.TextColorProperty, "TextSecondary");
        }
    }

}