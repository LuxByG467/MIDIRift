# DSN0.2.2.1b Cross-target compile fix

Fixes CS0103 for `engineBuild` in `MainPage.xaml.cs` when Visual Studio evaluates
the iOS, MacCatalyst and Windows target frameworks.

`engineBuild`, `engine`, and `model` are now declared in common scope before the
`#if ANDROID` block. Android still performs the actual engine construction.
Non-Android targets retain the existing `PlatformNotSupportedException`; no
runtime behavior was added or changed for those platforms.

No DSP, persistence, First Run, patch-bank, or Android playback behavior changed.
