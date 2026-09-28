namespace MIDIRift;

public static partial class Mp3PlayerFactory
{
    public static IMp3Player Create(string path) => new Mp3AudioPlayer(path);
}
