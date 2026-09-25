using MIDIRift.Synth.DsnLike;
using MIDIRift.Modules.Capabilities;
using MIDIRift.Modules;
using MIDIRift.Modules.UI;
using MIDIRift.Modules.Library;
using Microsoft.Maui.Controls;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Dispatching;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MIDIRift;

public partial class MainPage : ContentPage
{
    // ── Motores de audio (uno u otro activo a la vez, nunca ambos) ────────
    private IChiptunePlayer? _chiptune;
    private IWaveTypeControl? _waveTypeControl;
    private IChannelMixer? _channelMixer;
    private IEngineTelemetry? _engineTelemetry;
    private ITrackerTimelineSource? _trackerTimeline;
    private IMp3Player? _mp3;
    private TrackerPlayer? _player;
#if ANDROID
    private TrackerPanel? _trackerPanel;
    private SpectrumPanel? _spectrumPanel;
    private OscilloscopePanel? _oscilloscopePanel;
    private DsnLikePanel? _dsnLikePanel;
#endif
    private System.Threading.Timer? _panelDataTimer;
#if ANDROID
    private readonly float[] _trackerMeterSamples = new float[512];
    private DateTime _lastTrackerMeterUpdate = DateTime.MinValue;
#endif

    // Qué se muestra en TrackerContainer ahora mismo. Abrir Spectrum u
    // Oscilloscope detiene el renderizado/captura de todo lo demás (Tracker
    // incluido); cerrar ambos vuelve a Tracker. Misma lógica en modo MIDI y
    // en modo MP3 — lo único que cambia entre modos es de dónde sale el
    // audio que alimenta a los paneles (ver GetPanelAudioSource).
    private enum ActivePanel { Tracker, Spectrum, Oscilloscope, DsnLike }
    private ActivePanel _activePanel = ActivePanel.Tracker;

    private enum PlayingMode { None, Midi, Mp3 }
    private PlayingMode _mode = PlayingMode.None;

    // ── Playlists / biblioteca / wavetype persistente ─────────────────────
    // _playlistController ahora se inyecta desde AppShell (una sola instancia
    // compartida con LibraryPage — ver AppShell.xaml.cs) en lugar de crearse
    // acá: LibraryPage necesita ver exactamente la misma biblioteca/playlists
    // que MainPage, no una copia independiente que pisaría los mismos
    // archivos playlists.json/library.json por su cuenta.
    private readonly PlaylistController _playlistController;
    private readonly IPlaybackQueue _playbackQueue;
    private readonly IModuleRegistry _moduleRegistry;
    private readonly WaveTypeConfig _waveTypeConfig = new();
    private readonly ChannelVolumeConfig _channelVolumeConfig = new();
    private bool _updatingChannelVolumeUi;
    private CancellationTokenSource? _channelVolumeSaveCts;
#if ANDROID
    private Grid? _trackerHost;
    private Grid? _channelMixerPanel;
    private HorizontalStackLayout? _channelMixerStrip;
    private Button? _channelMixerToggleButton;
    private Button? _channelMixerResetButton;
    private readonly List<Slider> _channelMixerSliders = new();
    private readonly List<Label> _channelMixerValueLabels = new();
    private bool _channelMixerExpanded;
#endif

    // ── Ecualizador — estado compartido entre motores (mismo patrón que
    //    MainWindow en Desktop: _eqGains es el estado "real", dueño
    //    MainPage, sobrevive a que el usuario cierre/reabra EqualizerPage o
    //    cambie de canción). _eqPresetStore además persiste esas ganancias
    //    a disco (a diferencia de Desktop, ver EqPresetStore.cs) y los
    //    presets personalizados. ─────────────────────────────────────────
    private readonly EqPresetStore _eqPresetStore = new();
    private readonly float[] _eqGains = new float[EqualizerBands.Count];
    private float _eqPreamp = 0f;
    private readonly BassRestorationSettings _bassSettings = BassRestorationSettingsStore.Load();

    // Puente sin UI hacia LibraryPage — ver PlaybackBridge.cs. Le empuja el
    // estado necesario para el mini reproductor flotante y el ícono de
    // "sonando ahora" en las filas de la Biblioteca, y escucha de vuelta
    // cuándo el usuario eligió una canción ahí para cargarla acá.
    private readonly PlaybackBridge _playbackBridge;

    private string? _currentPath;
    private bool _startupRestoreAttempted;
    private string? _currentTrackId;
    // Motor que realmente creó la pista MIDI actual. Se guarda separado
    // de la preferencia porque el usuario puede cambiar el engine mientras
    // una canción anterior sigue reproduciéndose.
    private ChiptuneEngineKind _currentMidiEngineKind = ChiptuneEngineKind.Lyra;

    // Ciclo de repetición: 0 = apagado, 1 = repetir canción, 2 = repetir playlist.
    // Mismos 3 estados que ControlPanel.LoopMode en desktop.
    private int _loopMode = 0;

    private bool _isPaused = false;

    // Se incrementa cada vez que StopAndReset() corre (Stop explícito, fin
    // de pista, o el StopAndReset(keepFile:true) con el que arrancan
    // LoadMidi/LoadMp3). LoadMidi/LoadMp3 graban este valor al empezar y lo
    // vuelven a chequear justo antes de asignarse a _chiptune/_mp3 y arrancar
    // a sonar — si para entonces ya no coincide (porque se apretó Next/Prev/
    // Play de nuevo mientras esta carga todavía estaba parseando/creando el
    // engine en el Task.Run), significa que esta carga quedó obsoleta: se
    // descarta con Dispose() en vez de pisar la instancia más nueva que ya
    // está sonando. Sin esto, apretar Next/Prev varias veces seguidas podía
    // dejar motores de audio huérfanos sonando en paralelo, porque cada
    // carga corría en su propio Task.Run sin enterarse de las demás.
    private int _loadGeneration = 0;

    // Cada carga tiene una generación única y su EOF sólo puede consumirse
    // una vez. Esto protege tanto contra callbacks tardíos de una pista vieja
    // como contra dos detectores de fin que observen la misma pista (MP3 usa
    // OnCompleted + polling IsFinished). Sin esta compuerta ambos podían
    // ejecutar NextAuto() y saltar dos entradas de la playlist.
    private int _finishConsumedGeneration = -1;

    // Última posición/duración conocida, para poder reflejar el estado
    // Play/Pausa en la notificación nativa (MediaSessionBridge) incluso
    // cuando el cambio no viene acompañado de un tick de progreso (p. ej.
    // al pausar, el timer de progreso se detiene).
    private float _lastElapsedSeconds = 0f;
    private float _lastTotalSeconds = 0f;

    // true mientras el usuario tiene el pulgar del ProgressSlider apretado
    // (entre DragStarted y DragCompleted). Es el único punto de exclusión
    // que hace falta: tanto el timer de progreso (OnTrackerFrameTick para
    // MIDI, _mp3ProgressTimer para MP3) como los handlers del Slider corren
    // en el hilo de UI —no hay lock que tomar—, así que esta bandera booleana
    // alcanza para que UpdateProgress() deje de tocar el Slider mientras el
    // dedo lo está moviendo, y así no "pelean" por el valor.
    private bool _isSeekingProgress = false;
    private int _midiSeekGeneration = 0;
    private bool _midiSeekInProgress = false;

    // Sólo un seek MIDI puede reconstruir el engine a la vez. La generación
    // por sí sola descartaba resultados viejos, pero NO detenía su trabajo:
    // varios seeks podían quedar corriendo/esperando simultáneamente.
    private readonly SemaphoreSlim _midiSeekSerial = new(1, 1);

    // Throttle para PlaybackBridge.NotifyChanged() desde UpdateProgress, que
    // en MIDI corre a ritmo de frame de audio (30-120/seg) — a ese ritmo
    // notificar a LibraryPage en cada tick sería tan desperdiciado como el
    // problema que ya evita _isBackground más abajo. El mini reproductor
    // solo necesita refrescar el tiempo transcurrido ~1 vez por segundo.
    private DateTime _lastBridgeNotifyUtc = DateTime.MinValue;

    // ── Segundo plano: apagar todo lo que es puramente visual ──────────────
    // Con la app en segundo plano nadie mira ProgressSlider/Labels/paneles de
    // Tracker-Spectrum-Oscilloscope, así que actualizarlos 30-120 veces por
    // segundo (el ritmo normal de OnTrackerFrameTick) es CPU/batería tirada
    // a la basura. Mientras _isBackground es true: se saltea por completo el
    // trabajo de UI de cada tick (throttleado a ~1/seg, solo lo justo para
    // que MediaSession/notificación no queden con datos viejos) y se apaga
    // el timer de los paneles de visualización. La reproducción de audio en
    // sí (motor MIDI/MP3, detección de fin de pista, avance de playlist) no
    // depende de nada de esto — sigue exactamente igual.
    private bool _isBackground = false;
    private DateTime _lastBackgroundProgressUpdate = DateTime.MinValue;

    // FIX: UpdateWaveTypes() es polling defensivo (el cambio real llega por
    // el dropdown del propio TrackerPanel) pero GetWaveTypes() asigna un
    // List<WaveType> nuevo por llamada (ChiptuneAudioTrack.GetWaveTypes).
    // Llamarlo a TrackerPlayer.TargetFps (hasta 120/seg) era GC innecesario
    // en el hilo de UI para detectar un cambio que en la práctica ocurre
    // pocas veces por minuto. 8/seg alcanza de sobra para que el polling
    // defensivo siga sintiéndose instantáneo.
    private static readonly TimeSpan WaveTypePollInterval = TimeSpan.FromMilliseconds(125);
    private DateTime _lastWaveTypePoll = DateTime.MinValue;

    // FIX: evita reasignar ElapsedLabel.Text/TotalLabel.Text cuando el
    // segundo entero mostrado no cambió (ver UpdateProgress).
    private int _lastDisplayedElapsedSec = -1;
    private int _lastDisplayedTotalSec = -1;

    // ── Timer de progreso para MP3 ─────────────────────────────────────────
    // El motor MIDI (TrackerPlayer) empuja su propio progreso vía OnFrameTick.
    // MediaPlayer no empuja nada, así que para MP3 hacemos polling periódico
    // de CurrentPosition, igual en espíritu al _uiTimer de desktop.
    private IDispatcherTimer? _mp3ProgressTimer;

    // Tarjeta compacta de "reproduciendo ahora". El título se conserva
    // completo en _currentTrackTitle; FileNameLabel solo muestra una ventana
    // desplazada cuando no cabe. El timer es deliberadamente lento: esto no
    // necesita animación a 60 FPS para mover unas cuantas letras.
    private IDispatcherTimer? _trackInfoTimer;
    private string _currentTrackTitle = "Ningún archivo seleccionado";
    private int _trackTitleOffset;
    private int _trackInfoRotationIndex;
    private int _trackInfoTimerTicks;

