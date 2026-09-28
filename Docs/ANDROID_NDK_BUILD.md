# Android NDK and the MIDIRift native AAudio backend

MIDIRift's Lyra audio path can use a small native real-time backend based on
AAudio. Its C++ source lives in `Platforms/Android/Native/jni/`, and the project
contains an MSBuild target (`BuildMIDIRiftNativeAudio`) that invokes
`ndk-build` directly before the Android build.

## Why an older MIDIRift source tree used `C:\AndroidNDK`

During development we needed the custom MSBuild target to find `ndk-build.cmd`
reliably. The .NET/MAUI Android workload can know about an installed NDK while
a custom `Exec` target still does not have a usable `ndk-build` path. MIDIRift
therefore used `C:\AndroidNDK` as an explicit development-machine workaround.
It solved the native-backend build, but it was intentionally machine-specific
and is not suitable as the only path in a public repository.

The public project keeps `C:\AndroidNDK` only as a **last-resort compatibility
fallback**. It first accepts an explicit MSBuild property and then the common
Android NDK environment variables.

## Recommended setup

Set one of these before building:

- MSBuild property: `MIDIRiftNdkDirectory`
- Environment variable: `ANDROID_NDK_HOME`
- Environment variable: `ANDROID_NDK_ROOT`

Example with an explicit MSBuild property on Windows:

```powershell
dotnet build MIDIRift.Android.csproj -f net10.0-android -p:MIDIRiftNdkDirectory="C:\path\to\Android\Sdk\ndk\<version>"
```

Or set an environment variable and build normally:

```powershell
$env:ANDROID_NDK_HOME = "C:\path\to\Android\Sdk\ndk\<version>"
dotnet build MIDIRift.Android.csproj -f net10.0-android
```

On Linux/macOS, point the same variable/property at the NDK directory that
contains the `ndk-build` executable.

## If the build says that Android NDK / `ndk-build` was not found

1. Confirm that the Android NDK is installed through Visual Studio/Android SDK
   tooling or another supported Android SDK installation.
2. Locate the NDK directory and verify that it contains `ndk-build.cmd`
   (Windows) or `ndk-build` (Linux/macOS).
3. Set `MIDIRiftNdkDirectory`, `ANDROID_NDK_HOME`, or `ANDROID_NDK_ROOT` to that
   directory.
4. Rebuild the Android target.
5. As a compatibility option for the original development layout, placing the
   NDK at `C:\AndroidNDK` still works on Windows.

The prebuilt `libmidirift_rt_audio.so` files under
`Platforms/Android/Native/libs/` are generated from the C++ source included in
the repository. They are intentionally kept in source control so the origin of
the native backend is auditable and the project retains the known-good native
artifacts from this alpha snapshot.
