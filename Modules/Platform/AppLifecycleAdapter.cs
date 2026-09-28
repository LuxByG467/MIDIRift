namespace MIDIRift.Modules.Platform;

/// <summary>Compatibility adapter over the existing AppLifecycleState static.</summary>
public sealed class AppLifecycleAdapter : IAppLifecycle, IDisposable
{
    public AppLifecycleAdapter() => AppLifecycleState.Changed += OnChanged;
    public bool IsInBackground => AppLifecycleState.IsInBackground;
    public event Action<bool>? BackgroundStateChanged;
    private void OnChanged(bool value) => BackgroundStateChanged?.Invoke(value);
    public void Dispose() => AppLifecycleState.Changed -= OnChanged;
}
