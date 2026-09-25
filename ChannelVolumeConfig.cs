using System.Text.Json;

namespace MIDIRift;

/// <summary>Persiste multiplicadores de volumen por canal y por canción.</summary>
public sealed class ChannelVolumeConfig
{
    private readonly object _sync = new();
    private Dictionary<string, List<float>> _data = new();

    public ChannelVolumeConfig() => Load();

    public void ApplyTo(string trackId, List<Channel> channels)
    {
        lock (_sync)
        {
            if (!_data.TryGetValue(trackId, out var saved)) return;
            int count = Math.Min(channels.Count, saved.Count);
            for (int i = 0; i < count; i++)
                channels[i].UserGain = Math.Clamp(saved[i], 0f, 2f);
        }
    }

    /// <summary>Foundation-1: aplica ganancias al input común antes de crear el motor.</summary>
    public void ApplyTo(string trackId, ChiptuneEngineInput input)
    {
        lock (_sync)
        {
            if (!_data.TryGetValue(trackId, out var saved)) return;
            int count = Math.Min(input.Channels.Length, saved.Count);
            for (int i = 0; i < count; i++)
                input.Channels[i].UserGain = Math.Clamp(saved[i], 0f, 2f);
        }
    }

    public void Save(string trackId, IReadOnlyList<float> gains)
    {
        lock (_sync)
        {
            var copy = new List<float>(gains.Count);
            for (int i = 0; i < gains.Count; i++)
                copy.Add(Math.Clamp(gains[i], 0f, 2f));
            _data[trackId] = copy;
            Persist();
        }
    }

    public void Reset(string trackId, int channelCount)
    {
        var values = new float[Math.Max(0, channelCount)];
        Array.Fill(values, 1f);
        Save(trackId, values);
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(AppDataPaths.ChannelVolumes)) return;
            var loaded = JsonSerializer.Deserialize<Dictionary<string, List<float>>>(
                File.ReadAllText(AppDataPaths.ChannelVolumes));
            if (loaded == null) return;
            foreach (var key in loaded.Keys.ToArray())
            {
                var values = loaded[key];
                if (values == null)
                {
                    loaded[key] = new List<float>();
                    continue;
                }
                values.RemoveAll(v => float.IsNaN(v) || float.IsInfinity(v));
                for (int i = 0; i < values.Count; i++)
                    values[i] = Math.Clamp(values[i], 0f, 2f);
            }
            _data = loaded;
        }
        catch
        {
            FirstRunInitializer.Quarantine(AppDataPaths.ChannelVolumes);
            _data = new();
            Persist();
        }
    }

    private void Persist()
    {
        try
        {
            string json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
            FirstRunInitializer.AtomicWriteAllText(AppDataPaths.ChannelVolumes, json);
        }
        catch { }
    }
}
