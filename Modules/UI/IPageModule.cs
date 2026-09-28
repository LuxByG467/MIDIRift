using Microsoft.Maui.Controls;

namespace MIDIRift.Modules.UI;

/// <summary>Factory boundary for a navigable MIDIRift page.</summary>
public interface IPageModule : IMidiRiftModule
{
    Page CreatePage();
}
