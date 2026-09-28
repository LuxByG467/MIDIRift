using Microsoft.Maui.Storage;
using System.Text.Json;

namespace MIDIRift.Synth.DsnLike;

/// <summary>
/// DSN program assignments and reusable user patches. Factory patches are immutable;
/// edits become named user patches and GM programs only store an assignment id.
/// </summary>
public sealed class DsnProgramBank
{
    private const string FormatId = "midirift.dsn.bank";
    private const int FormatVersion = 2;
    private readonly Dictionary<string, DsnLikePatch> _patches = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, string> _assignments = new();

    public static string StoragePath => Path.Combine(AppDataPaths.Root, "dsnlike-bank-v2.json");
    private static string LegacyStoragePath => Path.Combine(FileSystem.AppDataDirectory, "dsnlike-bank-v2.json");
    public IReadOnlyDictionary<string,DsnLikePatch> UserPatches => _patches;

    public DsnLikePatch Resolve(int program)
    {
        program = Math.Clamp(program,0,127);
        return _assignments.TryGetValue(program,out var id) && _patches.TryGetValue(id,out var patch) ? patch : DsnFactoryPatchBank.Get(program);
    }
    public string? AssignedPatchId(int program) => _assignments.TryGetValue(Math.Clamp(program,0,127),out var id) ? id : null;
    public bool HasOverride(int program) => AssignedPatchId(program) is not null;
    public string SaveUserPatch(string name, DsnLikePatch patch, string? existingId = null)
    {
        DsnPatchStorage.Validate(patch);
        string id = string.IsNullOrWhiteSpace(existingId) ? Guid.NewGuid().ToString("N") : existingId;
        _patches[id] = patch; PatchNames[id] = DsnPatchStorage.NormalizeName(name); return id;
    }
    public Dictionary<string,string> PatchNames { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string GetPatchName(string id) => PatchNames.TryGetValue(id,out var n) ? n : "User Patch";
    public void Assign(int program,string patchId) { if(!_patches.ContainsKey(patchId)) throw new KeyNotFoundException(patchId); _assignments[Math.Clamp(program,0,127)] = patchId; }
    public void SetOverride(int program,DsnLikePatch patch) { var id=SaveUserPatch(DsnFactoryPatchBank.GetName(program)+" Edit",patch); Assign(program,id); }
    public void ResetOverride(int program) => _assignments.Remove(Math.Clamp(program,0,127));
    public bool DeleteUserPatch(string id) { foreach(var p in _assignments.Where(x=>x.Value.Equals(id,StringComparison.OrdinalIgnoreCase)).Select(x=>x.Key).ToArray()) _assignments.Remove(p); PatchNames.Remove(id); return _patches.Remove(id); }
    public string DuplicateUserPatch(string id,string name) => SaveUserPatch(name,_patches[id]);
    public void RenameUserPatch(string id,string name) { if(!_patches.ContainsKey(id)) throw new KeyNotFoundException(id); PatchNames[id]=DsnPatchStorage.NormalizeName(name); }

    public void Save()
    {
        Directory.CreateDirectory(AppDataPaths.Root);
        var doc=new BankDocument{Patches=_patches.ToDictionary(x=>x.Key,x=>new NamedPatch{ Name=GetPatchName(x.Key),Patch=x.Value}),Assignments=_assignments.ToDictionary(x=>x.Key.ToString(),x=>x.Value)};
        FirstRunInitializer.AtomicWriteAllText(StoragePath,JsonSerializer.Serialize(doc,JsonOptions));
    }
    public static DsnProgramBank Load()
    {
        string loadPath = StoragePath;
        if (!File.Exists(loadPath) && File.Exists(LegacyStoragePath))
            loadPath = LegacyStoragePath;
        if (!File.Exists(loadPath))
            return new DsnProgramBank();

        try
        {
            var d = JsonSerializer.Deserialize<BankDocument>(File.ReadAllText(loadPath), JsonOptions);
            if (d is null || d.Format != FormatId || d.Version < 1 || d.Version > FormatVersion)
                throw new InvalidDataException("Banco DSN incompatible o inválido.");

            var b = new DsnProgramBank();
            if (d.Patches != null)
            {
                foreach (var x in d.Patches)
                {
                    if (x.Value?.Patch is null) continue;
                    try
                    {
                        DsnPatchStorage.Validate(x.Value.Patch);
                        b._patches[x.Key] = x.Value.Patch;
                        b.PatchNames[x.Key] = DsnPatchStorage.NormalizeName(x.Value.Name);
                    }
                    catch
                    {
                        // Un patch malo no invalida todo el banco; simplemente no se importa.
                    }
                }
            }

            if (d.Assignments != null)
                foreach (var x in d.Assignments)
                    if (int.TryParse(x.Key, out var program) &&
                        program is >= 0 and <= 127 &&
                        b._patches.ContainsKey(x.Value))
                        b._assignments[program] = x.Value;

            return b;
        }
        catch
        {
            FirstRunInitializer.Quarantine(loadPath);
            // Ausencia de banco es un estado válido: Factory Bank vive en código.
            return new DsnProgramBank();
        }
    }
    private static readonly JsonSerializerOptions JsonOptions=new(){WriteIndented=true,PropertyNameCaseInsensitive=true};
    private sealed class NamedPatch { public string Name{get;set;}="User Patch"; public DsnLikePatch? Patch{get;set;} }
    private sealed class BankDocument { public string Format{get;set;}=FormatId; public int Version{get;set;}=FormatVersion; public Dictionary<string,NamedPatch>? Patches{get;set;} public Dictionary<string,string>? Assignments{get;set;} }
}

/// <summary>Factory GM interpretation for DSN-like. Deliberately engine-specific.</summary>
public static class DsnFactoryPatchBank
{
    // GM Level 1 names. The bank remains a DSN interpretation: these names describe
    // the MIDI program semantics, not a promise to emulate acoustic instruments.
    private static readonly string[] ProgramNames =
    {
        "Acoustic Grand Piano","Bright Acoustic Piano","Electric Grand Piano","Honky-tonk Piano","Electric Piano 1","Electric Piano 2","Harpsichord","Clavinet",
        "Celesta","Glockenspiel","Music Box","Vibraphone","Marimba","Xylophone","Tubular Bells","Dulcimer",
        "Drawbar Organ","Percussive Organ","Rock Organ","Church Organ","Reed Organ","Accordion","Harmonica","Tango Accordion",
        "Acoustic Guitar (nylon)","Acoustic Guitar (steel)","Electric Guitar (jazz)","Electric Guitar (clean)","Electric Guitar (muted)","Overdriven Guitar","Distortion Guitar","Guitar Harmonics",
        "Acoustic Bass","Electric Bass (finger)","Electric Bass (pick)","Fretless Bass","Slap Bass 1","Slap Bass 2","Synth Bass 1","Synth Bass 2",
        "Violin","Viola","Cello","Contrabass","Tremolo Strings","Pizzicato Strings","Orchestral Harp","Timpani",
        "String Ensemble 1","String Ensemble 2","Synth Strings 1","Synth Strings 2","Choir Aahs","Voice Oohs","Synth Voice","Orchestra Hit",
        "Trumpet","Trombone","Tuba","Muted Trumpet","French Horn","Brass Section","Synth Brass 1","Synth Brass 2",
        "Soprano Sax","Alto Sax","Tenor Sax","Baritone Sax","Oboe","English Horn","Bassoon","Clarinet",
        "Piccolo","Flute","Recorder","Pan Flute","Blown Bottle","Shakuhachi","Whistle","Ocarina",
        "Lead 1 (square)","Lead 2 (sawtooth)","Lead 3 (calliope)","Lead 4 (chiff)","Lead 5 (charang)","Lead 6 (voice)","Lead 7 (fifths)","Lead 8 (bass + lead)",
        "Pad 1 (new age)","Pad 2 (warm)","Pad 3 (polysynth)","Pad 4 (choir)","Pad 5 (bowed)","Pad 6 (metallic)","Pad 7 (halo)","Pad 8 (sweep)",
        "FX 1 (rain)","FX 2 (soundtrack)","FX 3 (crystal)","FX 4 (atmosphere)","FX 5 (brightness)","FX 6 (goblins)","FX 7 (echoes)","FX 8 (sci-fi)",
        "Sitar","Banjo","Shamisen","Koto","Kalimba","Bag Pipe","Fiddle","Shanai",
        "Tinkle Bell","Agogo","Steel Drums","Woodblock","Taiko Drum","Melodic Tom","Synth Drum","Reverse Cymbal",
        "Guitar Fret Noise","Breath Noise","Seashore","Bird Tweet","Telephone Ring","Helicopter","Applause","Gunshot"
    };

