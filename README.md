# MIDIRift

MIDIRift is an experimental MIDI player and synthesizer focused on custom
real-time synthesis engines, visualization, playback analysis, and an
architecture that can evolve independently from a conventional SoundFont-based
player.

This repository snapshot contains **MIDIRift Android 0.14.0** with
**DSN 0.2.23.1 alpha (FirstRunUiStateHotfix)**. The project is still alpha
software: interfaces, engine behavior, storage layout, and build requirements
may change between versions.

## Current source snapshot

- Application: MIDIRift Android 0.14.0 alpha
- DSN engine: 0.2.23.1 alpha
- UI: .NET MAUI
- Android audio: managed AudioTrack path plus Lyra native AAudio RT backend
- Native backend source: `Platforms/Android/Native/jni/`
- Technical/development notes: `Docs/`

MIDIRift has separate application and engine versioning. The detailed runtime
version label is defined in the source and may be more specific than Android's
package/display version metadata.

## Building Android

The project targets .NET 10 / .NET MAUI and requires the corresponding Android
workload. The Lyra AAudio backend also has a custom native build step that calls
Android NDK `ndk-build` directly.

If the build cannot find the NDK, read
[`Docs/ANDROID_NDK_BUILD.md`](Docs/ANDROID_NDK_BUILD.md). That document also
explains why older development snapshots used the fixed `C:\AndroidNDK` path
and how the public source tree makes that path configurable.

## Documentation

`Docs/` intentionally contains detailed notes from many development milestones.
They are useful for understanding implementation decisions and regressions, but
not every document describes the latest state. Start with
[`Docs/README.md`](Docs/README.md).

## Third-party software

MIDIRift references third-party packages including NAudio, OpenTK, SkiaSharp,
and AndroidX packages. Those projects remain subject to their respective
licenses. A dedicated third-party license inventory should be kept up to date as
MIDIRift's dependencies evolve.

## License

MIDIRift source code is released under the **GNU General Public License v3.0**.
See [`LICENSE`](LICENSE) for the complete license text.

## Third-party software

MIDIRift uses third-party open-source libraries under their own licenses. Direct
NuGet dependencies and redistribution notes are documented in
[`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md). When producing binary releases,
also preserve license and NOTICE material supplied by the exact restored packages and
their transitive dependencies.
