# EQ staged-load compile fix

Fixes C# name resolution collisions introduced by the staged EQ helpers `Button(...)` and `Label(...)`. Inside those helper methods, references to MAUI bindable properties are now fully qualified as `Microsoft.Maui.Controls.Button.*` and `Microsoft.Maui.Controls.Label.*`. No DSP or runtime behavior changed.