    // Función para mostrar frecuencias como nombres de nota
    private static readonly string[] NoteNames =
        { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    private static string FreqToNote(float freq)
    {
        if (freq <= 0) return "---";
        int midi = (int)MathF.Round(69f + 12f * MathF.Log2(freq / 440f));
        int oct = midi / 12 - 1;
        return $"{NoteNames[midi % 12]}{oct}";
    }

    public MainPage(PlaylistController playlistController, PlaybackBridge playbackBridge, IPlaybackQueue playbackQueue, IModuleRegistry moduleRegistry)
    {
        InitializeComponent();
        _landscapeAutoHideTimer = new System.Threading.Timer(
            _ => MainThread.BeginInvokeOnMainThread(HideLandscapeChromeAfterInactivity),
            null,
            Timeout.Infinite,
            Timeout.Infinite);
        _playlistController = playlistController;
        _playbackQueue = playbackQueue ?? throw new ArgumentNullException(nameof(playbackQueue));
        _moduleRegistry = moduleRegistry ?? throw new ArgumentNullException(nameof(moduleRegistry));
        _playbackBridge = playbackBridge;

        InitTrackerPanel();
        ThemePalette.Changed += OnThemePaletteChanged;
        ApplyThemeToVisuals(ThemePalette.EffectiveTheme);
#if ANDROID
        MeasurementLabelsSettings.Changed += OnMeasurementLabelsChanged;
#endif
        UpdatePlaylistStatus();

        Array.Copy(_eqPresetStore.CurrentGains, _eqGains, _eqGains.Length);
        _eqPreamp = _eqPresetStore.CurrentPreampDb;

        // La Biblioteca pide reproducir una canción -> mismo camino que ya
        // usaba el botón de playlist (📃) de acá arriba, LoadByExtension.
        _playbackBridge.PlayRequested += path =>
            MainThread.BeginInvokeOnMainThread(() => LoadByExtension(path));
        _playbackBridge.TogglePauseRequested += () =>
            MainThread.BeginInvokeOnMainThread(() => OnPlayPauseClicked(this, EventArgs.Empty));
        _playbackBridge.NextRequested += () =>
            MainThread.BeginInvokeOnMainThread(() => OnNextClicked(this, EventArgs.Empty));
        _playbackBridge.PreviousRequested += () =>
            MainThread.BeginInvokeOnMainThread(() => OnPrevClicked(this, EventArgs.Empty));
        _playbackBridge.SeekRequested += seconds =>
            MainThread.BeginInvokeOnMainThread(() => SeekFromPlaybackService(seconds));

#if ANDROID
        InitMediaSessionBridge();
        PlaybackLifecycleCoordinator.RegisterOwner(() => StopAndReset(keepFile: true));
#endif

        AppLifecycleState.Changed += OnBackgroundStateChanged;
        StartTrackInfoTimer();
        RefreshTrackInfoCard();
        Dispatcher.Dispatch(() =>
        {
            try { TryRestoreLastTrackOnStartup(); }
            catch
            {
                // Session restore is optional state. A missing/unavailable
                // Preferences provider on a genuinely clean Android start must
                // never prevent Now Playing from being constructed.
                _startupRestoreAttempted = true;
            }
        });
    }

    /// <summary>
    /// Único punto de entrada/salida de segundo plano. Alimentado por
    /// AppLifecycleState (que a su vez alimenta MainActivity.OnStart/OnStop
    /// en Android — ver comentarios ahí). En Desktop/otras plataformas esto
    /// simplemente nunca se dispara con true, así que no cambia nada.
    /// </summary>
#if ANDROID
    private void OnMeasurementLabelsChanged(bool enabled)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _trackerPanel?.InvalidateSurface();
            _spectrumPanel?.InvalidateSurface();
            _oscilloscopePanel?.InvalidateSurface();
        });
    }
#endif

    private void OnBackgroundStateChanged(bool isBackground)
    {
        _isBackground = isBackground;

#if ANDROID
        if (isBackground)
        {
            // El marquee y la rotación de información son puramente visuales.
            // Se detienen por completo mientras la app no está visible.
            _trackInfoTimer?.Stop();

            // Los paneles de visualización (Spectrum/Oscilloscope) son
            // 100% cosméticos: sin nadie mirando la pantalla, calcular FFT
            // y redibujar no tiene ningún propósito.
            _panelDataTimer?.Dispose();
            _panelDataTimer = null;
        }
        else
        {
            StartTrackInfoTimer();
            RefreshTrackInfoCard();

            // Volvimos a primer plano: si había un panel de visualización
            // activo, reconectarlo (Attach + reiniciar su timer). Reusa
            // ApplyActivePanel(), que ya es idempotente para los tres casos
            // (Tracker/Spectrum/Oscilloscope).
            ApplyActivePanel();
        }
#endif
    }

#if ANDROID
    /// <summary>
    /// Conecta los comandos que pueden llegar desde fuera de la app —
    /// notificación, pantalla de bloqueo, botón de auriculares/Bluetooth —
    /// con la misma lógica que usan los botones propios de la UI. Así el
    /// estado de reproducción nunca se desincroniza entre ambos.
    /// </summary>
    private void InitMediaSessionBridge()
    {
        MediaSessionBridge.PlayPauseRequested += () =>
            MainThread.BeginInvokeOnMainThread(() => OnPlayPauseClicked(this, EventArgs.Empty));
        MediaSessionBridge.NextRequested += () =>
            MainThread.BeginInvokeOnMainThread(() => OnNextClicked(this, EventArgs.Empty));
        MediaSessionBridge.PrevRequested += () =>
            MainThread.BeginInvokeOnMainThread(() => OnPrevClicked(this, EventArgs.Empty));
        MediaSessionBridge.StopRequested += () =>
            MainThread.BeginInvokeOnMainThread(() => OnStopClicked(this, EventArgs.Empty));
        MediaSessionBridge.SeekRequested += positionMs =>
            MainThread.BeginInvokeOnMainThread(async () =>
                await SeekPlaybackAsync((float)(positionMs / 1000.0)));

        // POST_NOTIFICATIONS es obligatorio desde Android 13 (API 33) para
        // poder mostrar la notificación de controles — sin esto el
        // foreground service arranca igual (así que el audio no se corta),
        // pero la notificación queda oculta.
        _ = RequestNotificationPermissionAsync();
    }

    private static async Task RequestNotificationPermissionAsync()
    {
        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
            if (status != PermissionStatus.Granted)
                await Permissions.RequestAsync<Permissions.PostNotifications>();
        }
        catch
        {
            // En versiones de Android donde el permiso no aplica (< 13) o si
            // el runtime de MAUI usado no incluye este permiso todavía, no
            // hay nada que pedir: se ignora silenciosamente.
        }
    }
