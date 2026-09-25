using System.Text.Json;
using System.Text.Json.Serialization;

namespace MIDIRift;

/// <summary>
/// Persiste la configuración de WaveType por canal para cada canción,
/// indexada por LibraryTrack.Id.
/// Se guarda en AppData/MIDIRift/wavetypes.json.
/// </summary>
public class WaveTypeConfig
{
    // ── Persistencia ──────────────────────────────────────────────────────
    private static readonly string SavePath = AppDataPaths.WaveTypes;

    // trackId → lista de WaveType por índice de canal
    private Dictionary<string, List<WaveType>> _data = new();

    // ── Constructor ───────────────────────────────────────────────────────

    public WaveTypeConfig()
    {
        Load();
    }

    // ── API pública ───────────────────────────────────────────────────────

    /// <summary>
    /// Devuelve la configuración guardada para un track, o null si no hay ninguna.
    /// </summary>
    public List<WaveType>? Get(string trackId)
    {
        _data.TryGetValue(trackId, out var result);
        return result;
    }

    /// <summary>
    /// Aplica la configuración guardada sobre una lista de canales ya construida.
    /// Solo sobreescribe los índices que existan en ambas listas.
    /// </summary>
    public void ApplyTo(string trackId, List<Channel> channels)
    {
        if (!_data.TryGetValue(trackId, out var saved)) return;

        for (int i = 0; i < Math.Min(channels.Count, saved.Count); i++)
            channels[i].WaveType = saved[i];
    }

    /// <summary>Foundation-1: aplica timbres al input común antes de crear el motor.</summary>
    public void ApplyTo(string trackId, ChiptuneEngineInput input)
    {
        if (!_data.TryGetValue(trackId, out var saved)) return;
        int count = Math.Min(input.Channels.Length, saved.Count);
        for (int i = 0; i < count; i++)
            input.Channels[i].WaveType = saved[i];
    }

    /// <summary>
    /// Guarda la configuración actual de wave types para un track.
    /// Se llama cada vez que el usuario cambia un wave type.
    /// </summary>
    public void Save(string trackId, List<WaveType> waveTypes)
    {
        _data[trackId] = new List<WaveType>(waveTypes);
        Persist();
    }

    // ── Persistencia ──────────────────────────────────────────────────────

    private void Load()
    {
        var raw = FirstRunInitializer.LoadJsonOrDefault(
            SavePath,
            () => JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(SavePath)),
            () => new Dictionary<string, List<string>>(),
            d => d != null && d.All(x => x.Key != null && x.Value != null),
            d => FirstRunInitializer.AtomicWriteAllText(
                SavePath,
                JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = true })));

        _data.Clear();
        foreach (var (id, names) in raw)
        {
            var types = new List<WaveType>();
            foreach (var name in names)
                if (Enum.TryParse<WaveType>(name, out var wt))
                    types.Add(wt);
            _data[id] = types;
        }
    }

    private void Persist()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SavePath)!);

            // Serializar como strings para que el JSON sea legible
            var raw = new Dictionary<string, List<string>>();
            foreach (var (id, types) in _data)
                raw[id] = types.ConvertAll(t => t.ToString());

            var json = JsonSerializer.Serialize(raw,
                new JsonSerializerOptions { WriteIndented = true });
            FirstRunInitializer.AtomicWriteAllText(SavePath, json);
        }
        catch { }
    }
}