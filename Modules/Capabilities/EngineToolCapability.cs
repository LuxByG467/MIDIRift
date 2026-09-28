namespace MIDIRift.Modules.Capabilities;

[Flags]
public enum EngineToolCapability
{
    None = 0,
    WaveTypeSelector = 1 << 0,
    PatchEditor = 1 << 1,
    PatchBank = 1 << 2,
    DualVco = 1 << 3,
    Fm = 1 << 4,
    HardSync = 1 << 5,
    DedicatedDrums = 1 << 6,
}

public interface IEngineCapabilityProvider
{
    EngineToolCapability ToolCapabilities { get; }
    bool Supports(EngineToolCapability capability) => (ToolCapabilities & capability) == capability;
}

public sealed class StaticEngineCapabilityProvider : IEngineCapabilityProvider
{
    public StaticEngineCapabilityProvider(EngineToolCapability capabilities) => ToolCapabilities = capabilities;
    public EngineToolCapability ToolCapabilities { get; }
}
