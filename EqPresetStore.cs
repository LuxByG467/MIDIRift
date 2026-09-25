using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MIDIRift;

/// <summary>
/// Guarda/carga/borra presets PERSONALIZADOS del ecualizador, más la
/// configuración actual (10 ganancias) para que sobreviva a cerrar la app
/// — a diferencia de la versión Desktop, donde _eqGains solo vive en
/// memoria durante la sesión (ver MainWindow.axaml.cs). Mismo patrón de
/// persistencia que <see cref="WaveTypeConfig"/>: JSON en
/// AppData/MIDIRift/config, try/catch silencioso (un fallo de disco no
/// debe tumbar la app ni el ecualizador).
/// Vive en MainPage y se carga una sola vez al construirse.
/// </summary>
public class EqPresetStore
{
    private static readonly string SaveDir = AppDataPaths.Config;
    private static readonly string PresetsPath = AppDataPaths.EqPresets;

    private class EqConfigData
    {
        public List<EqPreset> Custom { get; set; } = new();
        public float[] CurrentGains { get; set; } = new float[EqualizerBands.Count];
        public float CurrentPreampDb { get; set; } = 0f;
    }

    public List<EqPreset> Custom { get; private set; } = new();

    /// <summary>Últimas ganancias activas guardadas (para restaurar al reabrir la app).</summary>
    public float[] CurrentGains { get; private set; } = new float[EqualizerBands.Count];

    /// <summary>Último preamp activo guardado (para restaurar al reabrir la app).</summary>
    public float CurrentPreampDb { get; private set; } = 0f;

    /// <summary>Se dispara tras guardar o borrar un preset personalizado (para refrescar el combo en EqualizerPage).</summary>
    public event Action? CustomPresetsChanged;

    public EqPresetStore() => Load();

    /// <summary>Crea o sobrescribe (por nombre, sin distinguir mayúsculas) un preset personalizado.</summary>
    public void SaveCustomPreset(string name, ReadOnlySpan<float> gainsDb, float preampDb = 0f)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) return;

        var gains = gainsDb.ToArray();
        int existing = Custom.FindIndex(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        if (existing >= 0)
            Custom[existing] = new EqPreset(name, gains, preampDb);
        else
            Custom.Add(new EqPreset(name, gains, preampDb));

        Persist();
        CustomPresetsChanged?.Invoke();
    }

    public void DeleteCustomPreset(string name)
    {
        int removed = Custom.RemoveAll(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        if (removed > 0)
        {
            Persist();
            CustomPresetsChanged?.Invoke();
        }
    }

    /// <summary>Guarda la configuración actual del ecualizador (se llama al cambiar cualquier banda).</summary>
    public void SaveCurrentGains(ReadOnlySpan<float> gainsDb)
    {
        int n = Math.Min(CurrentGains.Length, gainsDb.Length);
        for (int i = 0; i < n; i++) CurrentGains[i] = gainsDb[i];
        Persist();
    }

    /// <summary>Guarda el preamp activo (se llama al mover el slider de preamp).</summary>
    public void SaveCurrentPreamp(float preampDb)
    {
        CurrentPreampDb = preampDb;
        Persist();
    }

    private void Persist()
    {
        try
        {
            Directory.CreateDirectory(SaveDir);
            var data = new EqConfigData { Custom = Custom, CurrentGains = CurrentGains, CurrentPreampDb = CurrentPreampDb };
            var json = JsonSerializer.Serialize(data,
                new JsonSerializerOptions { WriteIndented = true });
            FirstRunInitializer.AtomicWriteAllText(PresetsPath, json);
        }
        catch { }
    }

    private void Load()
    {
        EqConfigData PersistedDefault()
        {
            var d = new EqConfigData();
            try
            {
                var json = JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = true });
                FirstRunInitializer.AtomicWriteAllText(PresetsPath, json);
            }
            catch { }
            return d;
        }

        var data = FirstRunInitializer.LoadJsonOrDefault(
            PresetsPath,
            () => JsonSerializer.Deserialize<EqConfigData>(File.ReadAllText(PresetsPath)),
            () => new EqConfigData(),
            d => d.Custom != null && d.CurrentGains != null &&
                 d.CurrentGains.Length == EqualizerBands.Count &&
                 d.CurrentGains.All(float.IsFinite) &&
                 float.IsFinite(d.CurrentPreampDb),
            _ => PersistedDefault());

        Custom = data.Custom ?? new();
        CurrentPreampDb = float.IsFinite(data.CurrentPreampDb) ? data.CurrentPreampDb : 0f;
        CurrentGains = new float[EqualizerBands.Count];
        int n = Math.Min(CurrentGains.Length, data.CurrentGains?.Length ?? 0);
        for (int i = 0; i < n; i++)
            CurrentGains[i] = float.IsFinite(data.CurrentGains![i]) ? data.CurrentGains[i] : 0f;
    }

}
