namespace MIDIRift;

public sealed class EngineSettingsPage : ContentPage
{
    private readonly Picker _blockPicker;
    private readonly Picker _audioBackendPicker;
    private readonly Picker _themePicker;
    private readonly Picker _closeBehaviorPicker;
    private readonly Picker _startupBehaviorPicker;
    private readonly Button _lyraEngineButton;
    private readonly Button _classicEngineButton;
    private readonly Button _dsnEngineButton;
    private ChiptuneEngineKind _selectedEngine;
    private readonly Slider _ringSlider;
    private readonly Slider _audioSlider;
    private readonly Label _ringValue;
    private readonly Label _audioValue;
    private readonly Switch _measurementLabelsSwitch;

    public EngineSettingsPage()
    {
        Title = "Configuración de MIDIRift";
        SetDynamicResource(BackgroundColorProperty, "PageBackground");
        var current = EngineSettingsStore.Load();

        _selectedEngine = ChiptuneEngineSettings.Current;

        _lyraEngineButton = new Button
        {
            Text = "Lyra",
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 10,
            HeightRequest = 48,
        };
        _lyraEngineButton.Clicked += (_, _) => SelectEngine(ChiptuneEngineKind.Lyra);

        _classicEngineButton = new Button
        {
            Text = "Classic / Legacy",
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 10,
            HeightRequest = 48,
        };
        _classicEngineButton.Clicked += (_, _) => SelectEngine(ChiptuneEngineKind.Classic);

        _dsnEngineButton = new Button
        {
            Text = "DSN-like",
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 10,
            HeightRequest = 48,
        };
        _dsnEngineButton.Clicked += (_, _) => SelectEngine(ChiptuneEngineKind.DsnLike);

        ApplyEngineButtonState();

        _themePicker = new Picker { Title = "Tema de la aplicación" };
        _themePicker.SetDynamicResource(Picker.TextColorProperty, "TextPrimary");
        _themePicker.Items.Add("Controlado por el sistema");
        _themePicker.Items.Add("Claro");
        _themePicker.Items.Add("Oscuro");
        _themePicker.SelectedIndex = AppThemeSettings.Current switch
        {
            MIDIRiftTheme.Light => 1,
            MIDIRiftTheme.Dark => 2,
            _ => 0,
        };

        _closeBehaviorPicker = new Picker { Title = "Comportamiento al cerrar MIDIRift" };
        _closeBehaviorPicker.SetDynamicResource(Picker.TextColorProperty, "TextPrimary");
        _closeBehaviorPicker.Items.Add("Continuar reproduciendo");
        _closeBehaviorPicker.Items.Add("Detener inmediatamente");
        _closeBehaviorPicker.Items.Add("Continuar hasta terminar");
        _closeBehaviorPicker.SelectedIndex = PlaybackCloseBehaviorSettings.Current switch
        {
            PlaybackCloseBehavior.ContinuePlaying => 0,
            PlaybackCloseBehavior.StopImmediately => 1,
            _ => 2,
        };

        _startupBehaviorPicker = new Picker { Title = "Comportamiento al abrir MIDIRift" };
        _startupBehaviorPicker.SetDynamicResource(Picker.TextColorProperty, "TextPrimary");
        _startupBehaviorPicker.Items.Add("Esperar a que el usuario cargue una canción");
        _startupBehaviorPicker.Items.Add("Cargar la última canción reproducida");
        _startupBehaviorPicker.SelectedIndex = StartupPlaybackSettings.Behavior == StartupPlaybackBehavior.RestoreLastTrack ? 1 : 0;

        _audioBackendPicker = new Picker { Title = "Backend de audio de Lyra" };
        _audioBackendPicker.SetDynamicResource(Picker.TextColorProperty, "TextPrimary");
        _audioBackendPicker.Items.Add("AAudio Native RT");
        _audioBackendPicker.Items.Add("AudioTrack");
        _audioBackendPicker.SelectedIndex = string.Equals(
            current.AudioBackend,
            "AudioTrack",
            StringComparison.OrdinalIgnoreCase) ? 1 : 0;

        _blockPicker = new Picker { Title = "Frames por bloque" };
        _blockPicker.SetDynamicResource(Picker.TextColorProperty, "TextPrimary");
        foreach (int value in new[] { 256, 512, 1024, 2048 }) _blockPicker.Items.Add(value.ToString());
        _blockPicker.SelectedItem = current.RenderBlockFrames.ToString();

        _ringValue = ValueLabel(current.RingBufferBlocks, " bloques");
        _ringSlider = CreateIntegerSlider(4, 32, current.RingBufferBlocks, (_, _) =>
            _ringValue.Text = $"{Math.Round(_ringSlider.Value):0} bloques");

        _audioValue = ValueLabel(current.AudioTrackBufferMultiplier, "×");
        _audioSlider = CreateIntegerSlider(4, 24, current.AudioTrackBufferMultiplier, (_, _) =>
            _audioValue.Text = $"{Math.Round(_audioSlider.Value):0}×");

        _measurementLabelsSwitch = new Switch
        {
            IsToggled = MeasurementLabelsSettings.Enabled,
            ThumbColor = Colors.White,
            HorizontalOptions = LayoutOptions.End,
        };
        _measurementLabelsSwitch.SetDynamicResource(Switch.OnColorProperty, "PurpleAccent");

        var dsnPatchEditor = new Button { Text = "Abrir DSN Patch Editor", CornerRadius = 10 };
        dsnPatchEditor.SetDynamicResource(Button.BackgroundColorProperty, "ControlBackground");
        dsnPatchEditor.SetDynamicResource(Button.TextColorProperty, "TextPrimary");
        dsnPatchEditor.Clicked += async (_, _) => await Navigation.PushModalAsync(new DsnPatchEditorPage());

        var save = new Button { Text = "Guardar", TextColor = Colors.White, CornerRadius = 10 };
        save.SetDynamicResource(Button.BackgroundColorProperty, "PurplePrimary");
        save.Clicked += OnSave;

        var cancel = new Button { Text = "Cancelar", CornerRadius = 10 };
        cancel.SetDynamicResource(Button.BackgroundColorProperty, "ControlBackground");
        cancel.SetDynamicResource(Button.TextColorProperty, "TextSecondary");
        cancel.Clicked += async (_, _) => await Navigation.PopModalAsync();

        var title = new Label { Text = "Configuración de MIDIRift", FontSize = 24, FontAttributes = FontAttributes.Bold };
        title.SetDynamicResource(Label.TextColorProperty, "TextPrimary");
        var subtitle = new Label { Text = "El motor chiptune elegido se usa al cargar el próximo MIDI. Los cambios de buffers se aplican al reiniciar MIDIRift." };
        subtitle.SetDynamicResource(Label.TextColorProperty, "TextMuted");
        var recommendation = new Label { Text = "Recomendado para segundo plano: 1024 frames, 12 bloques y AudioTrack 12×.", FontSize = 12 };
        recommendation.SetDynamicResource(Label.TextColorProperty, "TextSubtle");

        var buttons = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 10,
            Children = { cancel, save }
        };
        Grid.SetColumn(save, 1);

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 20,
                Spacing = 14,
                Children =
                {
                    title,
                    subtitle,
                    SectionTitle("Configuraciones del engine"),
                    Card(
                        "Motor chiptune",
                        "Lyra es el motor moderno y predeterminado. Classic conserva el carácter Legacy. DSN-like usa el nuevo sintetizador dual-VCO optimizado. El cambio se aplica al próximo MIDI que cargues.",
                        EngineSelectorRow()),
                    Card("DSN-like Patch Bank", "Editor independiente para VCO1/VCO2, filtro, ADSR, LFO, FM, Sync y overrides de los 128 programas GM. No usa el WaveTypeSelector de Lyra/Legacy.", dsnPatchEditor),
                                        Card("Backend de audio de Lyra", "AAudio Native RT usa el callback nativo de baja latencia de Clean Room. Si AAudio no está disponible o falla al inicializar, Lyra cae automáticamente a AudioTrack.", _audioBackendPicker),
                    Card("Bloque de síntesis", "Bloques pequeños reparten la carga; bloques grandes reducen llamadas y toleran mejor el scheduler.", _blockPicker),
                    Card("Ring buffer PCM", "Cantidad de bloques que el sintetizador puede dejar preparados antes del hilo de salida.", Row(_ringSlider, _ringValue)),
                    Card("Buffer de AudioTrack", "Multiplicador sobre el tamaño de bloque para absorber pausas del sistema y apps pesadas.", Row(_audioSlider, _audioValue)),
                    recommendation,
                    SectionTitle("Apariencia y métricas"),
                    Card("Apariencia", "Elige un tema fijo o permite que MIDIRift siga el modo claro u oscuro de Android.", _themePicker),
                    Card("Etiquetas de medición", "Muestra RMS, dB, Peak, Crest y el aviso de clipping en el tracker, FFT y osciloscopio.", SettingRow("Mostrar métricas", _measurementLabelsSwitch)),
                    Card("Comportamiento al cerrar MIDIRift", "Elige qué ocurre con la reproducción cuando eliminas la app desde la pantalla de aplicaciones recientes.", _closeBehaviorPicker),
                    Card("Comportamiento al abrir MIDIRift", "Decide si la pantalla de reproducción inicia vacía o carga automáticamente la última canción reproducida.", _startupBehaviorPicker),
                    buttons
                }
            }
        };
    }

    private async void OnSave(object? sender, EventArgs e)
    {
        int block = int.TryParse(_blockPicker.SelectedItem?.ToString(), out int parsed) ? parsed : 1024;
        EngineSettingsStore.Save(new EngineSettings
        {
            RenderBlockFrames = block,
            RingBufferBlocks = (int)Math.Round(_ringSlider.Value),
            AudioTrackBufferMultiplier = (int)Math.Round(_audioSlider.Value),
            AudioBackend = _audioBackendPicker.SelectedIndex == 1 ? "AudioTrack" : "AAudio",
        });
        ChiptuneEngineSettings.Current = _selectedEngine;
        ChiptuneEngineSelection.DefaultEngine = _selectedEngine;
        MeasurementLabelsSettings.Enabled = _measurementLabelsSwitch.IsToggled;
        PlaybackCloseBehaviorSettings.Current = _closeBehaviorPicker.SelectedIndex switch
        {
            0 => PlaybackCloseBehavior.ContinuePlaying,
            1 => PlaybackCloseBehavior.StopImmediately,
            _ => PlaybackCloseBehavior.ContinueUntilFinished,
        };
        StartupPlaybackSettings.Behavior = _startupBehaviorPicker.SelectedIndex == 1
            ? StartupPlaybackBehavior.RestoreLastTrack
            : StartupPlaybackBehavior.EmptyPlayer;
        AppThemeSettings.Current = _themePicker.SelectedIndex switch
        {
            1 => MIDIRiftTheme.Light,
            2 => MIDIRiftTheme.Dark,
            _ => MIDIRiftTheme.System,
        };
        await DisplayAlert("Configuración guardada", "El motor chiptune seleccionado se usará al cargar el próximo MIDI. El backend de Lyra y los ajustes de buffer se aplican al reiniciar MIDIRift.", "Aceptar");
        await Navigation.PopModalAsync();
    }

    private Grid EngineSelectorRow()
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
            },
            ColumnSpacing = 10,
        };

        grid.Add(_lyraEngineButton, 0);
        grid.Add(_classicEngineButton, 1);
        grid.Add(_dsnEngineButton, 2);
        return grid;
    }

    private void SelectEngine(ChiptuneEngineKind engine)
    {
        _selectedEngine = engine;
        ApplyEngineButtonState();
    }

    private void ApplyEngineButtonState()
    {
        ApplyEngineButtonStyle(
            _lyraEngineButton,
            _selectedEngine == ChiptuneEngineKind.Lyra);

        ApplyEngineButtonStyle(
            _classicEngineButton,
            _selectedEngine == ChiptuneEngineKind.Classic);

        ApplyEngineButtonStyle(
            _dsnEngineButton,
            _selectedEngine == ChiptuneEngineKind.DsnLike);
    }

    private static void ApplyEngineButtonStyle(Button button, bool selected)
    {
        button.SetDynamicResource(
            Button.BackgroundColorProperty,
            selected ? "PurplePrimary" : "ControlBackground");

        if (selected)
            button.TextColor = Colors.White;
        else
            button.SetDynamicResource(Button.TextColorProperty, "TextSecondary");

        button.BorderWidth = selected ? 2 : 1;
        button.SetDynamicResource(
            Button.BorderColorProperty,
            selected ? "PurpleLight" : "BorderColor");
    }

    private static Label SectionTitle(string text)
    {
        var label = new Label
        {
            Text = text,
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            Margin = new Thickness(0, 8, 0, 0),
        };
        label.SetDynamicResource(Label.TextColorProperty, "PurpleLight");
        return label;
    }

    private static Border Card(string title, string description, View control)
    {
        var titleLabel = new Label { Text = title, FontAttributes = FontAttributes.Bold };
        titleLabel.SetDynamicResource(Label.TextColorProperty, "TextPrimary");
        var descriptionLabel = new Label { Text = description, FontSize = 12 };
        descriptionLabel.SetDynamicResource(Label.TextColorProperty, "TextMuted");
        var border = new Border
        {
            StrokeThickness = 1,
            Padding = 14,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            Content = new VerticalStackLayout { Spacing = 7, Children = { titleLabel, descriptionLabel, control } }
        };
        border.SetDynamicResource(Border.BackgroundColorProperty, "CardBackground");
        border.SetDynamicResource(Border.StrokeProperty, "BorderColor");
        return border;
    }

    private static Grid SettingRow(string label, View control)
    {
        var text = new Label { Text = label, VerticalTextAlignment = TextAlignment.Center };
        text.SetDynamicResource(Label.TextColorProperty, "TextSecondary");
        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 12,
            VerticalOptions = LayoutOptions.Center,
        };
        grid.Add(text);
        grid.Add(control, 1);
        return grid;
    }

    private static Grid Row(View slider, View value)
    {
        var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(70) }, ColumnSpacing = 8 };
        grid.Add(slider); grid.Add(value, 1); return grid;
    }

    private static Label ValueLabel(int value, string suffix)
    {
        var label = new Label { Text = $"{value}{suffix}", VerticalTextAlignment = TextAlignment.Center, HorizontalTextAlignment = TextAlignment.End };
        label.SetDynamicResource(Label.TextColorProperty, "TextSecondary");
        return label;
    }

    private static Slider CreateIntegerSlider(double min, double max, double value, EventHandler<ValueChangedEventArgs> changed)
    {
        var slider = new Slider { Minimum = min, Maximum = max, Value = value };
        slider.SetDynamicResource(Slider.MinimumTrackColorProperty, "GreenPrimary");
        slider.SetDynamicResource(Slider.MaximumTrackColorProperty, "ControlBackground");
        slider.SetDynamicResource(Slider.ThumbColorProperty, "GreenAccent");
        slider.ValueChanged += changed;
        return slider;
    }
}