#endif

    // ── Landscape inmersivo ──────────────────────────────────────────────
    // En horizontal el contenido visual (tracker, espectro u osciloscopio)
    // ocupa toda la página. Un toque en ese contenido alterna el resto del
    // chrome. No movemos filas ni RowSpan manualmente: al ocultar las filas
    // Auto, la fila estrella del TrackerContainer absorbe el espacio sola.
    private bool _isLandscape;
    private bool _landscapeChromeVisible = true;
    private readonly System.Threading.Timer _landscapeAutoHideTimer;
    private const int LandscapeAutoHideDelayMs = 3500;

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0 || height <= 0) return;

        bool isLandscape = width > height;
        if (isLandscape == _isLandscape) return;

        _isLandscape = isLandscape;

        // Cada vez que se entra a landscape arrancamos realmente inmersivos.
        // Al volver a portrait restauramos la interfaz completa.
        _landscapeChromeVisible = !isLandscape;
        UpdateChromeVisibility();

        if (isLandscape)
            StopLandscapeAutoHideTimer();
        else
            StopLandscapeAutoHideTimer();
    }

    private void OnTrackerAreaTapped(object? sender, TappedEventArgs e) =>
        HandleLandscapePanelTap();

    private void OnVisualPanelTapped() => HandleLandscapePanelTap();

    private void HandleLandscapePanelTap()
    {
        if (!_isLandscape) return;

        // Un toque en el contenido solo muestra el chrome. Ocultarlo a mano
        // desde el tracker hacía imposible usar los Pickers sin entrar en un
        // bucle. El ocultado ahora lo resuelve el temporizador de inactividad.
        if (!_landscapeChromeVisible)
        {
            _landscapeChromeVisible = true;
            UpdateChromeVisibility();
        }

        RestartLandscapeAutoHideTimer();
    }

    private void OnAndroidUserInteraction()
    {
        if (!_isLandscape) return;

        // DispatchTouchEvent ve también botones, sliders y Pickers, incluso
        // cuando Skia o el control nativo consumen el evento.
        if (_landscapeChromeVisible)
            RestartLandscapeAutoHideTimer();
    }

    private void RestartLandscapeAutoHideTimer()
    {
        if (!_isLandscape || !_landscapeChromeVisible) return;
        _landscapeAutoHideTimer.Change(LandscapeAutoHideDelayMs, Timeout.Infinite);
    }

    private void StopLandscapeAutoHideTimer() =>
        _landscapeAutoHideTimer.Change(Timeout.Infinite, Timeout.Infinite);

    private void HideLandscapeChromeAfterInactivity()
    {
        if (!_isLandscape || !_landscapeChromeVisible) return;
        _landscapeChromeVisible = false;
        UpdateChromeVisibility();
    }

    private void UpdateChromeVisibility()
    {
#if ANDROID
        bool showChrome = !_isLandscape || _landscapeChromeVisible;

        TopBar.IsVisible = showChrome;
        BottomControls.IsVisible = showChrome;
        BottomNavigation.IsVisible = showChrome;

        // El mezclador necesita conservar la barra de progreso dentro de
        // SecondaryControls, pero oculta los datos y la velocidad para dejar
        // sitio a los faders verticales.
        SecondaryControls.IsVisible = showChrome;
        // En landscape conservamos solo progreso y transporte. La tarjeta,
        // estado y velocidad consumían demasiado alto para una vista cuyo
        // protagonista debe ser el panel visual.
        TrackInfoPanel.IsVisible = showChrome && !_channelMixerExpanded && !_isLandscape;
        PlaybackStatusRow.IsVisible = showChrome && !_channelMixerExpanded && !_isLandscape;
        SpeedControls.IsVisible = showChrome && !_channelMixerExpanded && !_isLandscape;
        ChannelVolumeControls.IsVisible = false;

        ApplyLandscapeCompactSizing(_isLandscape);
#else
        SecondaryControls.IsVisible = !_isLandscape || _landscapeChromeVisible;
#endif
    }

    private void ApplyLandscapeCompactSizing(bool compact)
    {
        TopBar.Padding = compact ? new Thickness(8, 3) : new Thickness(12, 8);
        BottomControls.Padding = compact ? new Thickness(10, 4) : new Thickness(16, 10);
        BottomControls.Spacing = compact ? 4 : 8;
        SecondaryControls.Spacing = compact ? 4 : 8;

        double topWidth = compact ? 38 : 44;
        double topHeight = compact ? 34 : 40;
        foreach (var button in new[] { SpectrumToggleButton, OscilloscopeToggleButton, DsnLikeToggleButton, EqualizerToggleButton, EngineSettingsButton })
        {
            button.WidthRequest = topWidth;
            button.HeightRequest = topHeight;
            button.Padding = compact ? new Thickness(8) : new Thickness(10);
        }

        double transportHeight = compact ? 38 : 48;
        foreach (var button in new[] { ShuffleButton, PrevButton, PlayPauseButton, StopButton, LoopButton, NextButton })
            button.HeightRequest = transportHeight;

        BottomNavigation.SetCompactMode(compact);
    }

    private void OnThemePaletteChanged(AppTheme theme) =>
        MainThread.BeginInvokeOnMainThread(() => ApplyThemeToVisuals(theme));

    private void ApplyThemeToVisuals(AppTheme theme)
    {
#if ANDROID
        bool light = theme == AppTheme.Light;
        SpectrumToggleButton.ImageSource = light ? "icon_spectrum_light.svg" : "icon_spectrum.svg";
        OscilloscopeToggleButton.ImageSource = light ? "icon_oscilloscope_light.svg" : "icon_oscilloscope.svg";
        EqualizerToggleButton.ImageSource = light ? "icon_equalizer_light.svg" : "icon_equalizer.svg";
        EngineSettingsButton.ImageSource = light ? "icon_settings_light.svg" : "icon_settings.svg";
        ShuffleButton.ImageSource = light ? "icon_shuffle_light.svg" : "icon_shuffle.svg";

        // Los paneles sólo pintan cuando Reproduciendo ahora los necesita.
        // Cambiar la paleta invalida sus caches; el siguiente frame ya nace
        // con el tema correcto, sin mantener render loops ocultos.
        _trackerPanel?.ApplyTheme(light);
        _spectrumPanel?.ApplyTheme(light);
        _oscilloscopePanel?.ApplyTheme(light);

        // Controles creados en C# (MIX y faders) no reciben XAML DynamicResource
        // automáticamente, así que se refrescan junto con los visualizadores.
        ApplyChannelMixerTheme();
        UpdatePanelButtons();
        UpdateShuffleVisualState();
#endif
    }

    // ── TrackerPanel ──────────────────────────────────────────────────────

    private void InitTrackerPanel()
    {
#if ANDROID
        _trackerPanel = CreateVisualizer<TrackerPanel>(BuiltInUiModuleIds.Tracker);
        _trackerPanel.HorizontalOptions = LayoutOptions.Fill;
        _trackerPanel.VerticalOptions = LayoutOptions.Fill;
        BuildTrackerHost();

        // Hot-swap: el usuario elige un WaveType nuevo desde el dropdown
        // del header -> se lo aplicamos al motor en caliente y persistimos
        // la elección para esta canción (indexada por LibraryTrack.Id).
        _trackerPanel.OnWaveTypeChanged += (col, wave) =>
        {
            _waveTypeControl?.SetWaveType(col, wave);

            if (_currentTrackId != null && _chiptune != null)
                if (_waveTypeControl != null)
                    _waveTypeConfig.Save(_currentTrackId, _waveTypeControl.WaveTypes.ToList());
        };

        // DSN-like uses a Patch Architecture instead of WaveTypeSelector.
        // Tapping PATCH in a Tracker header opens the existing editor directly
        // on that channel's *current* GM program.
        _trackerPanel.PatchHeaderTapped += async col =>
        {
            if (_currentMidiEngineKind != ChiptuneEngineKind.DsnLike ||
                _chiptune is not DsnLikeAudioTrackPlayer dsn)
                return;

            if (dsn.IsPercussionChannel(col))
            {
                await Navigation.PushModalAsync(new DsnDrumEditorPage(() => dsn.RefreshDrumKitSettings()));
                return;
            }

            int program = dsn.GetChannelProgram(col);
            await Navigation.PushModalAsync(new DsnPatchEditorPage(
                program,
                assignmentChanged: changedProgram =>
                {
                    // A saved/reset User Bank assignment affects future notes
                    // only when this live channel currently uses that GM program.
                    if (dsn.GetChannelProgram(col) == changedProgram)
                        dsn.RefreshChannelPatchAssignment(col);
                },
                programChanged: changedProgram =>
                {
                    // Contextual editor semantics: choosing another GM program
                    // changes the live channel immediately. It is NOT a patch
                    // edit and must not create a User Patch.
                    dsn.SetChannelProgram(col, changedProgram);
                }));
        };

        _trackerPanel.HorizontalScrollChanged += OnTrackerHorizontalScrollChanged;
        _trackerPanel.VisualTapped += OnVisualPanelTapped;

        _spectrumPanel = CreateVisualizer<SpectrumPanel>(BuiltInUiModuleIds.Spectrum);
        _spectrumPanel.HorizontalOptions = LayoutOptions.Fill;
        _spectrumPanel.VerticalOptions = LayoutOptions.Fill;

        _oscilloscopePanel = CreateVisualizer<OscilloscopePanel>(BuiltInUiModuleIds.Oscilloscope);
        _oscilloscopePanel.HorizontalOptions = LayoutOptions.Fill;
        _oscilloscopePanel.VerticalOptions = LayoutOptions.Fill;

        _dsnLikePanel = new DsnLikePanel();
        _dsnLikePanel.HorizontalOptions = LayoutOptions.Fill;
        _dsnLikePanel.VerticalOptions = LayoutOptions.Fill;

        _spectrumPanel.VisualTapped += OnVisualPanelTapped;
        _oscilloscopePanel.VisualTapped += OnVisualPanelTapped;

        UpdatePanelButtons();
#endif
    }

    private TView CreateVisualizer<TView>(string moduleId)
        where TView : View
    {
        var module = _moduleRegistry.Get<IVisualizerModule>(moduleId)
            ?? throw new InvalidOperationException(
                $"Visualizer module '{moduleId}' is not registered.");

        var view = module.CreateView();
        if (view is TView typed)
            return typed;

        throw new InvalidOperationException(
            $"Visualizer module '{moduleId}' returned '{view.GetType().Name}', expected '{typeof(TView).Name}'.");
    }

    // ── Alternar Tracker / Spectrum / Oscilloscope ─────────────────────────

    private void OnSpectrumToggleClicked(object sender, EventArgs e) =>
        SetActivePanel(_activePanel == ActivePanel.Spectrum ? ActivePanel.Tracker : ActivePanel.Spectrum);

    private void OnOscilloscopeToggleClicked(object sender, EventArgs e) =>
        SetActivePanel(_activePanel == ActivePanel.Oscilloscope ? ActivePanel.Tracker : ActivePanel.Oscilloscope);

    private void OnDsnLikeToggleClicked(object sender, EventArgs e) =>
        SetActivePanel(_activePanel == ActivePanel.DsnLike ? ActivePanel.Tracker : ActivePanel.DsnLike);

    // ── Ecualizador ──────────────────────────────────────────────────────
    // A diferencia de Spectrum/Oscilloscope (que alternan contenido dentro
    // de TrackerContainer), el EQ abre una pantalla dedicada (requisito
    // explícito) vía PushModalAsync — no toca _activePanel ni el layout de
    // MainPage. _eqGains es el estado dueño de MainPage (mismo patrón que
    // _eqGains en MainWindow de Desktop): EqualizerPage solo lo edita a
    // través de los dos callbacks de abajo.
    private async void OnEqualizerClicked(object sender, EventArgs e)
    {
#if ANDROID
        // La construcción inicial de CurveView y del layout del EQ puede ser
        // costosa. Detener primero los paneles evita que FFT/osc compitan por
        // CPU durante ese pico y protege al productor de audio.
        _panelDataTimer?.Dispose();
        _panelDataTimer = null;
        _spectrumPanel?.Detach();
        _oscilloscopePanel?.Detach();
#endif
        await Task.Yield();
        var page = new EqualizerPage(_eqPresetStore, _eqGains, _eqPreamp, _bassSettings.Clone());

        page.OnBandGainChanged += (band, db) =>
        {
            if ((uint)band >= (uint)_eqGains.Length) return;
            _eqGains[band] = db;
            _chiptune?.SetEqBand(band, db);
            _mp3?.SetEqBand(band, db);
        };

        page.OnGainsReplaced += gains =>
        {
            int n = Math.Min(_eqGains.Length, gains.Length);
            for (int i = 0; i < n; i++)
            {
                _eqGains[i] = gains[i];
                _chiptune?.SetEqBand(i, gains[i]);
                _mp3?.SetEqBand(i, gains[i]);
            }
        };

        page.OnPreampChanged += db =>
        {
            _eqPreamp = db;
            _chiptune?.SetPreamp(db);
            _mp3?.SetPreamp(db);
        };

        page.OnGainsCommitted += gains =>
        {
            int n = Math.Min(_eqGains.Length, gains.Length);
            for (int i = 0; i < n; i++) _eqGains[i] = gains[i];
            _eqPresetStore.SaveCurrentGains(_eqGains);
        };

        page.OnPreampCommitted += db => _eqPresetStore.SaveCurrentPreamp(db);

        page.OnBassSettingsChanged += settings =>
        {
            _bassSettings.Enabled = settings.Enabled;
            _bassSettings.Intensity = settings.Intensity;
            _bassSettings.FrequencyHz = settings.FrequencyHz;
            _bassSettings.Mix = settings.Mix;
            ApplyBassRestoration();
        };

        page.OnBassSettingsCommitted += settings =>
        {
            _bassSettings.Enabled = settings.Enabled;
            _bassSettings.Intensity = settings.Intensity;
            _bassSettings.FrequencyHz = settings.FrequencyHz;
            _bassSettings.Mix = settings.Mix;
            BassRestorationSettingsStore.Save(_bassSettings);
        };

        page.Disappearing += (_, _) =>
        {
#if ANDROID
            ApplyActivePanel();
#endif
        };

        // Sin animación: evita una composición/transición pesada justo cuando
        // MAUI está midiendo por primera vez la curva y todos los sliders.
        await Navigation.PushModalAsync(page, animated: false);
    }

    /// <summary>Aplica _eqGains/_eqPreamp al motor recién cargado — mismo momento en que Desktop reaplica su _eqGains tras crear un nuevo IAudioSource.</summary>
    private void ApplyEqGains()
    {
        for (int i = 0; i < _eqGains.Length; i++)
        {
            _chiptune?.SetEqBand(i, _eqGains[i]);
            _mp3?.SetEqBand(i, _eqGains[i]);
        }
        _chiptune?.SetPreamp(_eqPreamp);
        _mp3?.SetPreamp(_eqPreamp);
        ApplyBassRestoration();
    }

    private void ApplyBassRestoration()
    {
        _chiptune?.SetBassRestorationIntensity(_bassSettings.Intensity);
        _chiptune?.SetBassRestorationFrequency(_bassSettings.FrequencyHz);
        _chiptune?.SetBassRestorationMix(_bassSettings.Mix);
        _chiptune?.SetBassRestorationEnabled(_bassSettings.Enabled);

        _mp3?.SetBassRestorationIntensity(_bassSettings.Intensity);
        _mp3?.SetBassRestorationFrequency(_bassSettings.FrequencyHz);
        _mp3?.SetBassRestorationMix(_bassSettings.Mix);
        _mp3?.SetBassRestorationEnabled(_bassSettings.Enabled);
    }

    private void SetActivePanel(ActivePanel panel)
    {
        _activePanel = panel;
        ApplyActivePanel();
    }

    /// <summary>
    /// Único punto que decide qué vive en TrackerContainer y qué recibe
    /// datos de audio ahora mismo. Todo lo que NO es el panel activo queda
    /// desconectado (Detach) — deja de capturar muestras y de invalidar su
    /// superficie, así que no gasta CPU aunque exista la instancia.
    /// </summary>
    private void ApplyActivePanel()
    {
#if ANDROID
        _panelDataTimer?.Dispose();
        _panelDataTimer = null;
        if (_activePanel != ActivePanel.Oscilloscope) _oscilloscopePanel?.Detach();

        switch (_activePanel)
        {
            case ActivePanel.Spectrum:
                TrackerContainer.Content = _spectrumPanel;
                // El SpectrumPanel clean-room posee su propio worker de análisis
                // y su propio render loop. No comparte el timer del osciloscopio.
                _spectrumPanel?.Attach(GetPanelAudioSource());
                break;

            case ActivePanel.Oscilloscope:
                TrackerContainer.Content = _oscilloscopePanel;
                _oscilloscopePanel?.Attach(GetPanelAudioSource());
                if (_oscilloscopePanel != null) StartPanelTimer(_oscilloscopePanel.Tick, 40);
                break;

            case ActivePanel.DsnLike:
                // DSN Lab is deliberately isolated from the live playback engine.
                // Benchmarks run on a worker thread and never touch the audio callback.
                TrackerContainer.Content = _dsnLikePanel;
                break;

            default: // Tracker (o el placeholder de MP3, que ocupa su lugar)
                if (_mode == PlayingMode.Mp3)
                    ShowMp3Placeholder();
                else if (_trackerPanel != null)
                {
                    if (_trackerHost is not null)
                        TrackerContainer.Content = _trackerHost;
                    else
                        TrackerContainer.Content = _trackerPanel;
                }
                break;
        }

        UpdatePanelButtons();
#endif
    }

