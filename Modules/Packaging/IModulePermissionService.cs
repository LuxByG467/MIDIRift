namespace MIDIRift.Modules.Packaging;

/// <summary>
/// Permission policy boundary. A permission is meaningful only when the host
/// mediates the corresponding capability.
/// </summary>
public interface IModulePermissionService
{
    bool IsGranted(string moduleId, ModulePermission permission);
    IReadOnlyCollection<ModulePermission> GetGranted(string moduleId);
    void SetGrant(string moduleId, ModulePermission permission, bool granted);
    void RevokeAll(string moduleId);
}
