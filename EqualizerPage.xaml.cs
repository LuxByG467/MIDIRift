using Microsoft.Maui.Controls;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MIDIRift;

/// <summary>
/// EQ móvil con construcción escalonada. El primer frame contiene sólo la
/// ventana/cabecera; los controles nativos se agregan por grupos después de
/// que la modal ya está visible, evitando bloquear la apertura completa.
/// </summary>
public partial class EqualizerPage : ContentPage
{
    private readonly EqPresetStore _presetStore;
    private readonly float[] _initialGains;
    private readonly float _initialPreampDb;
    private readonly BassRestorationSettings _bassSettings;
    private bool _uiBuilt, _buildingUi, _suppressPickerEvents, _suppressPreampEvent, _suppressBassEvents;

    private EqualizerCurveView? _curveView;
    private Picker? _factoryPresetPicker, _customPresetPicker;
    private Slider? _preampSlider, _bassIntensitySlider, _bassFrequencySlider, _bassMixSlider;
    private Label? _preampValueLabel, _bassIntensityValue, _bassFrequencyValue, _bassMixValue;
    private Switch? _bassEnabledSwitch;
    private Entry? _saveNameEntry;

    public event Action<int, float>? OnBandGainChanged;
    public event Action<float[]>? OnGainsReplaced;
    public event Action<float>? OnPreampChanged;
    public event Action<BassRestorationSettings>? OnBassSettingsChanged;

    // Commit events are deliberately separate from live DSP events. Audio
    // follows the finger; disk persistence waits until the gesture ends.
    public event Action<float[]>? OnGainsCommitted;
    public event Action<float>? OnPreampCommitted;
    public event Action<BassRestorationSettings>? OnBassSettingsCommitted;

