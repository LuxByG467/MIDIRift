namespace MIDIRift.Modules.Packaging;

public sealed record ModulePermissionGrant(
    string ModuleId,
    ModulePermission Permission,
    bool Granted);
