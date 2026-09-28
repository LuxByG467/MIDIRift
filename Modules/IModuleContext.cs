namespace MIDIRift.Modules;

/// <summary>Narrow gateway through which modules obtain host capabilities.</summary>
public interface IModuleContext
{
    T GetCapability<T>() where T : class;
    bool TryGetCapability<T>(out T? capability) where T : class;
}
