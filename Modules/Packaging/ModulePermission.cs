namespace MIDIRift.Modules.Packaging;

/// <summary>
/// Host-mediated capabilities a third-party module may request.
/// Stage H defines policy only; it does not grant arbitrary CLR access.
/// </summary>
public enum ModulePermission
{
    AudioAnalysis,
    PlaybackRead,
    PlaybackControl,
    LibraryRead,
    LibraryWrite,
    SettingsRead,
    SettingsWrite,
    Network,
    FileRead,
    FileWrite
}
