namespace MIDIRift.Modules.Packaging;

public sealed record ModuleRepositoryEntry(
    string PackageId,
    string Name,
    string Version,
    string DownloadUri,
    string Sha256,
    string? Signature,
    string? Source,
    ModuleTrustLevel Trust);
