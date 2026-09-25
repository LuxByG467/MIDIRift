using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MIDIRift;

/// <summary>
/// Selector interno para administrar las canciones de una playlist sin abrir
/// el file picker del sistema. Trabaja únicamente sobre la biblioteca ya
/// escaneada y aplica los cambios en lote al pulsar Guardar.
/// </summary>
public sealed class PlaylistTrackPickerPage : ContentPage
{
    private readonly PlaylistController _controller;
    private readonly int _playlistIndex;
    private readonly HashSet<string> _selectedIds;
    private readonly ObservableCollection<PlaylistTrackChoiceRow> _visibleRows = new();
    private readonly CollectionView _tracksView;
    private readonly SearchBar _searchBar;
    private readonly Label _countLabel;
    private TrackMediaType? _mediaFilter;

    public PlaylistTrackPickerPage(PlaylistController controller, int playlistIndex)
    {
        _controller = controller;
        _playlistIndex = playlistIndex;

        var playlist = controller.Playlists[playlistIndex];
        _selectedIds = playlist.Entries
            .Select(e => e.TrackId)
            .ToHashSet(StringComparer.Ordinal);

        Title = "Administrar canciones";
        SetDynamicResource(BackgroundColorProperty, "PageBackground");
        Shell.SetNavBarIsVisible(this, false);

        var closeButton = MakeHeaderButton("Cancelar", OnCancelClicked);
        var saveButton = MakeHeaderButton("Guardar", OnSaveClicked, accent: true);

        _countLabel = new Label
        {
            TextColor = ThemePalette.Get("TextMuted"),
            FontSize = 12,
            VerticalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center,
        };

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            ColumnSpacing = 8,
            Padding = new Thickness(12, 10, 12, 6),
            BackgroundColor = ThemePalette.Get("SurfaceBackground"),
        };
        header.Add(closeButton, 0);
        header.Add(_countLabel, 1);
        header.Add(saveButton, 2);

        _searchBar = new SearchBar
        {
            Placeholder = "Buscar en la biblioteca",
            PlaceholderColor = ThemePalette.Get("TextSubtle"),
            TextColor = ThemePalette.Get("TextPrimary"),
            BackgroundColor = ThemePalette.Get("CardBackground"),
            CancelButtonColor = ThemePalette.Get("TextMuted"),
            Margin = new Thickness(12, 4),
        };
        _searchBar.TextChanged += (_, _) => RefreshRows();

        var filters = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
            },
            ColumnSpacing = 6,
            Padding = new Thickness(12, 2, 12, 6),
        };
        filters.Add(MakeFilterButton("Todo", null), 0);
        filters.Add(MakeFilterButton("MIDI", TrackMediaType.Midi), 1);
        filters.Add(MakeFilterButton("MP3", TrackMediaType.Mp3), 2);

        _tracksView = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            ItemsSource = _visibleRows,
            EmptyView = new Label
            {
                Text = "No hay canciones en la biblioteca. Usá Escanear primero.",
                TextColor = ThemePalette.Get("TextSubtle"),
                HorizontalTextAlignment = TextAlignment.Center,
                Margin = new Thickness(24, 40),
            },
            ItemTemplate = new DataTemplate(() =>
            {
                var check = new CheckBox
                {
                    Color = ThemePalette.Get("PurpleAccent"),
                    VerticalOptions = LayoutOptions.Center,
                };
                check.SetBinding(CheckBox.IsCheckedProperty, nameof(PlaylistTrackChoiceRow.IsSelected), mode: BindingMode.TwoWay);
                check.CheckedChanged += OnChoiceChanged;

                var title = new Label
                {
                    FontSize = 14,
                    TextColor = ThemePalette.Get("TextPrimary"),
                    LineBreakMode = LineBreakMode.MiddleTruncation,
                };
                title.SetBinding(Label.TextProperty, nameof(PlaylistTrackChoiceRow.Title));

                var subtitle = new Label
                {
                    FontSize = 11,
                    TextColor = ThemePalette.Get("TextSubtle"),
                };
                subtitle.SetBinding(Label.TextProperty, nameof(PlaylistTrackChoiceRow.Subtitle));

                var text = new VerticalStackLayout { Spacing = 1, VerticalOptions = LayoutOptions.Center };
                text.Add(title);
                text.Add(subtitle);

                var row = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(GridLength.Auto),
                        new ColumnDefinition(GridLength.Star),
                    },
                    ColumnSpacing = 8,
                    Padding = new Thickness(12, 8),
                };
                row.Add(check, 0);
                row.Add(text, 1);
                return row;
            }),
        };

        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            },
        };
        root.Add(header, 0, 0);
        root.Add(_searchBar, 0, 1);
        root.Add(filters, 0, 2);
        root.Add(_tracksView, 0, 3);
        Content = root;

        RefreshRows();
    }

    private Button MakeHeaderButton(string text, EventHandler clicked, bool accent = false)
    {
        var button = new Button
        {
            Text = text,
            FontSize = 13,
            TextColor = accent ? Colors.White : ThemePalette.Get("TextPrimary"),
            BackgroundColor = ThemePalette.Get(accent ? "PurplePrimary" : "ControlBackground"),
            CornerRadius = 8,
            HeightRequest = 38,
            Padding = new Thickness(12, 0),
        };
        button.Clicked += clicked;
        return button;
    }

    private Button MakeFilterButton(string text, TrackMediaType? filter)
    {
        var button = new Button
        {
            Text = text,
            FontSize = 12,
            TextColor = ThemePalette.Get("TextSecondary"),
            BackgroundColor = ThemePalette.Get("ControlBackground"),
            CornerRadius = 8,
            HeightRequest = 34,
            Padding = new Thickness(8, 0),
        };
        button.Clicked += (_, _) =>
        {
            _mediaFilter = filter;
            RefreshRows();
        };
        return button;
    }

    private void RefreshRows()
    {
        string query = _searchBar?.Text?.Trim() ?? "";
        var tracks = _controller.Library
            .Where(t => _mediaFilter == null || t.MediaType == _mediaFilter)
            .Where(t => string.IsNullOrEmpty(query) ||
                        t.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        Path.GetFileName(t.Path).Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _visibleRows.Clear();
        foreach (var track in tracks)
        {
            _visibleRows.Add(new PlaylistTrackChoiceRow
            {
                TrackId = track.Id,
                Title = string.IsNullOrWhiteSpace(track.Title) ? Path.GetFileNameWithoutExtension(track.Path) : track.Title,
                Subtitle = PlaylistPage.FormatSubtitle(track),
                IsSelected = _selectedIds.Contains(track.Id),
            });
        }

        RefreshCount();
    }

    private void OnChoiceChanged(object? sender, CheckedChangedEventArgs e)
    {
        if (sender is not CheckBox check || check.BindingContext is not PlaylistTrackChoiceRow row)
            return;

        if (e.Value) _selectedIds.Add(row.TrackId);
        else _selectedIds.Remove(row.TrackId);
        RefreshCount();
    }

    private void RefreshCount() =>
        _countLabel.Text = $"{_selectedIds.Count} seleccionada{(_selectedIds.Count == 1 ? "" : "s")}";

    private async void OnCancelClicked(object? sender, EventArgs e) =>
        await Navigation.PopModalAsync();

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        _controller.SetPlaylistMembership(_playlistIndex, _selectedIds);
        await Navigation.PopModalAsync();
    }
}

public sealed class PlaylistTrackChoiceRow : INotifyPropertyChanged
{
    private bool _isSelected;

    public string TrackId { get; init; } = "";
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
