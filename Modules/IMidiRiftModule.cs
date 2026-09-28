namespace MIDIRift.Modules;

/// <summary>Root lifecycle contract for every MIDIRift module.</summary>
public interface IMidiRiftModule : IDisposable
{
    ModuleDescriptor Descriptor { get; }
    void Initialize(IModuleContext context);
}
