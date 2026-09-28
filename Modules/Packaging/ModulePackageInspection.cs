namespace MIDIRift.Modules.Packaging;

public sealed record ModulePackageInspection(
    ModuleManifest Manifest,
    IReadOnlyCollection<ModulePermission> EffectivePermissions,
    bool HashMatches,
    bool HasSignature,
    bool ContainsExecutablePayload,
    IReadOnlyList<string> Warnings)
{
    public bool CanInstallDeclarative =>
        HashMatches && !ContainsExecutablePayload && Warnings.Count == 0;
}
