# Foundation-3 — Common Playback Contract

Foundation-3 makes Classic and Lyra real implementations of the same Android
playback construction path.

## Architecture

```
MidiCompiler
    |
CompiledMidiSong
    |
ChiptuneEngineInput
    |
ChiptunePlayerFactory
    |------------------------|
    |                        |
Classic                  Lyra
LegacyMidiAdapter        LyraCompiledSongAdapter
ChiptuneAudioTrack       LyraAudioTrackPlayer
    |                        |
    -------- IChiptunePlayer -
```

`MainPage` does not know which concrete engine it receives.

## Selection

`ChiptuneEngineSelection.DefaultEngine` controls the factory.

Foundation-3 deliberately defaults to:

```csharp
ChiptuneEngineKind.Classic
```

For a developer test of Lyra:

```csharp
ChiptuneEngineSelection.DefaultEngine = ChiptuneEngineKind.Lyra;
```

Foundation-4 is responsible for exposing this as a persistent product setting.

## Lyra transport in this phase

Lyra now has a real Android `IChiptunePlayer` implementation:

- AudioTrack output
- producer/writer ring
- play/pause/reset/seek
- per-channel WaveType and gain
- GraphicEqualizer
- Bass Restoration
- Spectrum/Oscilloscope capture via IPanelAudioSource

The synthesis core is the Foundation-2 Lyra core and does not parse the MIDI
again.

## Known intentional limitation

`Speed` is part of the common contract and is stored by Lyra, but variable-speed
rendering is not promoted in Foundation-3. Classic remains the default engine,
so this does not regress existing product behaviour.

The later native/AAudio RT transport can replace AudioTrack under Lyra without
changing MainPage or the common engine contract.
