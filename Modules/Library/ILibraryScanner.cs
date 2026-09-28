namespace MIDIRift.Modules.Library;
public interface ILibraryScanner
{
    bool IsScanning { get; }
    int ScanFound { get; }
    bool HasScanned { get; }
    string? LastScanSummary { get; }
    string ScanCurrentFolder { get; }
    int ScanDirsVisited { get; }
    TimeSpan ScanElapsed { get; }
    event Action? ScanProgressChanged;
    void StartScan();
}
