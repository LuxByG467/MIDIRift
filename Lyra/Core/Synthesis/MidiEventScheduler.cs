using MIDIRift.CleanRoom.Features.Midi;

namespace MIDIRift.CleanRoom.Features.Midi.Synthesis;

public sealed class MidiEventScheduler
{
    private IReadOnlyList<CompiledMidiEvent> _events = Array.Empty<CompiledMidiEvent>();
    private IReadOnlyList<CompiledEventBatch> _batches = Array.Empty<CompiledEventBatch>();
    private int _nextBatchIndex;

    public int NextBatchIndex => _nextBatchIndex;
    public bool HasPendingEvents => _nextBatchIndex < _batches.Count;

    public void Configure(CompiledSong song)
    {
        ArgumentNullException.ThrowIfNull(song);
        _events = song.Events;
        _batches = song.EventBatches;
        _nextBatchIndex = 0;
    }

    public void Reset()
    {
        _nextBatchIndex = 0;
    }

    public void RestoreNextBatchIndex(int nextBatchIndex)
    {
        if ((uint)nextBatchIndex > (uint)_batches.Count)
            throw new ArgumentOutOfRangeException(nameof(nextBatchIndex));
        _nextBatchIndex = nextBatchIndex;
    }

    public int FramesUntilNextBatch(long absoluteSample, int maximumFrames)
    {
        if (maximumFrames <= 0)
            return 0;
        if (_nextBatchIndex >= _batches.Count)
            return maximumFrames;

        long nextSample = _batches[_nextBatchIndex].SamplePosition;
        long distance = nextSample - absoluteSample;
        if (distance <= 0)
            return 0;
        if (distance >= maximumFrames)
            return maximumFrames;
        return (int)distance;
    }

    public void DispatchDueBatches(long absoluteSample, Action<CompiledMidiEvent> receiver)
    {
        ArgumentNullException.ThrowIfNull(receiver);

        while (_nextBatchIndex < _batches.Count)
        {
            CompiledEventBatch batch = _batches[_nextBatchIndex];
            if (batch.SamplePosition > absoluteSample)
                break;

            int endIndex = batch.StartIndex + batch.Count;
            for (int eventIndex = batch.StartIndex; eventIndex < endIndex; eventIndex++)
                receiver(_events[eventIndex]);

            _nextBatchIndex++;
        }
    }

    public void ReplayUntil(long targetSample, Action<CompiledMidiEvent> receiver)
    {
        Reset();
        DispatchDueBatches(targetSample, receiver);
    }
}