    public EqualizerPage(EqPresetStore presetStore, float[] initialGains, float initialPreampDb, BassRestorationSettings bassSettings)
    {
        _presetStore = presetStore;
        _initialGains = (float[])initialGains.Clone();
        _initialPreampDb = initialPreampDb;
        _bassSettings = bassSettings.Normalize();
        InitializeComponent(); // intentionally tiny: header + loading shell only
        _presetStore.CustomPresetsChanged += RefreshCustomPicker;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_uiBuilt || _buildingUi) return;
        _buildingUi = true;
        try
        {
            // Give Android one frame to present the modal before creating the zoo.
            await Task.Yield();
            await BuildCoreEqAsync();
            await Task.Yield();
            BuildBassSection();
            await Task.Yield();
            BuildCustomPresetSection();
            EqContentHost.Remove(EqLoadingShell);
            _uiBuilt = true;
        }
        finally { _buildingUi = false; }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _presetStore.CustomPresetsChanged -= RefreshCustomPicker;
    }

    private Task BuildCoreEqAsync()
    {
        _factoryPresetPicker = new Picker { Title = "Preset" };
        _factoryPresetPicker.SetDynamicResource(Picker.TextColorProperty, "TextPrimary");
        _factoryPresetPicker.SetDynamicResource(Picker.TitleColorProperty, "TextMuted");
        _factoryPresetPicker.ItemsSource = EqualizerPresets.Factory.Select(p => p.Name).ToList();
        _factoryPresetPicker.SelectedIndexChanged += OnFactoryPresetSelected;

        var reset = Button("Reset"); reset.Clicked += OnResetClicked;
        var presetRow = new Grid { Padding = new Thickness(12,8), ColumnSpacing = 8, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        presetRow.SetDynamicResource(BackgroundColorProperty, "SurfaceBackground");
        var presetLabel = Label("Preset:"); presetLabel.VerticalOptions = LayoutOptions.Center;
        presetRow.Add(presetLabel,0); presetRow.Add(_factoryPresetPicker,1); presetRow.Add(reset,2);
        EqContentHost.Add(presetRow);

        _curveView = new EqualizerCurveView { Margin = 10, HeightRequest = 390 };
        _curveView.SetGains(_initialGains);
        _curveView.OnBandGainChanged = (band, db) => OnBandGainChanged?.Invoke(band, db);
        _curveView.OnBandDragCompleted = () => OnGainsCommitted?.Invoke(_curveView.GetGains());
        EqContentHost.Add(_curveView);

        _preampSlider = new Slider { Minimum=-12, Maximum=12, Value=0 };
        _preampSlider.ValueChanged += OnPreampSliderChanged;
        _preampSlider.DragCompleted += (_,__) => OnPreampCommitted?.Invoke((float)_preampSlider.Value);
        _preampValueLabel = Label("+0.0 dB"); _preampValueLabel.WidthRequest=60; _preampValueLabel.HorizontalTextAlignment=TextAlignment.End;
        var preampRow = ThreeColumnRow(Label("Preamp:"), _preampSlider, _preampValueLabel);
        EqContentHost.Add(preampRow);
        SetPreampSliderValue(_initialPreampDb);
        return Task.CompletedTask;
    }

    private void BuildBassSection()
    {
        _bassEnabledSwitch = new Switch();
        _bassEnabledSwitch.Toggled += OnBassEnabledToggled;
        _bassIntensitySlider = Slider(0,1.5); _bassFrequencySlider = Slider(45,160); _bassMixSlider = Slider(0,1);
        _bassIntensityValue=Label(""); _bassFrequencyValue=Label(""); _bassMixValue=Label("");
        foreach (var s in new[]{_bassIntensitySlider,_bassFrequencySlider,_bassMixSlider}) { s.ValueChanged += OnBassParameterChanged; s.DragCompleted += OnBassDragCompleted; }
        var titleRow = new Grid { ColumnDefinitions={new ColumnDefinition(GridLength.Star),new ColumnDefinition(GridLength.Auto)} };
        var titleStack = new VerticalStackLayout { Spacing=2 }; titleStack.Add(Label("Bass Restoration", true)); titleStack.Add(Label("Refuerzo grave mono, ligero y post-EQ", false, 11));
        titleRow.Add(titleStack,0); titleRow.Add(_bassEnabledSwitch,1);
        var reset=Button("Restablecer Bass Restoration"); reset.Clicked += OnBassResetClicked;
        var stack = new VerticalStackLayout { Spacing=8 }; stack.Add(titleRow); stack.Add(ThreeColumnRow(Label("Intensidad"),_bassIntensitySlider,_bassIntensityValue)); stack.Add(ThreeColumnRow(Label("Frecuencia"),_bassFrequencySlider,_bassFrequencyValue)); stack.Add(ThreeColumnRow(Label("Mezcla"),_bassMixSlider,_bassMixValue)); stack.Add(reset);
        var border = new Border { Margin=new Thickness(10,6), Padding=12, StrokeThickness=1, Content=stack }; border.SetDynamicResource(BackgroundColorProperty,"CardBackground"); border.SetDynamicResource(Border.StrokeProperty,"BorderColor");
        EqContentHost.Add(border); SetBassControls(_bassSettings);
    }

    private void BuildCustomPresetSection()
    {
        _customPresetPicker = new Picker(); _customPresetPicker.SetDynamicResource(Picker.TextColorProperty,"TextPrimary");
        var load=Button("Cargar"); load.Clicked+=OnLoadCustomClicked; var del=Button("Eliminar"); del.Clicked+=OnDeleteCustomClicked;
        var row = new Grid { ColumnSpacing=8, ColumnDefinitions={new ColumnDefinition(GridLength.Auto),new ColumnDefinition(GridLength.Star),new ColumnDefinition(GridLength.Auto),new ColumnDefinition(GridLength.Auto)} };
        row.Add(Label("Personalizados:"),0); row.Add(_customPresetPicker,1); row.Add(load,2); row.Add(del,3);
        _saveNameEntry = new Entry { Placeholder="Nombre del preset" }; _saveNameEntry.SetDynamicResource(Entry.TextColorProperty,"TextPrimary");
        var save=Button("Guardar como…"); save.Clicked+=OnSaveAsClicked;
        var saveRow=new Grid { ColumnSpacing=8, ColumnDefinitions={new ColumnDefinition(GridLength.Star),new ColumnDefinition(GridLength.Auto)} }; saveRow.Add(_saveNameEntry,0); saveRow.Add(save,1);
        var stack=new VerticalStackLayout { Spacing=8, Padding=new Thickness(12,8,12,18) }; stack.SetDynamicResource(BackgroundColorProperty,"SurfaceBackground"); stack.Add(row); stack.Add(saveRow); EqContentHost.Add(stack);
        RefreshCustomPicker();
    }

    private static Label Label(string text, bool bold=false, double size=14) { var l=new Label{Text=text,FontSize=size,VerticalOptions=LayoutOptions.Center}; if(bold)l.FontAttributes=FontAttributes.Bold; l.SetDynamicResource(Microsoft.Maui.Controls.Label.TextColorProperty,"TextPrimary"); return l; }
    private static Button Button(string text) { var b=new Button{Text=text,CornerRadius=8,Padding=new Thickness(10,4)}; b.SetDynamicResource(Microsoft.Maui.Controls.Button.BackgroundColorProperty,"ControlBackground"); b.SetDynamicResource(Microsoft.Maui.Controls.Button.TextColorProperty,"TextPrimary"); return b; }
    private static Slider Slider(double min,double max)=>new(){Minimum=min,Maximum=max};
    private static Grid ThreeColumnRow(View a,View b,View c){var g=new Grid{Padding=new Thickness(12,6),ColumnSpacing=8,ColumnDefinitions={new ColumnDefinition(GridLength.Auto),new ColumnDefinition(GridLength.Star),new ColumnDefinition(GridLength.Auto)}};g.Add(a,0);g.Add(b,1);g.Add(c,2);return g;}

    private void ApplyPreset(EqPreset preset) { if(_curveView is null)return; _curveView.SetGains(preset.GainsDb); SetPreampSliderValue(preset.PreampDb); OnGainsReplaced?.Invoke(_curveView.GetGains()); OnPreampChanged?.Invoke(preset.PreampDb); OnGainsCommitted?.Invoke(_curveView.GetGains()); OnPreampCommitted?.Invoke(preset.PreampDb); }
    private void OnFactoryPresetSelected(object? s,EventArgs e){if(_suppressPickerEvents||_factoryPresetPicker is null)return;int i=_factoryPresetPicker.SelectedIndex;if(i>=0)ApplyPreset(EqualizerPresets.Factory[i]);}
    private void OnResetClicked(object? s,EventArgs e){if(_curveView is null)return;_curveView.ResetAll();SetPreampSliderValue(0);OnGainsReplaced?.Invoke(_curveView.GetGains());OnPreampChanged?.Invoke(0);OnGainsCommitted?.Invoke(_curveView.GetGains());OnPreampCommitted?.Invoke(0);}
    private EqPreset? SelectedCustomPreset(){if(_customPresetPicker is null)return null;int i=_customPresetPicker.SelectedIndex;return i>=0&&i<_presetStore.Custom.Count?_presetStore.Custom[i]:null;}
    private void OnLoadCustomClicked(object? s,EventArgs e){var p=SelectedCustomPreset();if(p!=null)ApplyPreset(p);}
    private void OnDeleteCustomClicked(object? s,EventArgs e){var p=SelectedCustomPreset();if(p!=null)_presetStore.DeleteCustomPreset(p.Name);}
    private void OnSaveAsClicked(object? s,EventArgs e){if(_saveNameEntry is null||_curveView is null||_preampSlider is null)return;var n=_saveNameEntry.Text?.Trim();if(string.IsNullOrEmpty(n))return;_presetStore.SaveCustomPreset(n,_curveView.GetGains(),(float)_preampSlider.Value);_saveNameEntry.Text="";}
    private async void OnCloseClicked(object s,EventArgs e)=>await Navigation.PopModalAsync();
    private void RefreshCustomPicker(){if(_customPresetPicker is null)return;_suppressPickerEvents=true;_customPresetPicker.ItemsSource=_presetStore.Custom.Select(p=>p.Name).ToList();_suppressPickerEvents=false;}

    private void OnPreampSliderChanged(object? s,ValueChangedEventArgs e){float db=(float)e.NewValue;if(_preampValueLabel!=null)_preampValueLabel.Text=$"{(db>=0?"+":"")}{db:0.0} dB";if(!_suppressPreampEvent)OnPreampChanged?.Invoke(db);}
    private void SetPreampSliderValue(float db){if(_preampSlider is null)return;_suppressPreampEvent=true;_preampSlider.Value=db;if(_preampValueLabel!=null)_preampValueLabel.Text=$"{(db>=0?"+":"")}{db:0.0} dB";_suppressPreampEvent=false;}
    private void OnBassEnabledToggled(object? s,ToggledEventArgs e){if(_suppressBassEvents)return;_bassSettings.Enabled=e.Value;PublishBassSettings();OnBassSettingsCommitted?.Invoke(_bassSettings.Clone());}
    private void OnBassParameterChanged(object? s,ValueChangedEventArgs e){UpdateBassValueLabels();if(_suppressBassEvents||_bassIntensitySlider is null||_bassFrequencySlider is null||_bassMixSlider is null)return;_bassSettings.Intensity=(float)_bassIntensitySlider.Value;_bassSettings.FrequencyHz=(float)_bassFrequencySlider.Value;_bassSettings.Mix=(float)_bassMixSlider.Value;PublishBassSettings();}
    private void OnBassDragCompleted(object? s,EventArgs e)=>OnBassSettingsCommitted?.Invoke(_bassSettings.Clone());
    private void OnBassResetClicked(object? s,EventArgs e){_bassSettings.Enabled=false;_bassSettings.Intensity=BassRestorationProcessor.DefaultIntensity;_bassSettings.FrequencyHz=BassRestorationProcessor.DefaultFrequencyHz;_bassSettings.Mix=BassRestorationProcessor.DefaultMix;SetBassControls(_bassSettings);PublishBassSettings();OnBassSettingsCommitted?.Invoke(_bassSettings.Clone());}
    private void SetBassControls(BassRestorationSettings x){if(_bassEnabledSwitch is null||_bassIntensitySlider is null||_bassFrequencySlider is null||_bassMixSlider is null)return;_suppressBassEvents=true;_bassEnabledSwitch.IsToggled=x.Enabled;_bassIntensitySlider.Value=x.Intensity;_bassFrequencySlider.Value=x.FrequencyHz;_bassMixSlider.Value=x.Mix;UpdateBassValueLabels();_suppressBassEvents=false;}
    private void UpdateBassValueLabels(){if(_bassIntensitySlider is null||_bassFrequencySlider is null||_bassMixSlider is null)return;if(_bassIntensityValue!=null)_bassIntensityValue.Text=$"{_bassIntensitySlider.Value:0.00}";if(_bassFrequencyValue!=null)_bassFrequencyValue.Text=$"{_bassFrequencySlider.Value:0} Hz";if(_bassMixValue!=null)_bassMixValue.Text=$"{_bassMixSlider.Value*100:0}%";}
    private void PublishBassSettings()=>OnBassSettingsChanged?.Invoke(_bassSettings.Clone());
}