#if ANDROID
    /// <summary>
    /// De dónde sale el audio crudo para Spectrum/Oscilloscope según el modo
    /// actual. Tanto ChiptuneAudioTrack (MIDI) como Mp3AudioPlayer (MP3)
    /// implementan IPanelAudioSource con captura real de PCM — MP3 ya no
    /// pasa por Android.Media.MediaPlayer (una caja negra que no expone
    /// muestras), así que ambos modos alimentan a los paneles igual.
    /// </summary>
    private IPanelAudioSource? GetPanelAudioSource() => _mode switch
    {
        PlayingMode.Midi => _chiptune as IPanelAudioSource,
        PlayingMode.Mp3 => _mp3 as IPanelAudioSource,
        _ => null,
    };

    private void StartPanelTimer(Action tick, int intervalMs)
    {
        _panelDataTimer?.Dispose();
        _panelDataTimer = null;

        // Antes: Dispatcher.CreateTimer() a 33ms. Ese timer vive en el
        // message loop del hilo de UI de Android — el mismo hilo que
        // procesa touch, layout, y (en el caso de Spectrum) donde antes
        // corría el FFT completo por canal dentro de Tick(). Cuando ese
        // hilo se atoraba un instante, el tick se atrasaba y a veces
        // "recuperaba" de golpe con un salto grande — eso es lo que se
        // sentía como el jaloneo del osciloscopio (confirmado con el
        // medidor de cadencia: Tick llegaba a picos de 130+ms mientras el
        // paint se mantenía más parejo).
        //
        // Ahora: System.Threading.Timer corriendo en un hilo del
        // ThreadPool, totalmente separado del hilo de UI. tick() (que
        // copia las muestras del ring buffer y, en Spectrum, corre el
        // FFT) se ejecuta ahí — nunca compite por el hilo de UI. Recién
        // al final, cada panel marca el redibujado con
        // MainThread.BeginInvokeOnMainThread(InvalidateSurface) — una
        // llamada liviana, no el trabajo pesado.
        //
        // Se rearma manualmente (Change) DESPUÉS de que tick() termina,
        // en vez de un período fijo: así, si tick() tarda más de 33ms en
        // algún momento puntual, el siguiente tick simplemente arranca
        // más tarde — nunca se apilan dos ejecuciones en paralelo.
        // Cadencia absoluta: el siguiente deadline se deriva del anterior,
        // no del momento en que terminó el análisis. Si un FFT tarda 6 ms,
        // el timer espera ~34 ms para un intervalo de 40, en vez de sumar
        // otros 40 ms y degradar silenciosamente la frecuencia real.
        long intervalTicks = Math.Max(1L,
            (long)(System.Diagnostics.Stopwatch.Frequency * (intervalMs / 1000.0)));
        long nextDeadline = System.Diagnostics.Stopwatch.GetTimestamp() + intervalTicks;

        System.Threading.Timer? timer = null;
        timer = new System.Threading.Timer(_ =>
        {
            try
            {
                tick();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PanelTimer] Tick crash: {ex}");
            }
            finally
            {
                try
                {
                    long now = System.Diagnostics.Stopwatch.GetTimestamp();
                    nextDeadline += intervalTicks;

                    // Si Android suspendió el proceso o hubo una pausa larga,
                    // saltamos deadlines vencidos. Nunca intentamos "ponernos
                    // al corriente" apilando FFTs atrasadas.
                    if (nextDeadline <= now)
                    {
                        long missed = (now - nextDeadline) / intervalTicks + 1;
                        nextDeadline += missed * intervalTicks;
                    }

                    int delayMs = Math.Max(1, (int)Math.Ceiling(
                        (nextDeadline - now) * 1000.0 /
                        System.Diagnostics.Stopwatch.Frequency));
                    timer?.Change(delayMs, System.Threading.Timeout.Infinite);
                }
                catch (ObjectDisposedException)
                {
                    // El panel cambió mientras este tick estaba en curso.
                }
            }
        }, null, intervalMs, System.Threading.Timeout.Infinite);

        _panelDataTimer = timer;
    }

    private void UpdatePanelButtons()
    {
        SetToggleButtonVisual(SpectrumToggleButton, _activePanel == ActivePanel.Spectrum);
        SetToggleButtonVisual(OscilloscopeToggleButton, _activePanel == ActivePanel.Oscilloscope);
        SetToggleButtonVisual(DsnLikeToggleButton, _activePanel == ActivePanel.DsnLike);
    }

    private static void SetToggleButtonVisual(Button button, bool active)
    {
        // No fijar aquí los colores del tema oscuro: estos botones también
        // se actualizan mientras Light está activo.
        button.SetDynamicResource(Button.BackgroundColorProperty, active ? "PurplePrimary" : "ControlBackground");
        button.SetDynamicResource(Button.TextColorProperty, active ? "CardBackground" : "TextMuted");
    }
