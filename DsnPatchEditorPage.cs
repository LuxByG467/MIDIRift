using MIDIRift.Synth.DsnLike;

namespace MIDIRift;

/// <summary>Dedicated DSN-like patch editor. Intentionally separate from WaveTypeSelector.</summary>
public sealed class DsnPatchEditorPage : ContentPage
{
    private readonly DsnProgramBank _bank;
    private readonly Picker _program = new() { Title = "Programa General MIDI" };
    private readonly Picker _osc1 = EnumPicker<DsnOscillatorWave>("VCO 1");
    private readonly Picker _osc2 = EnumPicker<DsnOscillatorWave>("VCO 2");
    private readonly Picker _filter = EnumPicker<DsnFilterMode>("Filtro");
    private readonly Picker _lfoWave = EnumPicker<DsnLfoWave>("LFO");
    private readonly Switch _sync = new();
    private readonly Dictionary<string, Slider> _sliders = new();
    private readonly Label _source = new() { FontSize = 12 };
    private bool _loading;
    private DsnLikePatch _loadedPatch = DsnLikePatch.Default;
    private string? _workingPatchId;
    private readonly Label _dirty = new() { FontSize = 12 };

    public DsnPatchEditorPage(int initialProgram = 0, Action<int>? assignmentChanged = null, Action<int>? programChanged = null)
    {
        Title = "DSN Patch Editor";
        SetDynamicResource(BackgroundColorProperty, "PageBackground");
        _bank = DsnProgramBank.Load();

        for (int i = 0; i < 128; i++) _program.Items.Add(DsnFactoryPatchBank.GetName(i));
        _program.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _program.SelectedIndex < 0) return;
            int selectedProgram = _program.SelectedIndex;
            LoadProgram(selectedProgram);
            programChanged?.Invoke(selectedProgram);
        };
        _osc1.SelectedIndexChanged += (_,_)=>UpdateDirty(); _osc2.SelectedIndexChanged += (_,_)=>UpdateDirty(); _filter.SelectedIndexChanged += (_,_)=>UpdateDirty(); _lfoWave.SelectedIndexChanged += (_,_)=>UpdateDirty(); _sync.Toggled += (_,_)=>UpdateDirty();

        var save = Button("Guardar", async () =>
        {
            int p = Math.Max(0, _program.SelectedIndex);
            if (_workingPatchId is null)
            {
                string? name = await DisplayPromptAsync("Guardar patch", "Nombre del nuevo User Patch:", initialValue: DsnFactoryPatchBank.GetName(p) + " Edit");
                if (string.IsNullOrWhiteSpace(name)) return;
                _workingPatchId = _bank.SaveUserPatch(name, ReadPatch());
                _bank.Assign(p, _workingPatchId);
            }
            else _bank.SaveUserPatch(_bank.GetPatchName(_workingPatchId), ReadPatch(), _workingPatchId);
            _bank.Save(); _loadedPatch = ReadPatch(); UpdateSource(p); UpdateDirty(); assignmentChanged?.Invoke(p);
        });
        var saveAs = Button("Guardar como…", async () =>
        {
            int p=Math.Max(0,_program.SelectedIndex); string? name=await DisplayPromptAsync("Guardar como", "Nombre:", initialValue: DsnFactoryPatchBank.GetName(p)+" Custom");
            if(string.IsNullOrWhiteSpace(name)) return; _workingPatchId=_bank.SaveUserPatch(name,ReadPatch()); _bank.Assign(p,_workingPatchId); _bank.Save(); _loadedPatch=ReadPatch(); UpdateSource(p); UpdateDirty();
        });
        var duplicate = Button("Duplicar", async () =>
        {
            int p=Math.Max(0,_program.SelectedIndex); string? name=await DisplayPromptAsync("Duplicar patch", "Nombre de la copia:", initialValue: (_workingPatchId is null?DsnFactoryPatchBank.GetName(p):_bank.GetPatchName(_workingPatchId))+" Copy");
            if(string.IsNullOrWhiteSpace(name)) return; _workingPatchId=_bank.SaveUserPatch(name,ReadPatch()); _bank.Assign(p,_workingPatchId); _bank.Save(); _loadedPatch=ReadPatch(); UpdateSource(p); UpdateDirty();
        });
        var rename = Button("Renombrar", async () =>
        {
            if(_workingPatchId is null){await DisplayAlert("DSN-like","El Factory Bank es inmutable. Usa Guardar como para crear un User Patch.","Aceptar");return;}
            string? name=await DisplayPromptAsync("Renombrar patch","Nuevo nombre:",initialValue:_bank.GetPatchName(_workingPatchId)); if(string.IsNullOrWhiteSpace(name))return; _bank.RenameUserPatch(_workingPatchId,name); _bank.Save(); UpdateSource(Math.Max(0,_program.SelectedIndex));
        });
        var delete = Button("Eliminar User Patch", async () =>
        {
            if(_workingPatchId is null)return; if(!await DisplayAlert("Eliminar patch","Se eliminará el User Patch y sus asignaciones GM. Factory permanece intacto.","Eliminar","Cancelar"))return; _bank.DeleteUserPatch(_workingPatchId); _bank.Save(); int p = Math.Max(0,_program.SelectedIndex); LoadProgram(p); assignmentChanged?.Invoke(p);
        });
        var reset = Button("Restaurar Factory", async () =>
        {
            int p = Math.Max(0, _program.SelectedIndex); _bank.ResetOverride(p); _bank.Save(); LoadProgram(p); assignmentChanged?.Invoke(p);
            await DisplayAlert("DSN-like", "Programa GM restaurado a Factory. El User Patch no fue destruido.", "Aceptar");
        });
        var close = Button("Cerrar", async () => await Navigation.PopModalAsync());

        var stack = new VerticalStackLayout { Padding = 20, Spacing = 12 };
        stack.Add(TitleLabel("DSN-like Patch Editor"));
        stack.Add(Muted("Cambiar Programa GM aplica el Factory/User Patch asignado al canal inmediatamente. Guardar sólo es necesario para persistir cambios de síntesis en un User Patch."));
        stack.Add(_program); stack.Add(_source); stack.Add(_dirty);
        stack.Add(Section("VCO"));
        stack.Add(_osc1); stack.Add(SliderRow("VCO1 Level", "osc1Level", 0, 1, .75)); stack.Add(SliderRow("VCO1 Pulse Width", "pw1", .05, .95, .5));
        stack.Add(_osc2); stack.Add(SliderRow("VCO2 Level", "osc2Level", 0, 1, .25)); stack.Add(SliderRow("VCO2 Semitones", "semi2", -24, 24, 0)); stack.Add(SliderRow("VCO2 Pulse Width", "pw2", .05, .95, .5));
        stack.Add(SettingRow("Hard Sync VCO1 → VCO2", _sync)); stack.Add(SliderRow("FM Amount", "fm", 0, 1, 0));
        stack.Add(Section("VCF + Envelope"));
        stack.Add(_filter); stack.Add(SliderRow("Cutoff Hz", "cutoff", 80, 16000, 6000)); stack.Add(SliderRow("Resonance", "res", 0, .95, .1)); stack.Add(SliderRow("Envelope → Cutoff", "envCut", -1, 1, 0));
        stack.Add(SliderRow("Attack s", "atk", 0, 2, .005)); stack.Add(SliderRow("Decay s", "dec", 0, 2, .08)); stack.Add(SliderRow("Sustain", "sus", 0, 1, .75)); stack.Add(SliderRow("Release s", "rel", 0, 3, .12));
        stack.Add(Section("LFO + Output"));
        stack.Add(_lfoWave); stack.Add(SliderRow("LFO Hz", "lfoHz", 0, 20, 5)); stack.Add(SliderRow("LFO → Pitch", "lfoPitch", -1, 1, 0)); stack.Add(SliderRow("LFO → Cutoff", "lfoCut", -1, 1, 0)); stack.Add(SliderRow("LFO → Pulse Width", "lfoPw", -1, 1, 0));
        stack.Add(SliderRow("Drive", "drive", 0, 1, 0)); stack.Add(SliderRow("Output Gain", "gain", 0, 1.5, .8));
        var actions = new VerticalStackLayout { Spacing = 8 };
        actions.Add(new HorizontalStackLayout { Spacing=8, Children={save,saveAs,duplicate} });
        actions.Add(new HorizontalStackLayout { Spacing=8, Children={rename,delete,reset} });
        stack.Add(actions); stack.Add(close);
        Content = new ScrollView { Content = stack };
        _program.SelectedIndex = Math.Clamp(initialProgram, 0, 127);
        LoadProgram(_program.SelectedIndex);
    }

    private void LoadProgram(int program)
    {
        if (program < 0) return; _loading = true;
        var p = _bank.Resolve(program);
        _workingPatchId = _bank.AssignedPatchId(program);
        _loadedPatch = p;
        SetEnum(_osc1, p.Osc1Wave); SetEnum(_osc2, p.Osc2Wave); SetEnum(_filter, p.FilterMode); SetEnum(_lfoWave, p.LfoWave);
        _sync.IsToggled = p.HardSync;
        S("osc1Level",p.Osc1Level); S("pw1",p.PulseWidth1); S("osc2Level",p.Osc2Level); S("semi2",p.Osc2Semitones); S("pw2",p.PulseWidth2); S("fm",p.FmAmount);
        S("cutoff",p.CutoffHz); S("res",p.Resonance); S("envCut",p.EnvelopeToCutoff); S("atk",p.AttackSeconds); S("dec",p.DecaySeconds); S("sus",p.SustainLevel); S("rel",p.ReleaseSeconds);
        S("lfoHz",p.LfoHz); S("lfoPitch",p.LfoToPitch); S("lfoCut",p.LfoToCutoff); S("lfoPw",p.LfoToPulseWidth); S("drive",p.Drive); S("gain",p.OutputGain);
        UpdateSource(program); _loading = false; UpdateDirty();
    }

    private DsnLikePatch ReadPatch() => new()
    {
        Osc1Wave = GetEnum<DsnOscillatorWave>(_osc1), Osc2Wave = GetEnum<DsnOscillatorWave>(_osc2), Osc1Level = F("osc1Level"), Osc2Level = F("osc2Level"),
        Osc2Semitones = F("semi2"), PulseWidth1 = F("pw1"), PulseWidth2 = F("pw2"), HardSync = _sync.IsToggled, FmAmount = F("fm"),
        AttackSeconds = F("atk"), DecaySeconds = F("dec"), SustainLevel = F("sus"), ReleaseSeconds = F("rel"), FilterMode = GetEnum<DsnFilterMode>(_filter),
        CutoffHz = F("cutoff"), Resonance = F("res"), EnvelopeToCutoff = F("envCut"), LfoWave = GetEnum<DsnLfoWave>(_lfoWave), LfoHz = F("lfoHz"),
        LfoToPitch = F("lfoPitch"), LfoToCutoff = F("lfoCut"), LfoToPulseWidth = F("lfoPw"), Drive = F("drive"), OutputGain = F("gain")
    };

    private void UpdateSource(int p) { _source.Text = _workingPatchId is not null ? $"Asignado: {_bank.GetPatchName(_workingPatchId)} • User Bank" : $"Asignado: {DsnFactoryPatchBank.GetName(p)} • Factory Bank"; _source.SetDynamicResource(Label.TextColorProperty, "TextMuted"); }
    private void UpdateDirty() { if(_loading)return; _dirty.Text = ReadPatch() == _loadedPatch ? "Working Patch: sin cambios" : "Working Patch: cambios sin guardar"; _dirty.SetDynamicResource(Label.TextColorProperty,"TextMuted"); }
    private float F(string k) => (float)_sliders[k].Value; private void S(string k,float v) => _sliders[k].Value=v;
    private View SliderRow(string title,string key,double min,double max,double value) { var s=new Slider{Minimum=min,Maximum=max,Value=value}; _sliders[key]=s; var l=new Label{Text=$"{value:0.###}",WidthRequest=72,HorizontalTextAlignment=TextAlignment.End}; l.SetDynamicResource(Label.TextColorProperty,"TextSecondary"); s.ValueChanged += (_,e)=>{l.Text=e.NewValue.ToString(max>=100?"0":"0.###"); UpdateDirty();}; var g=new Grid{ColumnDefinitions={new ColumnDefinition(GridLength.Star),new ColumnDefinition(80)}}; g.Add(s,0); g.Add(l,1); return new VerticalStackLayout{Spacing=2,Children={new Label{Text=title},g}}; }
    private static Picker EnumPicker<T>(string title) where T:struct,Enum { var p=new Picker{Title=title}; foreach(var x in Enum.GetValues<T>()) p.Items.Add(x.ToString()); return p; }
    private static void SetEnum<T>(Picker p,T value) where T:struct,Enum => p.SelectedIndex=Array.IndexOf(Enum.GetValues<T>(),value);
    private static T GetEnum<T>(Picker p) where T:struct,Enum { var a=Enum.GetValues<T>(); return a[Math.Clamp(p.SelectedIndex,0,a.Length-1)]; }
    private static Label TitleLabel(string t)=>new(){Text=t,FontSize=24,FontAttributes=FontAttributes.Bold};
    private static Label Section(string t)=>new(){Text=t,FontSize=18,FontAttributes=FontAttributes.Bold,Margin=new Thickness(0,8,0,0)};
    private static Label Muted(string t)=>new(){Text=t,FontSize=12};
    private static Button Button(string t,Func<Task> action){var b=new Button{Text=t,CornerRadius=10}; b.Clicked+=async(_,_)=>await action(); return b;}
    private static View SettingRow(string t,View control){var g=new Grid{ColumnDefinitions={new ColumnDefinition(GridLength.Star),new ColumnDefinition(GridLength.Auto)}}; g.Add(new Label{Text=t,VerticalTextAlignment=TextAlignment.Center},0); g.Add(control,1); return g;}
}
