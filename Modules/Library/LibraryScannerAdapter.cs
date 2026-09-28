namespace MIDIRift.Modules.Library;
public sealed class LibraryScannerAdapter : ILibraryScanner
{
    private readonly PlaylistController _controller;
    public LibraryScannerAdapter(PlaylistController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _controller.ScanProgressChanged += () => ScanProgressChanged?.Invoke();
    }
    public bool IsScanning => _controller.IsScanning;
    public int ScanFound => _controller.ScanFound;
    public bool HasScanned => _controller.HasScanned;
    public string? LastScanSummary => _controller.LastScanSummary;
    public string ScanCurrentFolder => _controller.ScanCurrentFolder;
    public int ScanDirsVisited => _controller.ScanDirsVisited;
    public TimeSpan ScanElapsed => _controller.ScanElapsed;
    public event Action? ScanProgressChanged;
    public void StartScan() => _controller.StartScan();
}