#endif

    // ── Playlist ──────────────────────────────────────────────────────────
    // El botón de playlist (📃) que vivía acá en el TopBar se sacó: su
    // función (abrir la biblioteca para elegir una canción) ahora la cubre
    // la pestaña inferior "Biblioteca" (ver BottomNavBar / LibraryPage),
    // que llega hasta acá vía _playbackBridge.PlayRequested en vez de un
    // PushModalAsync.

    private void UpdatePlaylistStatus()
    {
        var playlist = _playlistController.ActivePlaylist;
        if (playlist == null)
        {
            PlaylistStatusLabel.IsVisible = false;
            PrevButton.IsEnabled = false;
            NextButton.IsEnabled = false;
            return;
        }

        PlaylistStatusLabel.IsVisible = true;
        int pos = _playlistController.CurrentEntryIndex + 1;
        PlaylistStatusLabel.Text = pos > 0
            ? $"{playlist.Name} · {pos}/{playlist.Entries.Count}"
            : $"{playlist.Name} · {playlist.Entries.Count} canciones";

        PrevButton.IsEnabled = playlist.Entries.Count > 0;
        NextButton.IsEnabled = playlist.Entries.Count > 0;
    }

    private void OnPrevClicked(object sender, EventArgs e)
    {
        var track = _playbackQueue.Previous();
        if (track != null) LoadByExtension(track.Path);
    }

    private void OnNextClicked(object sender, EventArgs e)
    {
        var track = _playbackQueue.Next();
        if (track != null) LoadByExtension(track.Path);
    }

    // ── Playback: Play/Pause (un solo botón) / Stop / Loop / Speed ─────────

    /// <summary>Verdadero si hay algo sonando activamente ahora mismo (ni pausado ni detenido).</summary>
    private bool IsActivelyPlaying =>
        !_isPaused &&
        ((_mode == PlayingMode.Midi && _chiptune != null) ||
         (_mode == PlayingMode.Mp3 && _mp3 != null));

    private void OnPlayPauseClicked(object sender, EventArgs e)
    {
        if (IsActivelyPlaying)
        {
            PauseCurrent();
            return;
        }

        if (_isPaused)
        {
            ResumeCurrent();
            return;
        }

        if (_currentPath != null)
            LoadByExtension(_currentPath);
    }

    private void ResumeCurrent()
    {
        _isPaused = false;

        if (_mode == PlayingMode.Midi)
        {
            _chiptune?.Play();
            _player?.Start();
        }
        else if (_mode == PlayingMode.Mp3)
        {
            _mp3?.Play();
            _mp3ProgressTimer?.Start();
        }

        SetPlayingState(true);
        if (_mode == PlayingMode.Midi && _chiptune != null)
        {
            SetStatus(
                $"Reproduciendo — {_chiptune.ChannelCount} canales • {(_currentMidiEngineKind switch { ChiptuneEngineKind.Lyra => "Lyra", ChiptuneEngineKind.DsnLike => "DSN-like", _ => "Classic / Legacy" })}",
                "#7fd48f");
        }
        else if (_mode == PlayingMode.Mp3 && _mp3 != null)
        {
            SetStatus("Reproduciendo MP3", "#7fd48f");
        }
    }

    private void PauseCurrent()
    {
        if (_mode == PlayingMode.Midi && _chiptune != null)
        {
            _isPaused = true;
            _chiptune.Stop(); // Stop sin Dispose — el engine mantiene su estado
            _player?.Stop();
        }
        else if (_mode == PlayingMode.Mp3 && _mp3 != null)
        {
            _isPaused = true;
            _mp3.Pause();
            _mp3ProgressTimer?.Stop();
        }

        SetPlayingState(false);
        SetStatus("Pausado", "#9e9ab8");
    }

    private void OnStopClicked(object sender, EventArgs e)
    {
        StopAndReset(keepFile: true);
#if ANDROID
        MediaSessionBridge.Stop();
#endif
    }

    private void OnLoopClicked(object sender, EventArgs e)
    {
        _loopMode = (_loopMode + 1) % 3;
        _playbackQueue.SetLooping(_loopMode == 2);

        (LoopButton.Text, LoopButton.TextColor) = _loopMode switch
        {
            1 => ("⟳¹", Color.FromArgb("#50a060")),  // repetir canción
            2 => ("⟳∞", Color.FromArgb("#4a90d0")),  // repetir playlist
            _ => ("⟳", Color.FromArgb("#5a566e")),   // apagado
        };
    }

    private void OnShuffleClicked(object sender, EventArgs e)
    {
        _playbackQueue.SetShuffling(!_playbackQueue.IsShuffling);

        UpdateShuffleVisualState();
    }

    private void UpdateShuffleVisualState()
    {
        bool enabled = _playbackQueue.IsShuffling;

        // El icono no depende de una fuente de emojis. El estado se comunica
        // mediante fondo + borde + descripción accesible, no solo por color.
        // El estado conserva contraste en ambos temas. En Light el estado
        // apagado usa la superficie clara y el icono oscuro específico.
        ShuffleButton.BackgroundColor = ThemePalette.Get(enabled ? "GreenSurface" : "ControlBackground");
        ShuffleButton.BorderColor = ThemePalette.Get(enabled ? "GreenAccent" : "BorderStrong");
        ShuffleButton.BorderWidth = enabled ? 2 : 1;
        ShuffleButton.Opacity = enabled ? 1.0 : 0.72;
        SemanticProperties.SetDescription(ShuffleButton, enabled
            ? "Reproducción aleatoria activada"
            : "Reproducción aleatoria desactivada");
    }

    private void OnSpeedChanged(object sender, ValueChangedEventArgs e)
    {
        float speed = (float)e.NewValue;
        SpeedLabel.Text = $"{speed:F2}×";
        if (_chiptune != null) _chiptune.Speed = speed;
        if (_mp3 != null) _mp3.Speed = speed;
    }

    private void OnResetSpeedClicked(object sender, EventArgs e)
    {
        SpeedSlider.Value = 1.0; // dispara OnSpeedChanged, que aplica y actualiza el label
    }

    private void TryRestoreLastTrackOnStartup()
    {
        if (_startupRestoreAttempted)
            return;

        _startupRestoreAttempted = true;

        if (StartupPlaybackSettings.Behavior != StartupPlaybackBehavior.RestoreLastTrack)
            return;

        string? path = StartupPlaybackSettings.LastTrackPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            if (!string.IsNullOrWhiteSpace(path))
                StartupPlaybackSettings.LastTrackPath = null;
            StartupPlaybackSettings.ClearPlaylistContext();
            return;
        }

        // Restaurar primero el contexto de cola. LoadByExtension terminará de
        // sincronizar por TrackId cuando la canción quede cargada.
        bool restoredPlaylist = _playlistController.TryRestorePlaybackContext(
            StartupPlaybackSettings.LastPlaylistId,
            StartupPlaybackSettings.LastPlaylistName,
            StartupPlaybackSettings.LastTrackId,
            StartupPlaybackSettings.LastEntryIndex);

        if (!restoredPlaylist)
            StartupPlaybackSettings.ClearPlaylistContext();

        UpdatePlaylistStatus();
        LoadByExtension(path);
    }

    private void SavePlaybackSession(string path, string? trackId)
    {
        StartupPlaybackSettings.LastTrackPath = path;

        var playlist = _playlistController.ActivePlaylist;
        int entryIndex = _playlistController.CurrentEntryIndex;
        if (playlist == null || entryIndex < 0 || entryIndex >= playlist.Entries.Count)
        {
            StartupPlaybackSettings.ClearPlaylistContext();
            return;
        }

        StartupPlaybackSettings.LastPlaylistId = playlist.Id;
        StartupPlaybackSettings.LastPlaylistName = playlist.Name;
        StartupPlaybackSettings.LastTrackId = trackId ?? playlist.Entries[entryIndex].TrackId;
        StartupPlaybackSettings.LastEntryIndex = entryIndex;
    }

    private void SeekFromPlaybackService(float seconds)
    {
        if (_mode == PlayingMode.Midi && _chiptune != null)
        {
            _chiptune.SeekTo(seconds);
            UpdateProgress(seconds, _lastTotalSeconds);
            return;
        }

        if (_mode == PlayingMode.Mp3 && _mp3 != null)
        {
            _mp3.SeekTo(seconds);
            UpdateProgress(seconds, _mp3.DurationSeconds);
        }
    }

    // ── Routing por extensión ─────────────────────────────────────────────

    private void LoadByExtension(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".mp3")
            LoadMp3(path);
        else
            LoadMidi(path);
    }

    // ── Carga y reproducción de MIDI ───────────────────────────────────────

    private void LoadMidi(string path)
    {
        try
        {
            StopAndReset(keepFile: true);
            int myGeneration = _loadGeneration;

            _ = Task.Run(() =>
            {
                CompiledMidiSong compiledSong;
                ChiptuneEngineInput engineInput;

                try
                {
                    // Foundation-1: una sola representación MIDI neutral.
                    // Legacy se adapta desde ella; Lyra consumirá compiledSong
                    // directamente en Foundation-2/3.
                    compiledSong = MidiCompiler.Compile(path);
                    engineInput = new ChiptuneEngineInput(compiledSong);
                }
                catch (Exception ex)
                {
                    if (myGeneration != _loadGeneration) return; // ya quedó obsoleta
                    MainThread.BeginInvokeOnMainThread(() =>
                        SetStatus($"Error al parsear: {ex.Message}", "#f87171"));
                    return;
                }

                if (compiledSong.MusicalChannelCount == 0)
                {
                    if (myGeneration != _loadGeneration) return; // ya quedó obsoleta
                    MainThread.BeginInvokeOnMainThread(() =>
                        SetStatus("El archivo no contiene pistas con notas", "#f87171"));
                    return;
                }

                // Si mientras parseábamos el usuario ya disparó otra carga
                // (Next/Prev/Play), no tiene sentido gastar en crear el
                // engine — se descartaría de todos modos.
                if (myGeneration != _loadGeneration) return;

                var libraryTrack = _playlistController.AddToLibrary(path);

                // Aplicar la configuración de WaveType guardada para esta canción
                // ANTES de crear el engine, para que arranque ya con los timbres
                // elegidos la última vez (si los hay).
                if (libraryTrack != null)
                {
                    if (ChiptuneEngineSelection.DefaultEngine != ChiptuneEngineKind.DsnLike)
                        _waveTypeConfig.ApplyTo(libraryTrack.Id, engineInput);
                    _channelVolumeConfig.ApplyTo(libraryTrack.Id, engineInput);
                }

                // Estas referencias también deben existir al compilar los TFMs
                // no-Android, aunque esa rama termine en PlatformNotSupported.
                // Antes engineBuild vivía dentro de #if ANDROID y el código común
                // posterior quedaba fuera de alcance en iOS/MacCatalyst/Windows.
                ChiptuneEngineBuild engineBuild = null!;
                IChiptunePlayer engine = null!;
                TrackerModel model = null!;

#if ANDROID
                ChiptuneEngineKind selectedEngineKind =
                    ChiptuneEngineSelection.DefaultEngine;

                try
                {
                    engineBuild = ChiptunePlayerFactory.Create(
                        engineInput,
                        selectedEngineKind);
                    engine = engineBuild.Player;
                    model = engineBuild.TrackerModel;
                }
                catch (Exception ex)
                {
                    if (myGeneration != _loadGeneration) return;

                    System.Diagnostics.Debug.WriteLine(
                        $"[MIDIRift.Engine] Error creando {selectedEngineKind}: {ex}");

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        SetStatus(
                            $"Error al iniciar {selectedEngineKind}: {ex.Message}",
                            "#f87171");
                        PlayPauseButton.IsEnabled = true;
                    });
                    return;
                }
#else
                throw new PlatformNotSupportedException("ChiptunePlayerFactory no está disponible en esta plataforma.");
#endif

                // Esta carga quedó obsoleta mientras se creaba el engine: se
                // descarta sin arrancarlo, para no dejarlo sonando huérfano.
                if (myGeneration != _loadGeneration)
                {
                    engine.Dispose();
                    return;
                }

                var player = new TrackerPlayer(model, engine);

                engine.Speed = (float)SpeedSlider.Value;

                player.OnFrameTick += OnTrackerFrameTick;
                player.OnFinished += () => OnTrackFinished(myGeneration);

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    // Último chequeo, ya en el hilo de UI: si para cuando nos
                    // tocó correr ya hubo otro StopAndReset/carga más nueva,
                    // no asignamos ni arrancamos — solo liberamos el engine.
                    if (myGeneration != _loadGeneration)
                    {
                        engine.Dispose();
                        return;
                    }

                    _chiptune = engine;
                    _waveTypeControl = engineBuild.WaveTypes;
                    _channelMixer = engineBuild.Mixer;
                    _engineTelemetry = engineBuild.Telemetry;
#if ANDROID
                    if (_dsnLikePanel != null)
                    {
                        var dsn = engine as DsnLikeAudioTrackPlayer;
                        _dsnLikePanel.SetRealtimeSource(dsn is null ? null : () => dsn.GetRealtimeTelemetrySnapshot());
                    }
#endif
                    _trackerTimeline = new TrackerTimelineAdapter(player);
                    _player = player;
                    _mode = PlayingMode.Midi;
#if ANDROID
                    _currentMidiEngineKind = selectedEngineKind;
                    if (_trackerPanel != null)
                        _trackerPanel.WaveTypeEditingEnabled = engineBuild.Capabilities.Supports(EngineToolCapability.WaveTypeSelector);
#endif
                    ApplyEqGains();
                    _isPaused = false;
                    _currentPath = path;
                    _currentTrackId = libraryTrack?.Id;

                    if (_currentTrackId != null)
                        _playbackQueue.SyncCurrentEntry(_currentTrackId);

                    SavePlaybackSession(path, _currentTrackId);

#if ANDROID
                    ApplyActivePanel();
                    var waveTypes = engineBuild.WaveTypes?.WaveTypes.ToList();
                    _trackerPanel?.LoadGrid(
                        player.Grid,
                        model.ChannelCount,
                        FreqToNote,
                        waveTypes);

                    if (_trackerPanel != null && engine is DsnLikeAudioTrackPlayer dsnHeaders)
                    {
                        var percussionHeaders = Enumerable.Range(0, dsnHeaders.ChannelCount)
                            .Where(dsnHeaders.IsPercussionChannel);
                        _trackerPanel.SetPercussionChannels(percussionHeaders);
                    }

                    InitializeChannelVolumeControls(engine);
#endif

                    var trackTitle = libraryTrack?.Title ?? Path.GetFileNameWithoutExtension(path);
                    SetTrackTitle(trackTitle, isMidi: true);
                    UpdateProgress(0, 0);
                    UpdatePlaylistStatus();
                    SetPlayingState(true);
                    SetStatus(
                        $"Reproduciendo — {model.ChannelCount} canales • {(_currentMidiEngineKind switch { ChiptuneEngineKind.Lyra => "Lyra", ChiptuneEngineKind.DsnLike => "DSN-like", _ => "Classic / Legacy" })}",
                        "#7fd48f");

#if ANDROID
                    MediaSessionBridge.Start();
                    MediaSessionBridge.UpdateMetadata(trackTitle, isMidi: true);
#endif

                    player.Start();
                });
            });

            SetStatus("Cargando...", "#9e9ab8");
            PlayPauseButton.IsEnabled = false;
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}", "#f87171");
        }
    }

    private void OnTrackerFrameTick(float continuousRow)
    {
        // Durante un seek Lyra reconstruye scheduler/voices. No hay razón para
        // hacer polling visual del engine hasta que esa reconstrucción termine.
        if (_midiSeekInProgress)
            return;

        if (_isBackground)
        {
            // En segundo plano no hace falta granularidad de frame — nadie
            // ve el tracker ni la barra de progreso. Alcanza con ~1
            // actualización/seg para que la posición en la MediaSession
            // (lockscreen/auriculares) no quede clavada. Nos ahorramos el
            // resto: el highlight de fila del tracker, UpdateWaveTypes, y
            // 29 de cada 30 despachos a MediaSessionBridge.
            var now = DateTime.UtcNow;
            if (now - _lastBackgroundProgressUpdate < TimeSpan.FromSeconds(1))
                return;
            _lastBackgroundProgressUpdate = now;

            if (_chiptune != null)
            {
                float elapsed = _chiptune.VirtualSample / 44100f;
                float total = _chiptune.TotalSamples / 44100f;
                UpdateProgress(elapsed, total);
            }
            return;
        }

#if ANDROID
        if (_activePanel == ActivePanel.Tracker && _trackerPanel != null)
        {
            _trackerPanel.ContinuousRow = continuousRow;
            if (MeasurementLabelsSettings.Enabled)
            {
                var meterNow = DateTime.UtcNow;
                if (meterNow - _lastTrackerMeterUpdate >= TimeSpan.FromMilliseconds(100))
                {
                    _lastTrackerMeterUpdate = meterNow;
                    var source = GetPanelAudioSource();
                    if (source != null)
                    {
                        source.CopyMeterSamples(_trackerMeterSamples);
                        _trackerPanel.SetSignalMetrics(SignalMetricsCalculator.Calculate(_trackerMeterSamples));
                    }
                }
            }
        }
#endif

        if (_chiptune != null)
        {
            float elapsed = _chiptune.VirtualSample / 44100f;
            float total = _chiptune.TotalSamples / 44100f;
            UpdateProgress(elapsed, total);
        }

#if ANDROID
        if (_activePanel == ActivePanel.Tracker && _chiptune != null)
        {
            var now = DateTime.UtcNow;
            if (now - _lastWaveTypePoll >= WaveTypePollInterval)
            {
                _lastWaveTypePoll = now;
                if (_waveTypeControl != null)
                    _trackerPanel?.UpdateWaveTypes(_waveTypeControl.WaveTypes.ToList());
            }
        }
#endif
    }

    // ── Carga y reproducción de MP3 ─────────────────────────────────────────

    private void LoadMp3(string path)
    {
        try
        {
            StopAndReset(keepFile: true);
            int myGeneration = _loadGeneration;

            SetStatus("Cargando...", "#9e9ab8");
            PlayPauseButton.IsEnabled = false;

            _ = Task.Run(() =>
            {
                IMp3Player engine;
                try
                {
#if ANDROID
                    engine = Mp3PlayerFactory.Create(path);
#else
                    throw new PlatformNotSupportedException("Mp3PlayerFactory no está disponible en esta plataforma.");
#endif
                }
                catch (Exception ex)
                {
                    if (myGeneration != _loadGeneration) return; // ya quedó obsoleta
                    MainThread.BeginInvokeOnMainThread(() =>
                        SetStatus($"Error al abrir MP3: {ex.Message}", "#f87171"));
                    return;
                }

                // Ya se disparó otra carga mientras se abría el archivo: se
                // descarta este engine sin arrancarlo.
                if (myGeneration != _loadGeneration)
                {
                    engine.Dispose();
                    return;
                }

                var libraryTrack = _playlistController.AddToLibrary(path);

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    // Último chequeo, ya en el hilo de UI — ver comentario
                    // equivalente en LoadMidi.
                    if (myGeneration != _loadGeneration)
                    {
                        engine.Dispose();
                        return;
                    }

                    _mp3 = engine;
                    _mode = PlayingMode.Mp3;
                    ApplyEqGains();
                    _isPaused = false;
                    _currentPath = path;
                    _currentTrackId = libraryTrack?.Id;

                    if (_currentTrackId != null)
                        _playbackQueue.SyncCurrentEntry(_currentTrackId);

                    SavePlaybackSession(path, _currentTrackId);

                    _mp3.OnCompleted += () => MainThread.BeginInvokeOnMainThread(() => OnTrackFinished(myGeneration));
                    _mp3.Speed = (float)SpeedSlider.Value;

#if ANDROID
                    ApplyActivePanel();
#endif

                    var trackTitle = libraryTrack?.Title ?? Path.GetFileNameWithoutExtension(path);
                    SetTrackTitle(trackTitle, isMidi: false);
                    UpdateProgress(0, _mp3.DurationSeconds);
                    UpdatePlaylistStatus();
                    SetPlayingState(true);
                    SetStatus("Reproduciendo MP3", "#7fd48f");

#if ANDROID
                    MediaSessionBridge.Start();
                    MediaSessionBridge.UpdateMetadata(trackTitle, isMidi: false);
#endif

                    _mp3.Play();
                    StartMp3ProgressTimer(myGeneration);
                });
            });
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}", "#f87171");
        }
    }

    private void StartMp3ProgressTimer(int myGeneration)
    {
        _mp3ProgressTimer?.Stop();
        _mp3ProgressTimer = Dispatcher.CreateTimer();
        _mp3ProgressTimer.Interval = TimeSpan.FromMilliseconds(250);
        _mp3ProgressTimer.Tick += (_, _) =>
        {
            if (_mp3 == null) return;

            UpdateProgress(_mp3.PositionSeconds, _mp3.DurationSeconds);

            if (_mp3.IsFinished)
            {
                _mp3ProgressTimer?.Stop();
                OnTrackFinished(myGeneration);
            }
        };
        _mp3ProgressTimer.Start();
    }

