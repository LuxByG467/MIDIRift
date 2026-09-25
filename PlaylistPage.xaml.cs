using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MIDIRift;

/// <summary>
/// Fila mostrada en el CollectionView de PlaylistPage. Es el equivalente
/// simplificado de las filas que dibuja a mano PlaylistPanel.cs en desktop
/// (título, subtítulo, favorito, si está sonando ahora, si se puede borrar).
/// </summary>
public class PlaylistEntryRow
{
    public int EntryIndex { get; set; }
    public string TrackId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public bool IsPlaying { get; set; }
    public bool IsFavorite { get; set; }
    public bool CanRemove { get; set; }
    public bool CanMoveUp { get; set; }
    public bool CanMoveDown { get; set; }
    public bool IsReorderMode { get; set; }

    public string IsPlayingIcon => IsPlaying ? "▶" : "";
    public bool ShowPlayingIcon => !IsReorderMode;
    public bool ShowNormalActions => !IsReorderMode;
    public bool ShowRemoveAction => CanRemove && !IsReorderMode;
    public double FavoriteOpacity => IsFavorite ? 1.0 : 0.35;
}

/// <summary>
/// Puerto de PlaylistPanel.cs (desktop) a una página nativa de MAUI.
///
/// El original en desktop es un Control dibujado a mano en Avalonia con
/// animaciones de slide y filas de dos líneas. Portar ese renderer pixel a
/// pixel no tiene sentido en MAUI (no es la forma idiomática de construir UI
/// aquí); en su lugar se usa un CollectionView nativo con la misma
/// información y las mismas operaciones que expone PlaylistController:
/// elegir playlist, reproducir una entrada, marcar favorito, quitar,
/// crear/borrar playlist y agregar archivos.
/// </summary>
public partial class PlaylistPage : ContentPage
{
    private readonly PlaylistController _controller;
    private readonly string? _currentTrackId;
    private bool _suppressPickerEvent = false;

    /// <summary>Se dispara cuando el usuario toca una entrada para reproducirla.</summary>
    public event Action<LibraryTrack>? EntrySelected;

    public PlaylistPage(PlaylistController controller, string? currentTrackId)
    {
        InitializeComponent();
        _controller = controller;
        _currentTrackId = currentTrackId;

        _controller.PlaylistsChanged += OnControllerChanged;
        _controller.ScanProgressChanged += OnScanProgressChanged;

        RefreshPicker();
        RefreshEntries();
        RefreshScanButton();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _controller.PlaylistsChanged -= OnControllerChanged;
        _controller.ScanProgressChanged -= OnScanProgressChanged;
    }

    private void OnScanProgressChanged()
    {
        MainThread.BeginInvokeOnMainThread(RefreshScanButton);
    }

