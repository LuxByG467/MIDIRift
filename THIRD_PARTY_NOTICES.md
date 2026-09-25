# Third-party software notices

MIDIRift is licensed under the GNU General Public License v3.0 (GPL-3.0).
The project also depends on third-party libraries distributed under their own licenses.
Those licenses apply to the corresponding third-party components and do not replace
MIDIRift's GPL-3.0 license.

This file documents the direct NuGet dependencies declared by the Android project at
the time of the MIDIRift Android 0.14.0 / DSN 0.2.23.1 source release.

## NAudio

Packages used:
- NAudio 3.0.0-preview.18
- NAudio.Core 3.0.0-preview.18
- NAudio.Midi 3.0.0-preview.18

License: MIT License.
Project: https://github.com/naudio/NAudio

NAudio's project documentation identifies NAudio as MIT-licensed. MIDIRift consumes
these packages as NuGet dependencies and does not relicense NAudio itself.

## OpenTK

Packages used:
- OpenTK 5.0.0-pre.16
- OpenTK.Core 5.0.0-pre.16
- OpenTK.Audio.OpenAL 5.0.0-pre.14

License: MIT License (also described by the project as MIT/X11).
Project: https://github.com/opentk/opentk

OpenTK's license file requires preservation of its copyright and permission notice in
copies or substantial portions of OpenTK. OpenTK also maintains its own third-party
notices; downstream redistributors should preserve any notices included with the
OpenTK packages they redistribute.

## SkiaSharp

Packages used:
- SkiaSharp 4.151.0-rc.1.1
- SkiaSharp.NativeAssets.Android 4.151.0-rc.1.1
- SkiaSharp.Views 4.151.0-rc.1.1
- SkiaSharp.Views.Maui.Controls 4.151.0-rc.1.1
- SkiaSharp.Views.Maui.Core 4.151.0-rc.1.1

License: MIT License.
Project: https://github.com/mono/SkiaSharp

SkiaSharp is a .NET binding around Google's Skia graphics library and its packages can
include native components and their associated notices. When distributing compiled
MIDIRift binaries, preserve the license/notices shipped with the exact NuGet packages
and native assets used by the build.

## AndroidX / Xamarin.AndroidX bindings

Packages used:
- Xamarin.AndroidX.Core 1.19.0.1
- Xamarin.AndroidX.Core.Core.Ktx 1.19.0.1
- Xamarin.AndroidX.Media 1.8.0.1

Primary upstream AndroidX license: Apache License 2.0.
Upstream project: https://github.com/androidx/androidx

The Xamarin.AndroidX NuGet packages are Microsoft/.NET bindings for AndroidX artifacts.
NuGet metadata for these bindings reports MIT and Apache-2.0 licensing, while the
underlying AndroidX project is Apache-2.0 licensed. Preserve the notices and license
metadata supplied by the exact NuGet packages and their upstream AndroidX artifacts
when redistributing binaries.

## Transitive dependencies

NuGet may restore additional transitive dependencies that are not listed above. Their
licenses remain their respective owners' licenses. Before publishing a compiled APK,
AAB, or other binary release, generate or inspect the resolved dependency graph for
that exact release and preserve all license/NOTICE files required by those packages.

This notice is informational and is not a substitute for the license text included by
each third-party project or package.
