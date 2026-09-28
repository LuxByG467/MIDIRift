namespace MIDIRift.Modules.Packaging;

/// <summary>
/// Future execution boundary for untrusted executable modules.
/// Stage H defines the contract but intentionally ships no permissive implementation.
/// </summary>
public interface IModuleSandbox
{
    bool SupportsExecutableModules { get; }
    bool CanRun(ModuleManifest manifest);
}
