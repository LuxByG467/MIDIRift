using System;
using System.Collections.Generic;

namespace MIDIRift;

/// <summary>
/// Modelo de datos de la canción.
///
/// Internamente usa arrays en lugar de List anidadas:
///   - _steps[canal][step] = MidiStep struct
///   - Sin boxing, sin resizing, sin GC pressure durante playback
/// </summary>
public class TrackerModel
{
    // ── Datos ─────────────────────────────────────────────────────────────
    // Array jagged: _steps[canal] es un MidiStep[] contiguo en memoria.
    // Acceso O(1), sin overhead de List<T>.
    private readonly MidiStep[][] _steps;

    public int ChannelCount => _steps.Length;

    /// <summary>Número de steps en el canal dado.</summary>
    public int StepCount(int channel) => _steps[channel].Length;

    /// <summary>Acceso directo a un step. Hot path — sin bounds check extra.</summary>
    public ref MidiStep GetStep(int channel, int stepIndex) =>
        ref _steps[channel][stepIndex];

    /// <summary>Span de todos los steps de un canal — para iterar sin indexación.</summary>
    public ReadOnlySpan<MidiStep> GetTrack(int channel) =>
        _steps[channel].AsSpan();

    // ── Constructores ─────────────────────────────────────────────────────

    /// <summary>
    /// Constructor principal: toma arrays ya construidos.
    /// El parser/importer debe construir MidiStep[] por canal y pasarlos aquí.
    /// </summary>
    public TrackerModel(MidiStep[][] steps)
    {
        _steps = steps ?? throw new ArgumentNullException(nameof(steps));
    }

    /// <summary>
    /// Compatibilidad con código existente que pasa List&lt;List&lt;MidiStep&gt;&gt;.
    /// Convierte a arrays internamente — se llama solo al cargar la canción, no durante playback.
    /// </summary>
    public TrackerModel(List<List<MidiStep>> tracks)
    {
        _steps = new MidiStep[tracks.Count][];
        for (int i = 0; i < tracks.Count; i++)
        {
            var src = tracks[i];
            var arr = new MidiStep[src.Count];
            for (int j = 0; j < src.Count; j++)
                arr[j] = src[j];
            _steps[i] = arr;
        }
    }

    /// <summary>
    /// Convenience: construye desde los canales del engine.
    /// </summary>
    public static TrackerModel FromChannels(IEnumerable<Channel> channels)
    {
        var list = new List<MidiStep[]>();
        foreach (var ch in channels)
        {
            var track = ch.Track;
            var arr = new MidiStep[track.Count];
            for (int i = 0; i < track.Count; i++)
                arr[i] = track[i];
            list.Add(arr);
        }
        return new TrackerModel(list.ToArray());
    }

    // ── Helpers para TrackerPlayer.BuildGrid ──────────────────────────────

    /// <summary>
    /// Calcula el tiempo de inicio en ms de cada step para un canal.
    /// Devuelve un array pre-calculado — se llama una vez en BuildGrid.
    /// </summary>
    public int[] BuildStartTimesMs(int channel)
    {
        var track = _steps[channel];
        var result = new int[track.Length];
        int t = 0;
        for (int i = 0; i < track.Length; i++)
        {
            result[i] = t;
            t += (int)track[i].DurationMs;
        }
        return result;
    }

    /// <summary>
    /// Duración total de un canal en ms.
    /// </summary>
    public int TotalDurationMs(int channel)
    {
        var track = _steps[channel];
        int t = 0;
        for (int i = 0; i < track.Length; i++)
            t += (int)track[i].DurationMs;
        return t;
    }
}