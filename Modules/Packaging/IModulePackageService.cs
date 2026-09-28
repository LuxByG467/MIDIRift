namespace MIDIRift.Modules.Packaging;

/// <summary>
/// Inspects packages as data. Stage H intentionally provides no executable
/// module loader.
/// </summary>
public interface IModulePackageService
{
    ModulePackageInspection Inspect(string packagePath);
}
