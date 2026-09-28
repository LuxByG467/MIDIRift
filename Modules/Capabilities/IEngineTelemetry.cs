namespace MIDIRift.Modules.Capabilities;

public interface IEngineTelemetry
{
    EngineTelemetrySnapshot Snapshot { get; }
}
