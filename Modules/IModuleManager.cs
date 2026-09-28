namespace MIDIRift.Modules;

/// <summary>Owns the lifecycle of explicitly registered internal modules.</summary>
public interface IModuleManager : IDisposable
{
    IModuleRegistry Registry { get; }
    bool IsInitialized { get; }
    void Register(IMidiRiftModule module);
    void InitializeAll();
}