    private void OnControllerChanged()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            RefreshPicker();
            RefreshEntries();
            RefreshScanButton();
        });
    }

    // ── Selector de playlist ─────────────────────────────────────────────

    private void RefreshPicker()
    {
        _suppressPickerEvent = true;

        PlaylistPicker.ItemsSource = _controller.Playlists
            .Select(p => p.IsVirtual ? p.Name : p.Name)
            .ToList();

        if (_controller.ActivePlaylistIndex >= 0 &&
            _controller.ActivePlaylistIndex < _controller.Playlists.Count)
        {
            PlaylistPicker.SelectedIndex = _controller.ActivePlaylistIndex;
        }
        else if (_controller.Playlists.Count > 0)
        {
            _controller.SelectPlaylist(0);
            PlaylistPicker.SelectedIndex = 0;
        }

        _suppressPickerEvent = false;
    }

    private void OnPlaylistPickerChanged(object sender, EventArgs e)
    {
        if (_suppressPickerEvent) return;
        int idx = PlaylistPicker.SelectedIndex;
        if (idx < 0) return;

        _controller.SelectPlaylist(idx);
        RefreshEntries();
    }

    // ── Entradas ──────────────────────────────────────────────────────────

    private void RefreshEntries()
    {
        var playlist = _controller.ActivePlaylist;
        if (playlist == null)
        {
            PlaylistInfoLabel.Text = "";
            EntriesView.ItemsSource = null;
            return;
        }

        bool editable = !playlist.IsVirtual;
        PlaylistInfoLabel.Text = editable
            ? $"{playlist.Entries.Count} canciones"
            : $"{playlist.Entries.Count} canciones · playlist automática (solo lectura)";

        var rows = new List<PlaylistEntryRow>();
        for (int i = 0; i < playlist.Entries.Count; i++)
        {
            var entry = playlist.Entries[i];
            var meta = _controller.GetMeta(entry);
            string subtitle = meta == null
                ? "Archivo no encontrado"
                : FormatSubtitle(meta);

            rows.Add(new PlaylistEntryRow
            {
                EntryIndex = i,
                TrackId = entry.TrackId,
                Title = _controller.GetTitle(entry),
                Subtitle = subtitle,
                IsPlaying = entry.TrackId == _currentTrackId,
                IsFavorite = _controller.IsFavorite(entry.TrackId),
                CanRemove = editable,
            });
        }

        EntriesView.ItemsSource = rows;
    }

    // internal (no private): LibraryPage reutiliza este formateo tal cual
    // para sus propias filas de canciones, en vez de duplicarlo.
    internal static string FormatSubtitle(LibraryTrack meta)
    {
        int s = (int)meta.DurationSeconds;
        string time = $"{s / 60:D2}:{s % 60:D2}";
        string kind = meta.MediaType == TrackMediaType.Midi ? "MIDI" : "MP3";
        return meta.MediaType == TrackMediaType.Midi
            ? $"{kind} · {time} · {meta.Bpm} BPM"
            : $"{kind} · {time}";
    }

    private void OnEntryTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is not PlaylistEntryRow row) return;

        var playlist = _controller.ActivePlaylist;
        if (playlist == null) return;
        if (row.EntryIndex < 0 || row.EntryIndex >= playlist.Entries.Count) return;

        _controller.SelectEntry(row.EntryIndex);

        var track = _controller.Resolve(playlist.Entries[row.EntryIndex]);
        if (track == null)
        {
            DisplayAlert("Archivo no disponible",
                "No se pudo encontrar este archivo. Puede que se haya movido o borrado.",
                "OK");
            return;
        }

        EntrySelected?.Invoke(track);
    }

    private void OnFavoriteClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn || btn.CommandParameter is not PlaylistEntryRow row) return;
        _controller.ToggleFavorite(row.TrackId);
    }

    private void OnRemoveClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn || btn.CommandParameter is not PlaylistEntryRow row) return;
        if (_controller.ActivePlaylistIndex < 0) return;
        _controller.RemoveEntry(_controller.ActivePlaylistIndex, row.EntryIndex);
    }

    // ── Escaneo ───────────────────────────────────────────────────────────

    private CancellationTokenSource? _hideScanStatusCts;

    private void RefreshScanButton()
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
            ScanButton.Text = _controller.HasScanned ? "↺" : "⌕";
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

    /// <summary>Oculta el aviso de "escaneo completo" solo, unos segundos después de mostrarlo.</summary>
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
        RefreshScanButton();
    }

    // ── Gestión de playlists ─────────────────────────────────────────────

    private async void OnCreatePlaylistClicked(object sender, EventArgs e)
    {
        string? name = await DisplayPromptAsync("Nueva playlist", "Nombre:", "Crear", "Cancelar");
        if (name == null) return; // canceló

        var pl = _controller.CreatePlaylist(string.IsNullOrWhiteSpace(name) ? null : name);
        int idx = _controller.Playlists.IndexOf(pl);
        if (idx >= 0) _controller.SelectPlaylist(idx);
    }

    private async void OnDeletePlaylistClicked(object sender, EventArgs e)
    {
        int idx = _controller.ActivePlaylistIndex;
        if (idx < 0 || idx >= _controller.Playlists.Count) return;

        var playlist = _controller.Playlists[idx];
        if (playlist.IsVirtual)
        {
            await DisplayAlert("No se puede borrar",
                "Las playlists automáticas (Biblioteca, Favoritos, Recientes, etc.) no se pueden eliminar.",
                "OK");
            return;
        }

        bool confirm = await DisplayAlert("Borrar playlist",
            $"¿Borrar \"{playlist.Name}\"? Esto no borra los archivos, solo la lista.",
            "Borrar", "Cancelar");
        if (!confirm) return;

        _controller.DeletePlaylist(idx);
    }

    // ── Agregar archivo ───────────────────────────────────────────────────

    private async void OnAddFileClicked(object sender, EventArgs e)
    {
        int idx = _controller.ActivePlaylistIndex;
        if (idx < 0 || idx >= _controller.Playlists.Count || _controller.Playlists[idx].IsVirtual)
        {
            await DisplayAlert("Elegí una playlist",
                "Para agregar archivos, primero elegí (o creá) una playlist editable.",
                "OK");
            return;
        }

        try
        {
            var options = new PickOptions
            {
                PickerTitle = "Agregar archivos a la playlist",
                FileTypes = new FilePickerFileType(
                    new Dictionary<DevicePlatform, IEnumerable<string>>
                    {
                        { DevicePlatform.Android, new[] { "audio/midi", "audio/x-midi", "audio/mpeg", "*/*" } },
                    })
            };

            var results = await FilePicker.Default.PickMultipleAsync(options);
            if (results == null) return;

            foreach (var result in results)
            {
                var ext = Path.GetExtension(result.FileName).ToLowerInvariant();
                if (ext != ".mid" && ext != ".midi" && ext != ".mp3") continue;

                var destPath = Path.Combine(FileSystem.CacheDirectory, result.FileName);
                using (var src = await result.OpenReadAsync())
                using (var dest = File.Create(destPath))
                    await src.CopyToAsync(dest);

                _controller.AddEntry(idx, destPath);
            }

            RefreshEntries();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"No se pudieron agregar los archivos: {ex.Message}", "OK");
        }
    }

    private async void OnCloseClicked(object sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }
}