    public static string GetName(int program)
    {
        program = Math.Clamp(program, 0, 127);
        return $"GM {program + 1:000} • {ProgramNames[program]}";
    }

    public static DsnLikePatch Get(int program)
    {
        program = Math.Clamp(program, 0, 127);

        // Intentionally hand-shaped by GM program instead of deriving 128 aliases
        // from sixteen family templates. DSN-like should reveal its own architecture.
        return program switch
        {
            // Piano
            0 => P(W.Triangle,W.Pulse,.78f,.22f,12,.003f,.28f,.30f,.42f,5200,.12f,.35f,0,.02f,pw2:.44f),
            1 => P(W.Saw,W.Triangle,.45f,.55f,12,.002f,.20f,.24f,.34f,7000,.10f,.25f,0,.025f),
            2 => P(W.Triangle,W.Sine,.62f,.38f,12,.006f,.38f,.45f,.65f,4700,.18f,.45f,.03f,.04f),
            3 => P(W.Pulse,W.Pulse,.62f,.38f,12,.001f,.12f,.18f,.24f,3900,.24f,.55f,0,.10f,pw1:.35f,pw2:.63f),
            4 => P(W.Sine,W.Pulse,.60f,.40f,12,.004f,.55f,.55f,.85f,5600,.10f,.20f,.04f,.025f,pw2:.42f),
            5 => P(W.Triangle,W.Pulse,.48f,.52f,12,.003f,.42f,.48f,.75f,6500,.16f,.30f,.05f,.06f,pw2:.31f),
            6 => P(W.Pulse,W.Saw,.70f,.30f,12,.001f,.18f,.22f,.25f,7200,.22f,.62f,0,.05f,pw1:.28f),
            7 => P(W.Pulse,W.Triangle,.72f,.28f,0,.001f,.09f,.35f,.16f,6100,.15f,.48f,0,.07f,pw1:.23f),

            // Chromatic percussion
            8 => P(W.Sine,W.Triangle,.75f,.25f,24,.001f,.55f,.08f,.60f,10500,.08f,.18f,.06f,0),
            9 => P(W.Sine,W.Sine,.72f,.28f,19,.001f,.80f,.05f,.75f,12500,.06f,.12f,.08f,.01f),
            10=> P(W.Sine,W.Triangle,.66f,.34f,12,.002f,.95f,.12f,1.05f,8500,.10f,.16f,.05f,.01f),
            11=> P(W.Sine,W.Triangle,.55f,.45f,12,.004f,.75f,.32f,1.10f,7200,.12f,.20f,.07f,.02f),
            12=> P(W.Triangle,W.Sine,.78f,.22f,12,.001f,.28f,.10f,.30f,6200,.08f,.30f,0,.02f),
            13=> P(W.Triangle,W.Pulse,.82f,.18f,12,.001f,.18f,.05f,.20f,8000,.08f,.42f,0,.025f,pw2:.35f),
            14=> P(W.Sine,W.Pulse,.70f,.30f,12,.001f,1.20f,.12f,1.40f,10000,.18f,.15f,.08f,.03f,pw2:.40f),
            15=> P(W.Triangle,W.Pulse,.64f,.36f,12,.003f,.65f,.18f,.70f,5800,.12f,.32f,.03f,.03f,pw2:.30f),

            // Organs
            16=> P(W.Sine,W.Sine,.62f,.38f,12,.008f,.05f,.94f,.12f,12000,.03f,0,.02f,0),
            17=> P(W.Sine,W.Pulse,.58f,.42f,12,.002f,.12f,.82f,.16f,9800,.08f,.08f,.03f,.015f,pw2:.42f),
            18=> P(W.Pulse,W.Saw,.58f,.42f,12,.004f,.08f,.90f,.12f,8800,.12f,.12f,.04f,.07f,pw1:.44f),
            19=> P(W.Sine,W.Triangle,.70f,.30f,12,.060f,.18f,.96f,.80f,9200,.04f,0,.015f,0),
            20=> P(W.Pulse,W.Sine,.55f,.45f,12,.012f,.10f,.88f,.20f,7200,.10f,.12f,.04f,.02f,pw1:.36f),
            21=> P(W.Pulse,W.Triangle,.52f,.48f,0,.010f,.14f,.82f,.22f,6800,.10f,.10f,.04f,.02f,pw1:.40f),
            22=> P(W.Pulse,W.Sine,.48f,.52f,12,.006f,.18f,.72f,.20f,7800,.08f,.16f,.05f,.015f,pw1:.32f),
            23=> P(W.Pulse,W.Triangle,.60f,.40f,12,.008f,.10f,.86f,.18f,7600,.11f,.14f,.04f,.025f,pw1:.38f),

            // Guitars
            24=> P(W.Triangle,W.Pulse,.76f,.24f,12,.002f,.22f,.36f,.28f,4800,.10f,.45f,0,.02f,pw2:.40f),
            25=> P(W.Triangle,W.Saw,.70f,.30f,12,.001f,.18f,.30f,.25f,6000,.12f,.52f,0,.03f),
            26=> P(W.Triangle,W.Pulse,.58f,.42f,0,.006f,.18f,.58f,.30f,4300,.18f,.28f,.02f,.025f,pw2:.46f),
            27=> P(W.Pulse,W.Triangle,.56f,.44f,0,.003f,.15f,.62f,.22f,5400,.14f,.30f,.02f,.03f,pw1:.43f),
            28=> P(W.Pulse,W.Saw,.72f,.28f,0,.001f,.08f,.26f,.10f,3600,.18f,.68f,0,.06f,pw1:.26f),
            29=> P(W.Saw,W.Pulse,.72f,.28f,0,.002f,.12f,.68f,.18f,5200,.28f,.35f,0,.22f,pw2:.42f),
            30=> P(W.Saw,W.Pulse,.78f,.22f,0,.001f,.08f,.74f,.16f,6100,.34f,.42f,0,.48f,pw2:.34f),
            31=> P(W.Sine,W.Pulse,.70f,.30f,12,.001f,.45f,.10f,.55f,9500,.12f,.20f,.04f,.03f,pw2:.25f),

            // Bass
            32=> P(W.Triangle,W.Sine,.82f,.18f,-12,.003f,.16f,.72f,.18f,1700,.12f,.38f,0,.02f),
            33=> P(W.Triangle,W.Pulse,.72f,.28f,-12,.002f,.12f,.70f,.14f,2100,.18f,.45f,0,.04f,pw2:.40f),
            34=> P(W.Pulse,W.Triangle,.68f,.32f,-12,.001f,.10f,.66f,.12f,2600,.20f,.52f,0,.05f,pw1:.36f),
            35=> P(W.Sine,W.Triangle,.74f,.26f,-12,.008f,.20f,.76f,.28f,1900,.16f,.28f,.03f,.02f),
            36=> P(W.Pulse,W.Saw,.74f,.26f,-12,.001f,.06f,.48f,.08f,3200,.24f,.78f,0,.09f,pw1:.30f),
            37=> P(W.Saw,W.Pulse,.70f,.30f,-12,.001f,.05f,.44f,.07f,3800,.26f,.84f,0,.12f,pw2:.32f),
            38=> P(W.Saw,W.Pulse,.66f,.34f,-12,.001f,.09f,.76f,.12f,2800,.32f,.60f,.03f,.14f,fm:.05f,pw2:.38f),
            39=> P(W.Pulse,W.Saw,.62f,.38f,-12,.001f,.07f,.82f,.10f,3400,.38f,.68f,.04f,.20f,fm:.10f,sync:true,pw1:.32f),

            // Strings
            40=> P(W.Saw,W.Triangle,.58f,.42f,0,.055f,.30f,.78f,.55f,5200,.12f,.10f,.04f,.02f),
            41=> P(W.Triangle,W.Saw,.62f,.38f,0,.070f,.34f,.76f,.60f,4600,.10f,.08f,.035f,.018f),
            42=> P(W.Triangle,W.Saw,.68f,.32f,-12,.075f,.38f,.80f,.70f,3900,.12f,.06f,.03f,.018f),
            43=> P(W.Triangle,W.Saw,.74f,.26f,-12,.090f,.42f,.82f,.80f,3300,.14f,.05f,.025f,.018f),
            44=> P(W.Saw,W.Pulse,.60f,.40f,0,.025f,.22f,.68f,.35f,5000,.20f,.18f,.10f,.025f,pw2:.46f),
            45=> P(W.Pulse,W.Triangle,.72f,.28f,0,.001f,.12f,.12f,.10f,6200,.12f,.72f,0,.02f,pw1:.36f),
            46=> P(W.Triangle,W.Sine,.60f,.40f,12,.010f,.65f,.36f,1.20f,7000,.10f,.30f,.05f,.015f),
            47=> P(W.Sine,W.Triangle,.82f,.18f,-12,.001f,.18f,.08f,.25f,2400,.22f,.90f,0,.04f),

            // Ensembles / voices
            48=> P(W.Saw,W.Triangle,.56f,.44f,0,.120f,.45f,.76f,.90f,4700,.16f,.08f,.05f,.02f),
            49=> P(W.Triangle,W.Saw,.60f,.40f,12,.180f,.60f,.72f,1.20f,3900,.18f,.06f,.07f,.02f),
            50=> P(W.Saw,W.Pulse,.58f,.42f,12,.090f,.35f,.70f,.75f,5600,.22f,.12f,.08f,.04f,pw2:.42f),
            51=> P(W.Pulse,W.Triangle,.52f,.48f,12,.160f,.55f,.68f,1.10f,4200,.24f,.08f,.10f,.035f,pw1:.40f),
            52=> P(W.Sine,W.Triangle,.58f,.42f,12,.100f,.38f,.78f,.85f,5200,.18f,.04f,.07f,.015f),
            53=> P(W.Sine,W.Pulse,.66f,.34f,12,.130f,.45f,.72f,.95f,4600,.14f,.05f,.06f,.015f,pw2:.44f),
            54=> P(W.Sine,W.Saw,.48f,.52f,12,.080f,.30f,.70f,.70f,6200,.26f,.08f,.12f,.04f,fm:.04f),
            55=> P(W.Saw,W.Pulse,.72f,.28f,12,.001f,.08f,.18f,.22f,8500,.34f,.90f,0,.18f,sync:true,pw2:.30f),

            // Brass
            56=> P(W.Saw,W.Pulse,.68f,.32f,0,.012f,.16f,.78f,.22f,6200,.22f,.28f,.04f,.07f,pw2:.42f),
            57=> P(W.Saw,W.Triangle,.70f,.30f,-12,.018f,.20f,.80f,.26f,5000,.20f,.24f,.035f,.06f),
            58=> P(W.Pulse,W.Triangle,.72f,.28f,-12,.020f,.22f,.82f,.28f,3600,.18f,.20f,.03f,.05f,pw1:.38f),
            59=> P(W.Pulse,W.Sine,.62f,.38f,0,.008f,.12f,.64f,.16f,4300,.30f,.34f,.02f,.035f,pw1:.28f),
            60=> P(W.Triangle,W.Saw,.58f,.42f,-12,.030f,.26f,.84f,.34f,4500,.16f,.18f,.025f,.04f),
            61=> P(W.Saw,W.Pulse,.72f,.28f,0,.014f,.18f,.80f,.24f,5700,.26f,.26f,.04f,.08f,pw2:.38f),
            62=> P(W.Saw,W.Pulse,.62f,.38f,12,.004f,.10f,.82f,.14f,7200,.34f,.44f,.06f,.16f,fm:.05f,sync:true,pw2:.35f),
            63=> P(W.Pulse,W.Saw,.58f,.42f,-12,.003f,.09f,.84f,.12f,6400,.40f,.50f,.08f,.22f,fm:.12f,sync:true,pw1:.30f),

            // Reeds
            64=> P(W.Pulse,W.Saw,.60f,.40f,0,.018f,.16f,.76f,.24f,5200,.24f,.18f,.04f,.03f,pw1:.40f),
            65=> P(W.Pulse,W.Triangle,.58f,.42f,0,.016f,.18f,.78f,.26f,4700,.22f,.16f,.035f,.025f,pw1:.42f),
            66=> P(W.Pulse,W.Saw,.56f,.44f,-12,.014f,.18f,.80f,.25f,4200,.20f,.15f,.03f,.03f,pw1:.44f),
            67=> P(W.Pulse,W.Triangle,.62f,.38f,-12,.016f,.20f,.82f,.28f,3600,.18f,.14f,.025f,.025f,pw1:.46f),
            68=> P(W.Saw,W.Sine,.48f,.52f,0,.030f,.22f,.72f,.32f,5000,.28f,.14f,.04f,.02f),
            69=> P(W.Triangle,W.Saw,.62f,.38f,-12,.032f,.24f,.74f,.36f,4200,.24f,.12f,.035f,.02f),
            70=> P(W.Triangle,W.Pulse,.70f,.30f,-12,.025f,.20f,.78f,.30f,3500,.20f,.12f,.03f,.02f,pw2:.42f),
            71=> P(W.Pulse,W.Triangle,.64f,.36f,0,.012f,.14f,.76f,.20f,4400,.22f,.20f,.04f,.025f,pw1:.36f),

            // Pipes
            72=> P(W.Sine,W.Triangle,.76f,.24f,12,.012f,.16f,.84f,.22f,10500,.04f,.06f,.05f,0),
            73=> P(W.Sine,W.Triangle,.70f,.30f,12,.018f,.20f,.82f,.28f,9200,.05f,.05f,.06f,0),
            74=> P(W.Triangle,W.Sine,.68f,.32f,12,.010f,.14f,.80f,.20f,8800,.06f,.08f,.05f,.005f),
            75=> P(W.Sine,W.Pulse,.72f,.28f,12,.020f,.22f,.78f,.30f,7600,.08f,.10f,.05f,.008f,pw2:.44f),
            76=> P(W.Sine,W.Triangle,.64f,.36f,12,.030f,.26f,.72f,.38f,6800,.10f,.12f,.04f,.008f),
            77=> P(W.Triangle,W.Pulse,.66f,.34f,12,.018f,.20f,.76f,.32f,6200,.12f,.14f,.04f,.01f,pw2:.38f),
            78=> P(W.Sine,W.Pulse,.80f,.20f,24,.004f,.10f,.70f,.14f,11500,.04f,.18f,.04f,0,pw2:.46f),
            79=> P(W.Sine,W.Triangle,.72f,.28f,12,.014f,.18f,.80f,.24f,8400,.06f,.10f,.05f,0),

            // Leads: deliberately expose DSN's character.
            80=> P(W.Pulse,W.Pulse,.66f,.34f,12,.002f,.08f,.72f,.12f,7200,.24f,.38f,.06f,.08f,pw1:.50f,pw2:.25f),
            81=> P(W.Saw,W.Saw,.64f,.36f,12,.001f,.07f,.74f,.10f,8200,.28f,.44f,.05f,.12f),
            82=> P(W.Pulse,W.Sine,.54f,.46f,12,.004f,.12f,.68f,.18f,6500,.32f,.30f,.08f,.10f,fm:.08f,pw1:.34f),
            83=> P(W.Saw,W.Pulse,.74f,.26f,24,.001f,.05f,.56f,.08f,9800,.22f,.70f,.02f,.10f,pw2:.20f),
            84=> P(W.Saw,W.Pulse,.60f,.40f,12,.001f,.08f,.76f,.12f,7600,.42f,.48f,.06f,.22f,fm:.10f,sync:true,pw2:.32f),
            85=> P(W.Sine,W.Pulse,.48f,.52f,12,.008f,.16f,.72f,.24f,6100,.26f,.22f,.12f,.06f,fm:.06f,pw2:.40f),
            86=> P(W.Saw,W.Pulse,.72f,.28f,7,.001f,.08f,.78f,.12f,8400,.34f,.42f,.05f,.16f,sync:true,pw2:.36f),
            87=> P(W.Pulse,W.Saw,.68f,.32f,-12,.001f,.06f,.82f,.10f,5200,.38f,.52f,.04f,.18f,fm:.08f,pw1:.30f),

            // Pads
            88=> P(W.Triangle,W.Sine,.58f,.42f,12,.280f,.80f,.68f,1.40f,5200,.18f,.04f,.12f,.01f),
            89=> P(W.Triangle,W.Pulse,.64f,.36f,12,.220f,.70f,.74f,1.20f,3600,.22f,.05f,.10f,.015f,pw2:.46f),
            90=> P(W.Saw,W.Pulse,.54f,.46f,12,.180f,.60f,.70f,1.00f,4700,.30f,.06f,.14f,.04f,pw2:.38f),
            91=> P(W.Sine,W.Triangle,.62f,.38f,12,.320f,.90f,.72f,1.60f,4300,.20f,.03f,.10f,.01f),
            92=> P(W.Triangle,W.Saw,.56f,.44f,12,.380f,1.00f,.66f,1.80f,3300,.26f,.02f,.16f,.02f),
            93=> P(W.Sine,W.Pulse,.42f,.58f,19,.120f,.55f,.62f,1.30f,6800,.46f,.04f,.18f,.06f,fm:.16f,pw2:.30f),
            94=> P(W.Sine,W.Saw,.58f,.42f,12,.300f,.85f,.70f,1.70f,5800,.34f,.03f,.15f,.03f,fm:.05f),
            95=> P(W.Saw,W.Pulse,.58f,.42f,12,.450f,1.20f,.64f,2.20f,2600,.48f,.02f,.32f,.06f,pw2:.42f),

            // Synth FX
            96=> P(W.Sine,W.Pulse,.48f,.52f,19,.020f,.55f,.42f,1.10f,7600,.44f,.06f,.26f,.05f,fm:.12f,pw2:.24f),
            97=> P(W.Triangle,W.Saw,.52f,.48f,-12,.180f,.80f,.58f,1.50f,3100,.52f,.02f,.30f,.08f,fm:.08f),
            98=> P(W.Sine,W.Pulse,.44f,.56f,24,.001f,.90f,.12f,1.60f,11000,.36f,.04f,.24f,.06f,fm:.22f,pw2:.18f),
            99=> P(W.Triangle,W.Saw,.50f,.50f,12,.240f,.90f,.62f,1.80f,5400,.50f,.02f,.34f,.10f,fm:.12f),
            100=>P(W.Saw,W.Sine,.60f,.40f,24,.001f,.40f,.24f,.70f,12500,.24f,.10f,.20f,.05f,sync:true),
            101=>P(W.Pulse,W.Saw,.52f,.48f,-12,.080f,.35f,.56f,.90f,2900,.58f,.04f,.28f,.12f,fm:.18f,pw1:.22f),
            102=>P(W.Sine,W.Pulse,.56f,.44f,12,.010f,.50f,.30f,1.30f,6800,.40f,.08f,.34f,.08f,fm:.14f,pw2:.28f),
            103=>P(W.Saw,W.Pulse,.54f,.46f,-12,.005f,.32f,.46f,1.10f,4300,.62f,.06f,.40f,.18f,fm:.28f,sync:true,pw2:.20f),

            // Ethnic
            104=>P(W.Saw,W.Triangle,.54f,.46f,12,.002f,.30f,.32f,.45f,5200,.28f,.62f,.04f,.05f),
            105=>P(W.Pulse,W.Triangle,.64f,.36f,12,.001f,.18f,.38f,.22f,4800,.20f,.58f,.02f,.04f,pw1:.34f),
            106=>P(W.Pulse,W.Sine,.62f,.38f,12,.001f,.16f,.34f,.20f,5600,.18f,.64f,.02f,.035f,pw1:.30f),
            107=>P(W.Triangle,W.Pulse,.70f,.30f,12,.001f,.22f,.40f,.28f,6000,.16f,.54f,.02f,.03f,pw2:.38f),
            108=>P(W.Sine,W.Triangle,.72f,.28f,12,.001f,.18f,.18f,.22f,7200,.10f,.70f,0,.02f),
            109=>P(W.Pulse,W.Saw,.58f,.42f,-12,.018f,.22f,.76f,.30f,4200,.26f,.20f,.04f,.04f,pw1:.38f),
            110=>P(W.Saw,W.Triangle,.60f,.40f,0,.012f,.18f,.74f,.26f,5100,.18f,.22f,.04f,.035f),
            111=>P(W.Pulse,W.Saw,.66f,.34f,12,.006f,.14f,.72f,.20f,6200,.22f,.30f,.04f,.045f,pw1:.36f),

            // Percussive melodic
            112=>P(W.Sine,W.Pulse,.68f,.32f,24,.001f,.65f,.06f,.80f,11000,.14f,.82f,.02f,.02f,pw2:.28f),
            113=>P(W.Sine,W.Triangle,.58f,.42f,12,.001f,.42f,.10f,.50f,9000,.12f,.76f,.02f,.02f),
            114=>P(W.Pulse,W.Sine,.56f,.44f,12,.001f,.50f,.16f,.65f,7600,.18f,.68f,.03f,.03f,pw1:.38f),
            115=>P(W.Pulse,W.Triangle,.78f,.22f,-12,.001f,.10f,.08f,.10f,4200,.12f,.92f,0,.02f,pw1:.24f),
            116=>P(W.Sine,W.Triangle,.74f,.26f,-12,.001f,.24f,.04f,.28f,3200,.22f,.96f,0,.04f),
            117=>P(W.Triangle,W.Pulse,.68f,.32f,-12,.001f,.18f,.08f,.22f,4600,.20f,.88f,0,.05f,pw2:.30f),
            118=>P(W.Pulse,W.Sine,.62f,.38f,-12,.001f,.16f,.05f,.18f,5200,.28f,.90f,0,.08f,pw1:.28f),
            119=>P(W.Saw,W.Pulse,.56f,.44f,-12,.001f,.80f,.04f,1.10f,9800,.44f,.70f,.04f,.12f,pw2:.20f),

            // Sound effects. Synthetic interpretations, intentionally strange.
            120=>P(W.Saw,W.Pulse,.46f,.54f,-24,.001f,.10f,.18f,.12f,2600,.58f,.88f,.02f,.16f,pw2:.16f),
            121=>P(W.Sine,W.Pulse,.38f,.62f,24,.001f,.25f,.20f,.35f,12500,.22f,.72f,.06f,.04f,fm:.16f,pw2:.18f),
            122=>P(W.Sine,W.Triangle,.60f,.40f,-12,.220f,.90f,.54f,1.80f,1800,.50f,.04f,.34f,.06f),
            123=>P(W.Sine,W.Pulse,.72f,.28f,24,.001f,.10f,.24f,.18f,10000,.10f,.80f,.02f,.02f,pw2:.22f),
            124=>P(W.Pulse,W.Sine,.70f,.30f,12,.001f,.08f,.62f,.10f,8500,.18f,.86f,.02f,.03f,pw1:.20f),
            125=>P(W.Saw,W.Pulse,.64f,.36f,-24,.010f,.20f,.72f,.25f,3400,.46f,.38f,.05f,.18f,fm:.10f,sync:true,pw2:.24f),
            126=>P(W.Pulse,W.Saw,.58f,.42f,-12,.001f,.30f,.18f,.40f,6200,.52f,.76f,.04f,.12f,pw1:.18f),
            _  =>P(W.Saw,W.Pulse,.76f,.24f,-24,.001f,.06f,.18f,.12f,7800,.62f,.96f,.01f,.30f,fm:.24f,sync:true,pw2:.16f),
        };
    }