#if ANDROID
    private void ShowMp3Placeholder()
    {
        TrackerContainer.Content = new Label
        {
            Text = "♪ Reproduciendo MP3 ♪",
            FontSize = 18,
            TextColor = Color.FromArgb("#5a566e"),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
    }
#endif

    // ── Fin de pista: repetir canción / avanzar playlist ────────────────────

    private void OnTrackFinished(int generation)
    {
        // EOF pertenece a una reproducción concreta. Si llegó tarde después
        // de un cambio manual/Next/Prev, o si otro detector ya consumió el
        // mismo EOF, no tiene permiso para tocar la cola.
        if (generation != _loadGeneration)
            return;

        if (_finishConsumedGeneration == generation)
            return;

        _finishConsumedGeneration = generation;

        if (_loopMode == 1)
        {
            // Repetir canción actual.
            //
            // Antes esto reusaba el motor de audio ya vivo (_player.Reset()+
            // Start() / _mp3.Reset()+Play()) en vez de recargar desde cero.
            // Ese camino depende de sincronizar correctamente el hilo de
            // render que recién está terminando (AudioTrack en pausa/flush,
            // hilo de decode/render viejo uniéndose al nuevo, estado de
            // canales) con el hilo de UI que dispara el reinicio — con
            // playlists de una sola canción, esa reutilización a veces
            // dejaba el AudioTrack "vivo" pero mudo (sin crashear, por eso
            // se sentía como una pausa) en vez de volver a sonar.
            //
            // LoadByExtension() ya resuelve esto para "repetir playlist"
            // (modo 2) recargando la canción entera de cero — que es
            // justamente el camino que SÍ funciona. Reusarlo acá para
            // "repetir canción" es más simple, comparte código con el
            // camino ya probado, y evita depender de reiniciar en caliente
            // el motor de audio.
            if (_currentPath != null)
                LoadByExtension(_currentPath);
            return;
        }

        // Modo 0 (apagado) o 2 (repetir playlist): NextAuto gestiona el wrap
        // según _playlistController._looping (fijado por OnLoopClicked).
        var nextTrack = _playbackQueue.AdvanceAfterFinished();
        if (nextTrack != null)
        {
            LoadByExtension(nextTrack.Path);
        }
        else
        {
            StopAndReset(keepFile: true);
            SetStatus("Reproducción terminada", "#9e9ab8");
#if ANDROID
            PlaybackLifecycleCoordinator.PlaybackFullyFinished();
            MediaSessionBridge.Stop();
#endif
        }
    }

    // ── Mezclador por canal MIDI ─────────────────────────────────────────

    private void InitializeChannelVolumeControls(IChiptunePlayer engine)
    {
        // El antiguo Picker queda oculto; el mezclador visual vive sobre el
        // TrackerPanel y comparte exactamente su desplazamiento horizontal.
        ChannelVolumeControls.IsVisible = false;
#if ANDROID
        if (_channelMixerPanel != null) _channelMixerPanel.IsVisible = false;
        if (_channelMixerResetButton != null) _channelMixerResetButton.IsVisible = false;
        if (_channelMixerToggleButton != null) _channelMixerToggleButton.IsVisible = false;
#endif
#if ANDROID
        RebuildChannelMixer(engine);
#endif
    }

#if ANDROID
    private void BuildTrackerHost()
    {
        if (_trackerPanel == null) return;

        _trackerHost = new Grid
        {
            IsClippedToBounds = true,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
        };
        _trackerHost.Children.Add(_trackerPanel);

        _channelMixerPanel = new Grid
        {
            // Alto suficiente para manipular cada fader con el pulgar sin
            // pelear contra una barra horizontal diminuta.
            HeightRequest = 286,
            VerticalOptions = LayoutOptions.End,
            HorizontalOptions = LayoutOptions.Fill,
            BackgroundColor = ThemePalette.Get("SurfaceOverlay"),
            IsVisible = false,
            IsClippedToBounds = true,
            ZIndex = 10,
        };

        _channelMixerStrip = new HorizontalStackLayout
        {
            Spacing = 0,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Fill,
        };
        _channelMixerPanel.Children.Add(_channelMixerStrip);
        _trackerHost.Children.Add(_channelMixerPanel);

        _channelMixerToggleButton = new Button
        {
            Text = "MIX",
            WidthRequest = 52,
            HeightRequest = 32,
            Padding = new Thickness(4, 0),
            FontSize = 10,
            CornerRadius = 8,
            BackgroundColor = ThemePalette.Get("ControlBackground"),
            TextColor = ThemePalette.Get("TextMuted"),
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.End,
            Margin = new Thickness(0, 0, 8, 8),
            ZIndex = 12,
        };
        _channelMixerToggleButton.Clicked += (_, _) => ToggleChannelMixer();
        _trackerHost.Children.Add(_channelMixerToggleButton);

        _channelMixerResetButton = new Button
        {
            Text = "RESET",
            WidthRequest = 58,
            HeightRequest = 32,
            Padding = new Thickness(4, 0),
            FontSize = 9,
            CornerRadius = 8,
            BackgroundColor = ThemePalette.Get("ControlBackground"),
            TextColor = ThemePalette.Get("TextMuted"),
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.End,
            Margin = new Thickness(0, 0, 66, 8),
            IsVisible = false,
            ZIndex = 12,
        };
        _channelMixerResetButton.Clicked += OnResetChannelVolumesClicked;
        _trackerHost.Children.Add(_channelMixerResetButton);

        TrackerContainer.Content = _trackerHost;
    }

    private void ApplyChannelMixerTheme()
    {
        if (_channelMixerPanel != null) _channelMixerPanel.BackgroundColor = ThemePalette.Get("SurfaceOverlay");
        foreach (var button in new[] { _channelMixerToggleButton, _channelMixerResetButton })
        {
            if (button == null) continue;
            button.BackgroundColor = ThemePalette.Get("ControlBackground");
            button.TextColor = ThemePalette.Get("TextMuted");
        }
        foreach (var slider in _channelMixerSliders)
        {
            slider.MinimumTrackColor = ThemePalette.Get("GreenPrimary");
            slider.MaximumTrackColor = ThemePalette.Get("ControlBackground");
            slider.ThumbColor = ThemePalette.Get("GreenAccent");
        }
        foreach (var label in _channelMixerValueLabels) label.TextColor = ThemePalette.Get("TextMuted");
    }

    private void ToggleChannelMixer()
    {
        _channelMixerExpanded = !_channelMixerExpanded;
        if (_channelMixerPanel != null)
            _channelMixerPanel.IsVisible = _channelMixerExpanded && _chiptune != null;
        if (_channelMixerResetButton != null)
            _channelMixerResetButton.IsVisible = _channelMixerExpanded && _chiptune != null;
        if (_channelMixerToggleButton != null)
            _channelMixerToggleButton.Text = _channelMixerExpanded ? "CERRAR" : "MIX";
        UpdateChromeVisibility();
        SyncChannelMixerScroll(_trackerPanel?.HorizontalScrollOffset ?? 0f);
    }

    private void RebuildChannelMixer(IChiptunePlayer engine)
    {
        if (_channelMixerStrip == null) return;

        _updatingChannelVolumeUi = true;
        try
        {
            _channelMixerStrip.Children.Clear();
            _channelMixerSliders.Clear();
            _channelMixerValueLabels.Clear();

            for (int channel = 0; channel < engine.ChannelCount; channel++)
            {
                int capturedChannel = channel;
                float gain = _channelMixer?.GetChannelGain(channel) ?? engine.GetChannelGain(channel);

                var title = new Label
                {
                    Text = $"CH {channel + 1}",
                    FontSize = 10,
                    TextColor = ThemePalette.Get("TextSecondary"),
                    HorizontalTextAlignment = TextAlignment.Center,
                };
                var value = new Label
                {
                    Text = $"{gain:0.00}×",
                    FontSize = 10,
                    TextColor = ThemePalette.Get("TextMuted"),
                    HorizontalTextAlignment = TextAlignment.Center,
                };
                var slider = new Slider
                {
                    Minimum = 0,
                    Maximum = 2,
                    Value = gain,
                    // MAUI no expone orientación vertical para Slider. Girarlo
                    // mantiene el control nativo y una zona táctil amplia.
                    Rotation = -90,
                    WidthRequest = 210,
                    HeightRequest = 44,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                    MinimumTrackColor = ThemePalette.Get("GreenPrimary"),
                    MaximumTrackColor = ThemePalette.Get("ControlBackground"),
                    ThumbColor = ThemePalette.Get("GreenAccent"),
                };
                slider.ValueChanged += (_, e) =>
                {
                    float newGain = (float)e.NewValue;
                    value.Text = $"{newGain:0.00}×";
                    if (_updatingChannelVolumeUi || _chiptune == null) return;
                    if ((uint)capturedChannel >= (uint)_chiptune.ChannelCount) return;
                    _channelMixer?.SetChannelGain(capturedChannel, newGain);
                    ScheduleChannelVolumeSave();
                };

                var sliderHost = new Grid
                {
                    HeightRequest = 222,
                    WidthRequest = TrackerPanel.ChannelColumnWidth,
                    HorizontalOptions = LayoutOptions.Center,
                    IsClippedToBounds = false,
                };
                sliderHost.Children.Add(slider);

                var cell = new VerticalStackLayout
                {
                    WidthRequest = TrackerPanel.ChannelColumnWidth,
                    Spacing = 1,
                    Padding = new Thickness(0, 6, 0, 4),
                    Children = { title, sliderHost, value },
                };

                _channelMixerSliders.Add(slider);
                _channelMixerValueLabels.Add(value);
                _channelMixerStrip.Children.Add(cell);
            }

            _channelMixerStrip.WidthRequest = engine.ChannelCount * TrackerPanel.ChannelColumnWidth;
            if (_channelMixerToggleButton != null)
                _channelMixerToggleButton.IsVisible = engine.ChannelCount > 0;
            if (_channelMixerPanel != null)
                _channelMixerPanel.IsVisible = _channelMixerExpanded && engine.ChannelCount > 0;
            if (_channelMixerResetButton != null)
                _channelMixerResetButton.IsVisible = _channelMixerExpanded && engine.ChannelCount > 0;
            SyncChannelMixerScroll(_trackerPanel?.HorizontalScrollOffset ?? 0f);
        }
        finally
        {
            _updatingChannelVolumeUi = false;
        }
    }

    private void OnTrackerHorizontalScrollChanged(float scrollX) =>
        Dispatcher.Dispatch(() => SyncChannelMixerScroll(scrollX));

    private void SyncChannelMixerScroll(float scrollX)
    {
        if (_channelMixerStrip != null)
            _channelMixerStrip.TranslationX = -scrollX;
    }
#endif

    private void OnChannelVolumeSelectionChanged(object sender, EventArgs e) { }
    private void OnChannelVolumeChanged(object sender, ValueChangedEventArgs e) { }

    private void OnResetChannelVolumesClicked(object? sender, EventArgs e)
    {
        if (_chiptune == null) return;

        _updatingChannelVolumeUi = true;
        try
        {
            for (int i = 0; i < _chiptune.ChannelCount; i++)
                _channelMixer?.SetChannelGain(i, 1f);
#if ANDROID
            for (int i = 0; i < _channelMixerSliders.Count; i++)
            {
                _channelMixerSliders[i].Value = 1f;
                if (i < _channelMixerValueLabels.Count)
                    _channelMixerValueLabels[i].Text = "1.00×";
            }
#endif
        }
        finally
        {
            _updatingChannelVolumeUi = false;
        }

        SaveCurrentChannelVolumes();
    }

    private void ScheduleChannelVolumeSave()
    {
        _channelVolumeSaveCts?.Cancel();
        var cts = new CancellationTokenSource();
        _channelVolumeSaveCts = cts;

        string? trackId = _currentTrackId;
        float[]? gains = _channelMixer?.GetChannelGains().ToArray();
        if (trackId == null || gains == null) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, cts.Token);
                if (!cts.IsCancellationRequested)
                    _channelVolumeConfig.Save(trackId, gains);
            }
            catch (OperationCanceledException) { }
            finally
            {
                if (ReferenceEquals(_channelVolumeSaveCts, cts))
                    _channelVolumeSaveCts = null;
                cts.Dispose();
            }
        });
    }

    private void SaveCurrentChannelVolumes()
    {
        _channelVolumeSaveCts?.Cancel();
        _channelVolumeSaveCts = null;
        if (_currentTrackId == null || _chiptune == null) return;
        _channelVolumeConfig.Save(_currentTrackId, _channelMixer.GetChannelGains().ToArray());
    }

    // ── Stop / Reset ──────────────────────────────────────────────────────

    private void StopAndReset(bool keepFile = false)
    {
        // Invalida cualquier LoadMidi/LoadMp3 que todavía esté parseando/
        // creando su engine en background — ver el comentario de
        // _loadGeneration más arriba.
        _loadGeneration++;

        _mp3ProgressTimer?.Stop();
        _mp3ProgressTimer = null;

#if ANDROID
        // Sin motor de audio no hay nada que capturar — soltamos la fuente
        // para que, si Spectrum/Oscilloscope estaban abiertos, vuelvan a su
        // estado "sin señal" en vez de seguir mostrando el último frame.
        _panelDataTimer?.Dispose();
        _panelDataTimer = null;
        _spectrumPanel?.Detach();
        _oscilloscopePanel?.Detach();
#endif

        _player?.Stop();
        _player = null;

        SaveCurrentChannelVolumes();
        _chiptune?.Stop();
        _chiptune?.Dispose();
        _chiptune = null;
        _waveTypeControl = null;
        _channelMixer = null;
        _engineTelemetry = null;
#if ANDROID
        _dsnLikePanel?.SetRealtimeSource(null);
#endif
        if (_trackerTimeline is IDisposable trackerTimelineDisposable) trackerTimelineDisposable.Dispose();
        _trackerTimeline = null;

        _mp3?.Stop();
        _mp3?.Dispose();
        _mp3 = null;

        _mode = PlayingMode.None;
        _isPaused = false;
        ChannelVolumeControls.IsVisible = false;
#if ANDROID
        if (_channelMixerPanel != null) _channelMixerPanel.IsVisible = false;
        if (_channelMixerResetButton != null) _channelMixerResetButton.IsVisible = false;
        if (_channelMixerToggleButton != null) _channelMixerToggleButton.IsVisible = false;
#endif

        SetPlayingState(false);
        UpdateProgress(0, 0);

        if (!keepFile)
        {
            PlayPauseButton.IsEnabled = false;
            SetStatus("Detenido", "#9e9ab8");
        }
        else
        {
            PlayPauseButton.IsEnabled = _currentPath != null;
        }
    }

    // ── UI helpers ────────────────────────────────────────────────────────

    private void SetPlayingState(bool playing)
    {
        PlayPauseButton.Text = string.Empty;
        PlayPauseButton.ImageSource = playing ? "icon_pause.svg" : "icon_play.svg";
        SemanticProperties.SetDescription(PlayPauseButton, playing ? "Pausar" : "Reproducir");
        PlayPauseButton.SetDynamicResource(Button.BackgroundColorProperty, "PurplePrimary");
        PlayPauseButton.IsEnabled = playing || _currentPath != null;
        StopButton.IsEnabled = playing || _isPaused;

#if ANDROID
        if (_currentPath != null)
            MediaSessionBridge.UpdateState(playing, _lastElapsedSeconds, _lastTotalSeconds);
#endif

        PushBridgeState(notify: true);
    }

    private void UpdateProgress(float elapsed, float total)
    {
        _lastElapsedSeconds = elapsed;
        _lastTotalSeconds = total;

        if (!_isBackground && !_isSeekingProgress)
        {
            // El Slider sí se actualiza cada tick (se ve la barra moverse
            // suave). Los labels de texto, en cambio, solo muestran mm:ss —
            // reasignar Label.Text hasta 120 veces/seg cuando el string
            // visible cambia como mucho 1 vez/seg era puro trabajo tirado:
            // cada asignación pasa por el binding pipeline de MAUI +
            // TextView.setText() nativo vía JNI. Se actualiza solo cuando el
            // segundo entero realmente cambió.
            ProgressSlider.Value = total > 0 ? Math.Clamp(elapsed / total, 0, 1) : 0;

            int elapsedSec = (int)elapsed;
            int totalSec = (int)total;
            if (elapsedSec != _lastDisplayedElapsedSec)
            {
                _lastDisplayedElapsedSec = elapsedSec;
                ElapsedLabel.Text = FormatTime(elapsed);
            }
            if (totalSec != _lastDisplayedTotalSec)
            {
                _lastDisplayedTotalSec = totalSec;
                TotalLabel.Text = FormatTime(total);
            }
        }

#if ANDROID
        if (_currentPath != null)
            MediaSessionBridge.UpdateState(IsActivelyPlaying, elapsed, total);
#endif

        // Los campos del bridge se actualizan siempre (asignación de campo,
        // no cuesta nada), pero NotifyChanged() —lo que efectivamente
        // dispara el refresco de UI en LibraryPage— se throttlea a ~1/seg.
        _playbackBridge.ElapsedSeconds = elapsed;
        _playbackBridge.TotalSeconds = total;
        var now = DateTime.UtcNow;
        if (now - _lastBridgeNotifyUtc >= TimeSpan.FromSeconds(1))
        {
            _lastBridgeNotifyUtc = now;
            PushBridgeState(notify: true);
        }
    }

    // ── Seek desde el ProgressSlider ─────────────────────────────────────
    // Mismo evento tanto para MIDI como para MP3: la única diferencia entre
    // motores es a qué SeekTo() se delega (IChiptunePlayer.SeekTo vs
    // IMp3Player.SeekTo — ambos con la misma firma, ver esas interfaces).
    // El "no pelear con el dedo" se resuelve enteramente con _isSeekingProgress
    // (ver su comentario junto a la declaración): UpdateProgress() no toca el
    // Slider mientras esté en true.

    private void OnProgressDragStarted(object sender, EventArgs e)
    {
        _isSeekingProgress = true;
    }

    /// <summary>
    /// Se dispara en cada frame de arrastre (y también, inofensivamente,
    /// cuando UpdateProgress asigna Value por código — por eso el guard: si
    /// no estamos en medio de un arrastre, este handler no hace nada).
    /// Solo actualiza el label de tiempo transcurrido como preview en vivo;
    /// el seek real ocurre recién en DragCompleted.
    /// </summary>
    private void OnProgressSliderValueChanged(object sender, ValueChangedEventArgs e)
    {
        if (!_isSeekingProgress) return;
        float preview = (float)e.NewValue * _lastTotalSeconds;
        ElapsedLabel.Text = FormatTime(preview);
    }

    private async void OnProgressDragCompleted(object sender, EventArgs e)
    {
        float target = (float)ProgressSlider.Value * _lastTotalSeconds;
        _isSeekingProgress = false;
        await SeekPlaybackAsync(target);
    }

    /// <summary>
    /// Ruta única de seek para el slider de la app y MediaSession. El seek
    /// MIDI se ejecuta fuera del hilo principal porque reconstruye el estado
    /// desde el inicio; MP3 puede delegarse directamente al decoder.
    /// </summary>
    private async Task SeekPlaybackAsync(float targetSeconds)
    {
        float duration = _lastTotalSeconds;
        float target = duration > 0f
            ? Math.Clamp(targetSeconds, 0f, duration)
            : Math.Max(0f, targetSeconds);

        if (_mode == PlayingMode.Midi)
        {
            var engine = _chiptune;
            if (engine == null) return;

            int generation =
                Interlocked.Increment(ref _midiSeekGeneration);

            _midiSeekInProgress = true;
            ProgressSlider.IsEnabled = false;

            await _midiSeekSerial.WaitAsync();
            try
            {
                // Si otro seek llegó mientras esperábamos, éste ya es viejo.
                // No gastamos CPU reconstruyendo un estado que será descartado.
                if (generation != Volatile.Read(ref _midiSeekGeneration))
                    return;

                await Task.Run(() => engine.SeekTo(target));

                if (generation == Volatile.Read(ref _midiSeekGeneration))
                {
                    UpdateProgress(target, duration);
#if ANDROID
                    MediaSessionBridge.UpdateState(
                        IsActivelyPlaying,
                        target,
                        duration);
#endif
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"MIDI seek failed: {ex}");
            }
            finally
            {
                _midiSeekSerial.Release();

                if (generation == Volatile.Read(ref _midiSeekGeneration))
                {
                    _midiSeekInProgress = false;
                    ProgressSlider.IsEnabled = true;
                }
            }
            return;
        }

        if (_mode == PlayingMode.Mp3 && _mp3 != null)
        {
            _mp3.SeekTo(target);
            UpdateProgress(target, duration);
#if ANDROID
            MediaSessionBridge.UpdateState(IsActivelyPlaying, target, duration);
#endif
        }
    }

    /// <summary>
    /// Vuelca el estado actual de reproducción al PlaybackBridge compartido,
    /// para que el mini reproductor flotante de LibraryPage (y el ícono de
    /// "sonando ahora" en sus filas) lo reflejen sin acoplarse a los
    /// internals de esta página.
    /// </summary>
    private void PushBridgeState(bool notify)
    {
        _playbackBridge.HasTrack = _currentPath != null;
        _playbackBridge.CurrentTrackId = _currentTrackId;
        _playbackBridge.IsPlaying = IsActivelyPlaying;
        _playbackBridge.Title = _currentTrackTitle;
        _playbackBridge.ElapsedSeconds = _lastElapsedSeconds;
        _playbackBridge.TotalSeconds = _lastTotalSeconds;

        _playbackBridge.Format = _mode switch
        {
            PlayingMode.Midi => "MIDI",
            PlayingMode.Mp3 => "MP3",
            _ => "",
        };
        _playbackBridge.Engine = _mode switch
        {
            PlayingMode.Midi => _currentMidiEngineKind == ChiptuneEngineKind.Lyra
                ? "Lyra"
                : "Classic",
            PlayingMode.Mp3 => "MP3 Decoder",
            _ => "",
        };
        _playbackBridge.SampleRate = _mode switch
        {
            PlayingMode.Midi => 44100, // misma constante que usa OnTrackerFrameTick para elapsed/total
            PlayingMode.Mp3 => _mp3?.SampleRate,
            _ => null,
        };
        _playbackBridge.Kbps = ComputeMp3Kbps();

        if (notify) _playbackBridge.NotifyChanged();
    }

    /// <summary>
    /// Bitrate aproximado del MP3 actual (tamaño de archivo / duración),
    /// ya que ni MediaExtractor ni IMp3Player exponen el bitrate declarado
    /// del stream directamente. Solo aplica en modo MP3 con duración conocida.
    /// </summary>
    private int? ComputeMp3Kbps()
    {
        if (_mode != PlayingMode.Mp3 || _mp3 == null || _currentPath == null) return null;
        if (_lastTotalSeconds <= 0) return null;

        try
        {
            var sizeBytes = new FileInfo(_currentPath).Length;
            return (int)(sizeBytes * 8L / 1000L / _lastTotalSeconds);
        }
        catch
        {
            return null;
        }
    }

    private void StartTrackInfoTimer()
    {
        if (_isBackground || AppLifecycleState.IsInBackground) return;

        if (_trackInfoTimer == null)
        {
            _trackInfoTimer = Dispatcher.CreateTimer();
            _trackInfoTimer.Interval = TimeSpan.FromMilliseconds(650);
            _trackInfoTimer.Tick += (_, _) =>
            {
                if (_isBackground || AppLifecycleState.IsInBackground)
                {
                    _trackInfoTimer?.Stop();
                    return;
                }

                _trackInfoTimerTicks++;
                AdvanceTrackTitleMarquee();

                // Igual que el mini reproductor, la segunda línea rota entre
                // datos útiles, pero a una cadencia baja para no generar ruido
                // de layout ni trabajo inútil.
                if (_trackInfoTimerTicks % 4 == 0)
                {
                    _trackInfoRotationIndex++;
                    RefreshTrackInfoSubtitle();
                }
            };
        }

        if (!_trackInfoTimer.IsRunning)
            _trackInfoTimer.Start();
    }

    private void SetTrackTitle(string? title, bool isMidi)
    {
        _currentTrackTitle = string.IsNullOrWhiteSpace(title)
            ? "Ningún archivo seleccionado"
            : title.Trim();
        _trackTitleOffset = 0;
        _trackInfoRotationIndex = 0;
        TrackCoverLabel.Text = isMidi ? "🎵" : "🎧";
        RefreshTrackInfoCard();
    }

    private void RefreshTrackInfoCard()
    {
        RefreshTrackTitleWindow();
        RefreshTrackInfoSubtitle();
    }

    private void AdvanceTrackTitleMarquee()
    {
        int capacity = GetTrackTitleCharacterCapacity();
        if (_currentTrackTitle.Length <= capacity)
        {
            if (FileNameLabel.Text != _currentTrackTitle)
                FileNameLabel.Text = _currentTrackTitle;
            _trackTitleOffset = 0;
            return;
        }

        string loop = _currentTrackTitle + "   •   ";
        _trackTitleOffset = (_trackTitleOffset + 1) % loop.Length;
        FileNameLabel.Text = SliceCircular(loop, _trackTitleOffset, capacity);
    }

    private void RefreshTrackTitleWindow()
    {
        int capacity = GetTrackTitleCharacterCapacity();
        if (_currentTrackTitle.Length <= capacity)
        {
            FileNameLabel.Text = _currentTrackTitle;
            return;
        }

        string loop = _currentTrackTitle + "   •   ";
        FileNameLabel.Text = SliceCircular(loop, _trackTitleOffset, capacity);
    }

    private int GetTrackTitleCharacterCapacity()
    {
        // El ancho real ya está disponible tras el primer layout. Antes de
        // eso usamos una estimación conservadora para evitar parpadeos.
        double width = FileNameLabel.Width;
        if (width <= 1) width = TrackInfoPanel.Width - 68;
        if (width <= 1) width = 230;

        return Math.Max(12, (int)(width / 7.2));
    }

    private static string SliceCircular(string value, int start, int length)
    {
        if (string.IsNullOrEmpty(value) || length <= 0) return string.Empty;

        var chars = new char[length];
        for (int i = 0; i < length; i++)
            chars[i] = value[(start + i) % value.Length];
        return new string(chars);
    }

    private void RefreshTrackInfoSubtitle()
    {
        if (_currentPath == null)
        {
            FileInfoLabel.Text = "";
            return;
        }

        var lines = new List<string>();
        lines.Add(_mode == PlayingMode.Midi ? "Chiptune Engine" : "MP3 Decoder");
        lines.Add($"{FormatTime(_lastElapsedSeconds)} / {FormatTime(_lastTotalSeconds)}");

        int? kbps = ComputeMp3Kbps();
        if (kbps is int bitrate) lines.Add($"{bitrate} kbps");
        lines.Add(_mode == PlayingMode.Midi ? "44100 Hz" : $"{_mp3?.SampleRate ?? 0} Hz");
        lines.Add(_mode == PlayingMode.Midi ? "MIDI" : "MP3");

        int idx = ((_trackInfoRotationIndex % lines.Count) + lines.Count) % lines.Count;
        FileInfoLabel.Text = lines[idx];
    }

    private void SetStatus(string text, string hexColor)
    {
        StatusLabel.Text = text;
        StatusLabel.TextColor = Color.FromArgb(hexColor);
    }

    private static string FormatTime(float seconds)
    {
        int s = (int)seconds;
        return $"{s / 60:D2}:{s % 60:D2}";
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
#if ANDROID
        MainActivity.UserInteraction -= OnAndroidUserInteraction;
        MainActivity.UserInteraction += OnAndroidUserInteraction;
        MainActivity.SetKeepScreenOn(true);
#endif
        if (_isLandscape && _landscapeChromeVisible)
            RestartLandscapeAutoHideTimer();
    }

    protected override void OnDisappearing()
    {
        StopLandscapeAutoHideTimer();
#if ANDROID
        MainActivity.UserInteraction -= OnAndroidUserInteraction;
        MainActivity.SetKeepScreenOn(false);
#endif
        base.OnDisappearing();
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────

    // Antes acá se llamaba StopAndReset() incondicionalmente. El problema:
    // OnDisappearing también se dispara cuando se empuja una página MODAL
    // encima de MainPage (por ejemplo, al abrir la playlist con
    // Navigation.PushModalAsync) — no solo cuando la app se cierra de
    // verdad. Eso cortaba la reproducción cada vez que el usuario abría la
    // playlist, aunque la intención fuera solo elegir una canción y volver.
    // La reproducción ahora sigue sonando de fondo mientras se navega dentro
    // de la misma app; StopAndReset() solo se llama explícitamente desde el
    // botón de Stop o al cargar una canción nueva.
    private async void OnEngineSettingsClicked(object sender, EventArgs e)
    {
        // La barra nativa de NavigationPage también forma parte del tema.
        // Antes estaba clavada a los colores Dark y sobrevivía como una
        // cintilla oscura incluso con MIDIRift en Light.
        await Navigation.PushModalAsync(new NavigationPage(new EngineSettingsPage())
        {
            BarBackgroundColor = ThemePalette.Get("SurfaceBackground"),
            BarTextColor = ThemePalette.Get("TextPrimary")
        });
    }

}