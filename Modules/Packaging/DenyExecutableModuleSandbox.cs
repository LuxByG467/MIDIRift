namespace MIDIRift.Modules.Packaging;

/// <summary>
/// Secure Stage-H default: executable third-party modules cannot run.
/// Declarative resources can be handled separately by trusted host code.
/// </summary>
public sealed class DenyExecutableModuleSandbox : IModuleSandbox
{
    public bool SupportsExecutableModules => false;
    public bool CanRun(ModuleManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return false;
    }
}