    private static DsnLikePatch P(DsnOscillatorWave a, DsnOscillatorWave b, float la, float lb,
        float semi, float atk, float dec, float sus, float rel, float cutoff, float res,
        float envCut, float lfoCut, float drive, float fm = 0, bool sync = false,
        float pw1 = .5f, float pw2 = .5f) => new()
    {
        Osc1Wave = a, Osc2Wave = b,
        Osc1Level = MathF.Min(la, .72f), Osc2Level = MathF.Min(lb, .38f),
        Osc2Semitones = semi, PulseWidth1 = Math.Clamp(pw1, .18f, .82f), PulseWidth2 = Math.Clamp(pw2, .18f, .82f),
        AttackSeconds = atk, DecaySeconds = dec, SustainLevel = sus, ReleaseSeconds = rel,
        FilterMode = DsnFilterMode.LowPass, CutoffHz = cutoff, Resonance = MathF.Min(res, .48f),
        EnvelopeToCutoff = Math.Clamp(envCut, -.75f, .75f),
        LfoHz = 5f, LfoToPitch = 0f, LfoToCutoff = Math.Clamp(lfoCut, -.22f, .22f),
        LfoToPulseWidth = (a == W.Pulse || b == W.Pulse) ? Math.Clamp(lfoCut * .25f, -.12f, .12f) : 0f,
        Drive = MathF.Min(drive, .18f), FmAmount = MathF.Min(fm, .12f), HardSync = sync,
        OutputGain = .62f
    };

    private static class W
    {
        public const DsnOscillatorWave Sine = DsnOscillatorWave.Sine;
        public const DsnOscillatorWave Triangle = DsnOscillatorWave.Triangle;
        public const DsnOscillatorWave Saw = DsnOscillatorWave.Saw;
        public const DsnOscillatorWave Pulse = DsnOscillatorWave.Pulse;
    }
}
