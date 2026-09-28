using LyraSong = MIDIRift.CleanRoom.Features.Midi.CompiledSong;
using LyraChannelState = MIDIRift.CleanRoom.Features.Midi.Synthesis.ChannelState;
using LyraVoicePool = MIDIRift.CleanRoom.Features.Midi.Synthesis.VoicePool;
using LyraScheduler = MIDIRift.CleanRoom.Features.Midi.Synthesis.MidiEventScheduler;

namespace MIDIRift;

/// <summary>
/// Contenedor de Foundation-2. Demuestra que el core de Lyra nace únicamente
/// desde ChiptuneEngineInput. Aún no posee el transporte ni el backend de audio.
/// </summary>
public sealed class LyraCoreSession
{
    public LyraSong Song { get; }
    public LyraChannelState[] Channels { get; }
    public LyraVoicePool Voices { get; }
    public LyraScheduler Scheduler { get; }

    internal LyraCoreSession(
        LyraSong song,
        LyraChannelState[] channels,
        LyraVoicePool voices,
        LyraScheduler scheduler)
    {
        Song = song;
        Channels = channels;
        Voices = voices;
        Scheduler = scheduler;
    }
}

public static class LyraCoreFactory
{
    public static LyraCoreSession Prepare(ChiptuneEngineInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        LyraSong song = LyraCompiledSongAdapter.Build(input);

        var channels = new LyraChannelState[song.Channels.Count];
        for (int i = 0; i < channels.Length; i++)
        {
            channels[i] = new LyraChannelState(song.Channels[i]);

            if (i < input.Channels.Length)
            {
                channels[i].SetWaveType(
                    LyraWaveTypeBridge.ToLyra(input.Channels[i].WaveType));
                channels[i].SetUserMix(
                    input.Channels[i].UserGain,
                    muted: false,
                    solo: false);
            }
        }

        var voices = new LyraVoicePool(96);
        voices.ConfigureSampleRate(LyraSong.DefaultSampleRate);

        var scheduler = new LyraScheduler();
        scheduler.Configure(song);

        return new LyraCoreSession(song, channels, voices, scheduler);
    }
}

public static class LyraWaveTypeBridge
{
    public static MIDIRift.CleanRoom.Features.Midi.ChiptuneWaveType ToLyra(WaveType waveType)
    {
        return waveType switch
        {
            WaveType.Square => MIDIRift.CleanRoom.Features.Midi.ChiptuneWaveType.Pulse50,
            WaveType.Pulse25 => MIDIRift.CleanRoom.Features.Midi.ChiptuneWaveType.Pulse25,
            WaveType.Pulse12 => MIDIRift.CleanRoom.Features.Midi.ChiptuneWaveType.Pulse25,
            WaveType.Triangle => MIDIRift.CleanRoom.Features.Midi.ChiptuneWaveType.Triangle,
            WaveType.Saw => MIDIRift.CleanRoom.Features.Midi.ChiptuneWaveType.Saw,
            WaveType.Sine => MIDIRift.CleanRoom.Features.Midi.ChiptuneWaveType.Sine,
            WaveType.Noise => MIDIRift.CleanRoom.Features.Midi.ChiptuneWaveType.Noise,
            WaveType.WhiteNoise => MIDIRift.CleanRoom.Features.Midi.ChiptuneWaveType.Noise,
            WaveType.ChipDrums => MIDIRift.CleanRoom.Features.Midi.ChiptuneWaveType.Noise,
            _ => MIDIRift.CleanRoom.Features.Midi.ChiptuneWaveType.Pulse50
        };
    }

    /// <summary>
    /// Proyecta el estado REAL de Lyra al enum común que consume la UI.
    /// Lyra tiene menos variantes que Legacy, así que la conversión devuelve
    /// la representación canónica de lo que realmente se está sintetizando.
    /// </summary>
    public static WaveType ToCommon(
        MIDIRift.CleanRoom.Features.Midi.ChiptuneWaveType waveType)
    {
        return waveType switch
        {
            MIDIRift.CleanRoom.Features.Midi.ChiptuneWaveType.Pulse50 => WaveType.Square,
            MIDIRift.CleanRoom.Features.Midi.ChiptuneWaveType.Pulse25 => WaveType.Pulse25,
            MIDIRift.CleanRoom.Features.Midi.ChiptuneWaveType.Sine => WaveType.Sine,
            MIDIRift.CleanRoom.Features.Midi.ChiptuneWaveType.Triangle => WaveType.Triangle,
            MIDIRift.CleanRoom.Features.Midi.ChiptuneWaveType.Saw => WaveType.Saw,
            MIDIRift.CleanRoom.Features.Midi.ChiptuneWaveType.Noise => WaveType.Noise,
            _ => WaveType.Square
        };
    }
}
