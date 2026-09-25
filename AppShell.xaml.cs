using MIDIRift.Modules;
using MIDIRift.Modules.UI;
using MIDIRift.Modules.Library;
namespace MIDIRift
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

            // Una sola instancia de cada uno, compartida entre las dos
            // pestañas — ver PlaybackBridge.cs para el porqué. Si cada
            // página creara su propio PlaylistController, Library y
            // LibraryPage terminarían pisándose los mismos
            // playlists.json/library.json por separado.
            var playlistController = new PlaylistController();
            var playbackBridge = new PlaybackBridge();

            // Stage E capability boundaries share the proven controller state.
            var playbackQueue = new PlaybackQueueAdapter(playlistController);
            var libraryService = new LibraryServiceAdapter(playlistController);
            var playlistService = new PlaylistServiceAdapter(playlistController);
            var libraryScanner = new LibraryScannerAdapter(playlistController);
            _ = libraryService;
            _ = playlistService;
            _ = libraryScanner;

            // Stage F: internal UI modules. Still compiled and trusted; no external
            // assembly discovery or arbitrary code loading is introduced.
            var uiModuleContext = new ModuleContext();
            var uiModuleRegistry = new ModuleRegistry();
            var uiModuleManager = new ModuleManager(uiModuleRegistry, uiModuleContext);

#if ANDROID
            uiModuleManager.Register(new BuiltInVisualizerModule(
                BuiltInUiModuleIds.Tracker, "Tracker", () => new TrackerPanel()));
            uiModuleManager.Register(new BuiltInVisualizerModule(
                BuiltInUiModuleIds.Spectrum, "Spectrum", () => new SpectrumPanel()));
            uiModuleManager.Register(new BuiltInVisualizerModule(
                BuiltInUiModuleIds.Oscilloscope, "Oscilloscope", () => new OscilloscopePanel()));
#endif

            uiModuleManager.Register(new BuiltInPageModule(
                BuiltInUiModuleIds.NowPlaying,
                "Now Playing",
                "nowplaying",
                "Reproduciendo ahora",
                0,
                true,
                () => new MainPage(playlistController, playbackBridge, playbackQueue, uiModuleRegistry)));

            uiModuleManager.Register(new BuiltInPageModule(
                BuiltInUiModuleIds.Library,
                "Library",
                "library",
                "Biblioteca",
                1,
                true,
                () => new LibraryPage(playlistController, playbackBridge)));

            uiModuleManager.InitializeAll();

            var pageModules = uiModuleRegistry.Modules
                .OfType<IPageModule>()
                .Where(module => module is INavigationContribution contribution && contribution.IsPrimary)
                .OrderBy(module => ((INavigationContribution)module).Order)
                .ToArray();

            var shellContents = new List<ShellContent>();
            foreach (var pageModule in pageModules)
            {
                var nav = (INavigationContribution)pageModule;
                var content = new ShellContent
                {
                    Title = nav.Title,
                    Route = nav.Route,
                };

                // Preserve the clean-start optimization: Now Playing is eager,
                // Library remains lazy and is created only on first navigation.
                if (pageModule.Descriptor.Id == BuiltInUiModuleIds.NowPlaying)
                    content.Content = pageModule.CreatePage();
                else
                    content.ContentTemplate = new DataTemplate(pageModule.CreatePage);

                shellContents.Add(content);
            }

            var tabBar = new TabBar();
            foreach (var content in shellContents)
                tabBar.Items.Add(content);

            Items.Add(tabBar);

            // La barra de pestañas nativa de Shell queda oculta: la
            // navegación visible es el BottomNavBar de dos botones que
            // cada página incluye en su propio layout (ver
            // BottomNavBar.xaml, MainPage.xaml, LibraryPage.xaml).
            Shell.SetTabBarIsVisible(this, false);

            CurrentItem = tabBar;
        }
    }
}