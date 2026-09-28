using Microsoft.Maui.Controls;

namespace MIDIRift.Modules.UI;

/// <summary>
/// Factory boundary for a visual surface. Specialized data contracts
/// (tracker timeline, telemetry, mixer control) are intentionally deferred to G.
/// </summary>
public interface IVisualizerModule : IMidiRiftModule
{
    View CreateView();
}
