namespace MIDIRift.Modules;

/// <summary>Registry of module identities. Registration does not initialize a module.</summary>
public interface IModuleRegistry
{
    IReadOnlyCollection<IMidiRiftModule> Modules { get; }
    void Register(IMidiRiftModule module);
    bool TryGet(string moduleId, out IMidiRiftModule? module);
    TModule? Get<TModule>(string moduleId) where TModule : class, IMidiRiftModule;
}
