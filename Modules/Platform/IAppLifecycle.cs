namespace MIDIRift.Modules.Platform;

public interface IAppLifecycle
{
    bool IsInBackground { get; }
    bool IsForeground => !IsInBackground;
    event Action<bool>? BackgroundStateChanged;
}
