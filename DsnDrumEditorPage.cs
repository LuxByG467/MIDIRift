using MIDIRift.Synth.DsnLike;

namespace MIDIRift;

public sealed class DsnDrumEditorPage : ContentPage
{
    private readonly DsnDrumKitSettings _settings;
    private readonly Picker _piece = new() { Title = "Pieza GM" };
    private readonly Slider _tune = new(-24, 24, 0);
    private readonly Slider _decay = new(.20, 4, 1);
    private readonly Slider _gain = new(0, 2, 1);
    private readonly Slider _tone = new(0, 1, .5);
    private readonly Label _summary = new() { FontSize = 12 };
    private readonly Action? _saved;
    private bool _loading;

    private static readonly (int Note, string Name)[] Pieces =
    {
        (35,"Acoustic Bass Drum"),(36,"Bass Drum 1"),(37,"Side Stick"),(38,"Acoustic Snare"),
        (39,"Hand Clap"),(40,"Electric Snare"),(41,"Low Floor Tom"),(42,"Closed Hi-Hat"),
        (43,"High Floor Tom"),(44,"Pedal Hi-Hat"),(45,"Low Tom"),(46,"Open Hi-Hat"),
        (47,"Low-Mid Tom"),(48,"Hi-Mid Tom"),(49,"Crash Cymbal 1"),(50,"High Tom"),
        (51,"Ride Cymbal 1"),(52,"Chinese Cymbal"),(53,"Ride Bell"),(54,"Tambourine"),
        (55,"Splash Cymbal"),(56,"Cowbell"),(57,"Crash Cymbal 2"),(58,"Vibraslap"),
        (59,"Ride Cymbal 2"),(60,"Hi Bongo"),(61,"Low Bongo"),(62,"Mute Hi Conga"),
        (63,"Open Hi Conga"),(64,"Low Conga"),(65,"High Timbale"),(66,"Low Timbale"),
        (67,"High Agogo"),(68,"Low Agogo"),(69,"Cabasa"),(70,"Maracas"),
        (71,"Short Whistle"),(72,"Long Whistle"),(73,"Short Guiro"),(74,"Long Guiro"),
        (75,"Claves"),(76,"Hi Wood Block"),(77,"Low Wood Block"),(78,"Mute Cuica"),
        (79,"Open Cuica"),(80,"Mute Triangle"),(81,"Open Triangle")
    };

    public DsnDrumEditorPage(Action? saved = null)
    {
        Title = "DSN Drum Kit";
        SetDynamicResource(BackgroundColorProperty, "PageBackground");
        _saved = saved;
        _settings = DsnDrumKitSettingsStore.Load();

        foreach (var p in Pieces) _piece.Items.Add($"{p.Note} · {p.Name}");
        _piece.SelectedIndexChanged += (_,_) => { if (!_loading) LoadPiece(); };

        var stack = new VerticalStackLayout { Padding = 20, Spacing = 12 };
        stack.Add(new Label { Text = "DSN-like Drum Kit", FontSize = 24, FontAttributes = FontAttributes.Bold });
        stack.Add(new Label { Text = "Canal 10 · Percusión sintética GM 35–81", FontSize = 13 });
        stack.Add(_piece);
        stack.Add(_summary);
        stack.Add(Row("Tune (semitonos)", _tune));
        stack.Add(Row("Decay", _decay));
        stack.Add(Row("Gain", _gain));
        stack.Add(Row("Tone / metallicidad", _tone));

        var save = new Button { Text = "Guardar pieza" };
        save.Clicked += (_,_) =>
        {
            SaveCurrent();
            DsnDrumKitSettingsStore.Save(_settings);
            _saved?.Invoke();
            _summary.Text = Summary();
        };

        var reset = new Button { Text = "Restaurar Factory" };
        reset.Clicked += (_,_) =>
        {
            int note = CurrentNote;
            _settings.Pieces.Remove(note);
            DsnDrumKitSettingsStore.Save(_settings);
            LoadPiece();
            _saved?.Invoke();
        };

        var close = new Button { Text = "Cerrar" };
        close.Clicked += async (_,_) => await Navigation.PopModalAsync();

        stack.Add(new HorizontalStackLayout { Spacing = 8, Children = { save, reset } });
        stack.Add(close);
        Content = new ScrollView { Content = stack };

        _piece.SelectedIndex = 7; // Closed Hi-Hat: useful default landing point.
        LoadPiece();
    }

    private int CurrentNote => Pieces[Math.Clamp(_piece.SelectedIndex, 0, Pieces.Length - 1)].Note;

    private void LoadPiece()
    {
        _loading = true;
        var p = _settings.Get(CurrentNote);
        _tune.Value = p.TuneSemitones;
        _decay.Value = p.DecayScale;
        _gain.Value = p.GainScale;
        _tone.Value = p.Tone;
        _summary.Text = Summary();
        _loading = false;
    }

    private void SaveCurrent()
    {
        var p = _settings.Get(CurrentNote);
        p.TuneSemitones = (float)_tune.Value;
        p.DecayScale = (float)_decay.Value;
        p.GainScale = (float)_gain.Value;
        p.Tone = (float)_tone.Value;
    }

    private string Summary()
    {
        var p = _settings.Get(CurrentNote);
        return $"GM {CurrentNote} · Tune {p.TuneSemitones:+0.0;-0.0;0} st · Decay {p.DecayScale:0.00}× · Gain {p.GainScale:0.00}× · Tone {p.Tone:0.00}";
    }

    private static View Row(string title, Slider slider) =>
        new VerticalStackLayout { Spacing = 3, Children = { new Label { Text = title, FontSize = 12 }, slider } };
}
