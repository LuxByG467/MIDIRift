namespace MIDIRift
{
    public partial class App : Application
    {
        private AppShell? _shell;
        private Window? _window;

        public App()
        {
            InitializeComponent();

#if ANDROID
            try { Android.Util.Log.Info("MIDIRift", MidiRiftVersion.FullDisplay); } catch { }
#endif

            // Preferences puede no estar listo todavía en algunos arranques
            // realmente limpios. El tema jamás debe impedir abrir la app.
            try { AppThemeSettings.ApplySaved(); } catch { }

            RequestedThemeChanged += (_, _) =>
            {
                try
                {
                    if (AppThemeSettings.Current == MIDIRiftTheme.System)
                        AppThemeSettings.ApplySaved();
                }
                catch { }
            };
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            if (_window != null)
                return _window;

            Exception? firstFailure = null;

            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    if (attempt == 0)
                        FirstRunInitializer.EnsureInitialized();
                    else
                        FirstRunInitializer.RebuildGeneratedFiles();

                    _shell = new AppShell();
                    _window = new Window(_shell);
                    return _window;
                }
                catch (Exception ex)
                {
                    firstFailure ??= ex;
                    _shell = null;
                    _window = null;

                    // Sólo reintentamos automáticamente cuando el estado era
                    // de primer arranque. Con datos reales existentes no sería
                    // aceptable esconder una regresión reconstruyendo archivos.
                    if (attempt == 0 && !FirstRunInitializer.IsFirstRun)
                        break;
                }
            }

            Exception failure = firstFailure ?? new InvalidOperationException("Fallo de arranque desconocido.");
            WriteStartupLog(failure);
            _window = new Window(CreateStartupErrorPage(failure));
            return _window;
        }

        private static void WriteStartupLog(Exception ex)
        {
            try
            {
                Directory.CreateDirectory(AppDataPaths.Root);
                File.WriteAllText(AppDataPaths.StartupLog, ex.ToString());
            }
            catch { }

#if ANDROID
            try { Android.Util.Log.Error("MIDIRift.Startup", ex.ToString()); } catch { }
#endif
        }

        private static ContentPage CreateStartupErrorPage(Exception ex) => new()
        {
            Title = "MIDIRift",
            Content = new ScrollView
            {
                Content = new VerticalStackLayout
                {
                    Padding = 24,
                    Spacing = 12,
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        new Label
                        {
                            Text = "MIDIRift no pudo iniciar.",
                            FontSize = 20,
                            FontAttributes = FontAttributes.Bold
                        },
                        new Label
                        {
                            Text = "La app intentó reconstruir automáticamente sus datos iniciales. El detalle también quedó registrado con la etiqueta MIDIRift.Startup en Logcat."
                        },
                        new Label
                        {
                            Text = ex.ToString(),
                            FontSize = 11,
                            LineBreakMode = LineBreakMode.WordWrap
                        }
                    }
                }
            }
        };
    }
}
