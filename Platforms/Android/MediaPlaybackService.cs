using System;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Graphics;
using Android.Graphics.Drawables;
using AndroidX.Core.App;
using AndroidX.Core.Graphics.Drawable;

// El binding de Xamarin.AndroidX.Media mezcla dos orígenes de paquete Java:
// - MediaSessionCompat / PlaybackStateCompat / MediaMetadataCompat vienen
//   del paquete legado "android.support.v4.media.*" (nunca se renombró).
// - MediaButtonReceiver y NotificationCompat.MediaStyle (androidx.media.app)
//   son incorporaciones más nuevas que sí usan el paquete real
//   "androidx.media.*".
// Por eso los alias de acá abajo apuntan a dos namespaces distintos.
using MediaSessionCompat = Android.Support.V4.Media.Session.MediaSessionCompat;
using PlaybackStateCompat = Android.Support.V4.Media.Session.PlaybackStateCompat;
using MediaMetadataCompat = Android.Support.V4.Media.MediaMetadataCompat;
using MediaButtonReceiver = AndroidX.Media.Session.MediaButtonReceiver;
using MediaNotificationCompat = AndroidX.Media.App.NotificationCompat;

namespace MIDIRift;

/// <summary>
/// Foreground service que hospeda la MediaSessionCompat de MIDIRift y la
/// notificación de reproducción (barra de notificaciones + pantalla de
/// bloqueo + botones de auriculares/Bluetooth).
///
/// No conoce nada de TrackerPlayer/ChiptuneAudioTrack/Mp3AudioPlayer — solo
/// recibe título/duración/posición/estado desde MainPage a través de
/// MediaSessionBridge (que vive en este mismo directorio) y traduce eso a
/// MediaSessionCompat + NotificationCompat.MediaStyle. Los comandos que
/// llegan desde fuera (notificación, lockscreen, botón de auriculares) se
/// reenvían hacia MainPage vía los eventos estáticos de MediaSessionBridge,
/// para que se ejecute exactamente la misma lógica que los botones propios
/// de la UI (OnPlayPauseClicked, OnNextClicked, etc.).
/// </summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeMediaPlayback)]
public class MediaPlaybackService : Service
{
    public const string ChannelId = "midirift_playback_channel";
    private const int NotificationId = 1001;

    public static MediaPlaybackService? Instance { get; private set; }

    private MediaSessionCompat? _session;
    private PlaybackStateCompat.Builder? _stateBuilder;
    private MediaMetadataCompat.Builder? _metadataBuilder;

    private string _title = "MIDIRift";
    private bool _isPlaying;
    private bool _isMidiTrack = true;
    private bool _taskWasRemoved;

    // Carátulas pequeñas generadas una sola vez. Evitamos cargar/decodificar
    // imágenes en cada PushNotification(), que puede ejecutarse una vez por
    // segundo mientras hay reproducción.
    private Bitmap? _midiArtwork;
    private Bitmap? _mp3Artwork;

    private static readonly global::Android.Graphics.Color NotificationPurple =
        global::Android.Graphics.Color.Rgb(103, 58, 183); // Material Deep Purple 500

    // ── Throttle de notificación ────────────────────────────────────────
    // UpdatePlaybackState() se llama en cada tick de progreso — hasta
    // TrackerPlayer.TargetFps veces por segundo (30-120) en modo MIDI.
    // Antes esto reconstruía la notificación completa (3x IconCompat +
    // Notification.Builder + MediaStyle) y llamaba StartForeground() en
    // cada tick: un IPC pesado hacia NotificationManagerService decenas de
    // veces por segundo, incluso con la app en segundo plano — exactamente
    // el tipo de comportamiento que un administrador de batería agresivo
    // (MIUI/HyperOS, EMUI, ColorOS, One UI) usa como excusa para congelar o
    // matar el proceso. La notificación visual se throttlea a como mucho 1
    // vez por segundo, salvo cambios de estado reales (play/pause, canción
    // nueva) que se reflejan al instante. La MediaSessionCompat (metadata +
    // playback state) tiene SU PROPIO throttle más abajo — ver
    // PlaybackStateThrottle: a diferencia de lo que se pensaba acá
    // originalmente, SetMetadata/SetPlaybackState SÍ son IPC hacia
    // system_server, no estado en memoria gratis.
    private static readonly TimeSpan NotificationThrottle = TimeSpan.FromSeconds(1);
    private DateTime _lastNotificationPush = DateTime.MinValue;
    private bool _lastPushedIsPlaying;

    // ── Throttle de MediaSession (SetMetadata/SetPlaybackState) ──────────
    // FIX (cuello de botella real del jank en TrackerPanel): SetMetadata y
    // SetPlaybackState de MediaSessionCompat NO son "solo estado en
    // memoria" — son llamadas Binder IPC hacia system_server (el mismo
    // canal que sincroniza lockscreen/Bluetooth AVRCP). UpdatePlaybackState
    // se llama desde OnTrackerFrameTick a TrackerPlayer.TargetFps (hasta
    // 120/seg) — eso eran hasta ~120 transacciones IPC/seg en el hilo de
    // UI, compitiendo directo con el paint del SKGLView del TrackerPanel.
    // PlaybackStateCompat ya soporta extrapolación: al incluir
    // positionMs + playbackSpeed, el sistema interpola solo la posición
    // entre updates (así lo recomienda la documentación de Android) — no
    // hace falta empujar esto a frame-rate, alcanza con throttlear igual
    // que ya se hace con la notificación. Los cambios de estado reales
    // (play/pause) siguen reflejándose al instante vía el "force".
    private static readonly TimeSpan PlaybackStateThrottle = TimeSpan.FromMilliseconds(250);
    private DateTime _lastPlaybackStatePush = DateTime.MinValue;
    private double _lastPushedDurationMs = -1;

    public override void OnCreate()
    {
        base.OnCreate();
        Instance = this;

        _session = new MediaSessionCompat(this, "MIDIRiftSession");
        _session.SetFlags(MediaSessionCompat.FlagHandlesMediaButtons | MediaSessionCompat.FlagHandlesTransportControls);
        _session.SetCallback(new SessionCallback());
        _session.Active = true;

        _stateBuilder = new PlaybackStateCompat.Builder()
            .SetActions(PlaybackStateCompat.ActionPlay
                        | PlaybackStateCompat.ActionPause
                        | PlaybackStateCompat.ActionPlayPause
                        | PlaybackStateCompat.ActionSkipToNext
                        | PlaybackStateCompat.ActionSkipToPrevious
                        | PlaybackStateCompat.ActionSeekTo
                        | PlaybackStateCompat.ActionStop);
        _session.SetPlaybackState(_stateBuilder.SetState(PlaybackStateCompat.StatePaused, 0, 1f).Build());

        _metadataBuilder = new MediaMetadataCompat.Builder();

        CreateNotificationChannel();

        // Cumple con el requisito de Android de llamar a StartForeground()
        // casi inmediatamente después de StartForegroundService(). Se
        // reemplaza por una notificación con datos reales en cuanto
        // MainPage llama a UpdateMetadata/UpdatePlaybackState.
        StartForeground(NotificationId, BuildNotification());
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        // Reenvía eventos de botones físicos de auriculares/Bluetooth
        // (KEYCODE_MEDIA_PLAY_PAUSE, etc.) al callback de la sesión.
        if (intent != null && _session != null)
            MediaButtonReceiver.HandleIntent(_session, intent);

        // El servicio nunca debe renacer solo sin el dueño del motor. Si el
        // proceso fue terminado por Android, un restart sticky produciría una
        // notificación huérfana pero no podría reconstruir AudioTrack ni la cola.
        return StartCommandResult.NotSticky;
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnTaskRemoved(Intent? rootIntent)
    {
        _taskWasRemoved = true;
        PlaybackLifecycleCoordinator.HandleTaskRemoved();

        // SystemUI can keep the QS tile cached after a task cleaner removes
        // MIDIRift. Force a refresh while we still have a process/context.
        MidiRiftQuickSettingsTileService.RequestRefresh(this);

        if (PlaybackCloseBehaviorSettings.Current == PlaybackCloseBehavior.StopImmediately)
        {
            StopForeground(StopForegroundFlags.Remove);
            StopSelf();
        }
        else
        {
            // Mantiene la sesión/notificación actual. El motor continúa siendo
            // propiedad de MainPage y no se crea ninguna segunda instancia.
            PushNotification(force: true);
        }

        base.OnTaskRemoved(rootIntent);
    }

    public void MarkTaskVisible()
    {
        _taskWasRemoved = false;
        PlaybackLifecycleCoordinator.MarkTaskVisible();
        PushNotification(force: true);
    }

    public override void OnDestroy()
    {
        _midiArtwork?.Recycle();
        _midiArtwork = null;
        _mp3Artwork?.Recycle();
        _mp3Artwork = null;

        _session?.Release();
        _session = null;
        Instance = null;
        base.OnDestroy();
    }

    private void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;

        var channel = new NotificationChannel(ChannelId, "Reproducción", NotificationImportance.Low)
        {
            Description = "Controles de reproducción de MIDIRift",
        };
        channel.SetShowBadge(false);

        (GetSystemService(NotificationService) as NotificationManager)?.CreateNotificationChannel(channel);
    }

    /// <summary>Título de la canción actual. Se llama al empezar a cargar cada pista.</summary>
    public void UpdateMetadata(string title, bool isMidi)
    {
        _title = title;
        _isMidiTrack = isMidi;
        var artwork = GetTrackArtwork();

        _metadataBuilder?.PutString(MediaMetadataCompat.MetadataKeyTitle, title);
        _metadataBuilder?.PutString(MediaMetadataCompat.MetadataKeyArtist, "MIDIRift");
        _metadataBuilder?.PutString(MediaMetadataCompat.MetadataKeyAlbum, isMidi ? "MIDI" : "MP3");
        _metadataBuilder?.PutBitmap(MediaMetadataCompat.MetadataKeyAlbumArt, artwork);
        _metadataBuilder?.PutBitmap(MediaMetadataCompat.MetadataKeyArt, artwork);
        _metadataBuilder?.PutBitmap(MediaMetadataCompat.MetadataKeyDisplayIcon, artwork);
        _session?.SetMetadata(_metadataBuilder?.Build());

        // Canción nueva: siempre se refleja al instante, sin throttle.
        PushNotification(force: true);
    }

    /// <summary>Reproduciendo/pausado + posición/duración en segundos. Se llama en cada tick de progreso.</summary>
    public void UpdatePlaybackState(bool isPlaying, double positionSeconds, double durationSeconds)
    {
        bool stateChanged = isPlaying != _isPlaying;
        _isPlaying = isPlaying;

        // FIX: SetMetadata/SetPlaybackState son IPC hacia system_server, no
        // "estado en memoria" — no se pueden llamar a frame-rate (hasta
        // 120/seg) sin trabar el hilo de UI. Se throttlean igual que la
        // notificación; el estado se sigue viendo fluido en lockscreen/BT
        // porque PlaybackStateCompat interpola la posición sola a partir de
        // positionMs + playbackSpeed entre nuestros updates.
        var now = DateTime.UtcNow;
        var durationMs = (long)(durationSeconds * 1000);
        bool durationChanged = durationMs != _lastPushedDurationMs;
        bool shouldPushState = stateChanged || durationChanged
            || (now - _lastPlaybackStatePush >= PlaybackStateThrottle);

        if (shouldPushState)
        {
            _lastPlaybackStatePush = now;

            if (durationChanged)
            {
                _lastPushedDurationMs = durationMs;
                _metadataBuilder?.PutLong(MediaMetadataCompat.MetadataKeyDuration, durationMs);
                _session?.SetMetadata(_metadataBuilder?.Build());
            }

            var state = isPlaying ? PlaybackStateCompat.StatePlaying : PlaybackStateCompat.StatePaused;
            var positionMs = (long)(positionSeconds * 1000);
            _session?.SetPlaybackState(_stateBuilder?.SetState(state, positionMs, isPlaying ? 1f : 0f).Build());
        }

        // Caro: reconstruye la notificación + StartForeground() (IPC hacia
        // NotificationManagerService). Play/pause se refleja al instante;
        // el resto (solo progreso) se throttlea.
        PushNotification(force: stateChanged);
    }

    private Notification BuildNotification()
    {
        var playPauseIcon = _isPlaying
            ? global::Android.Resource.Drawable.IcMediaPause
            : global::Android.Resource.Drawable.IcMediaPlay;

        var builder = new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle(_title)
            .SetContentText("MIDIRift")
            .SetContentIntent(BuildOpenAppPendingIntent())
            .SetSmallIcon(Resource.Mipmap.appicon)
            .SetLargeIcon(GetTrackArtwork())
            .SetColor(NotificationPurple.ToArgb())
            .SetColorized(true)
            .SetOnlyAlertOnce(true)
            .SetOngoing(_isPlaying)
            .SetVisibility((int)NotificationCompat.VisibilityPublic)
            .AddAction(BuildAction(global::Android.Resource.Drawable.IcMediaPrevious, "Anterior", PlaybackStateCompat.ActionSkipToPrevious))
            .AddAction(BuildAction(playPauseIcon, "Reproducir/Pausa", PlaybackStateCompat.ActionPlayPause))
            .AddAction(BuildAction(global::Android.Resource.Drawable.IcMediaNext, "Siguiente", PlaybackStateCompat.ActionSkipToNext));

        if (_session != null)
        {
            builder.SetStyle(new MediaNotificationCompat.MediaStyle()
                .SetMediaSession(_session.SessionToken)
                .SetShowActionsInCompactView(0, 1, 2));
        }

        return builder.Build();
    }


    private Bitmap GetTrackArtwork()
    {
        if (_isMidiTrack)
            return _midiArtwork ??= CreateTrackArtwork(isMidi: true);

        return _mp3Artwork ??= CreateTrackArtwork(isMidi: false);
    }

    /// <summary>
    /// Genera una carátula sencilla y estable para MediaStyle. Se usa un
    /// bitmap propio en vez de intentar decodificar el adaptive icon de MAUI,
    /// porque ciertos fabricantes no permiten usarlo como LargeIcon y lo
    /// muestran transparente o directamente lo descartan.
    /// </summary>
    private static Bitmap CreateTrackArtwork(bool isMidi)
    {
        const int size = 256;
        var bitmap = Bitmap.CreateBitmap(size, size, Bitmap.Config.Argb8888)!;
        using var canvas = new Canvas(bitmap);
        using var background = new global::Android.Graphics.Paint(PaintFlags.AntiAlias) { Color = NotificationPurple };
        canvas.DrawRoundRect(0, 0, size, size, 42, 42, background);

        using var accent = new global::Android.Graphics.Paint(PaintFlags.AntiAlias)
        {
            Color = global::Android.Graphics.Color.White,
            TextAlign = global::Android.Graphics.Paint.Align.Center
        };
        accent.SetTypeface(global::Android.Graphics.Typeface.Create(
            global::Android.Graphics.Typeface.Default,
            global::Android.Graphics.TypefaceStyle.Bold));

        if (isMidi)
        {
            // Teclado compacto: reconocible incluso en la carátula pequeña de
            // la notificación y sin depender de emojis/fuentes del sistema.
            accent.StrokeWidth = 8f;
            accent.SetStyle(global::Android.Graphics.Paint.Style.Stroke);
            canvas.DrawRoundRect(42, 73, 214, 183, 14, 14, accent);
            for (int i = 1; i < 7; i++)
            {
                float x = 42 + i * (172f / 7f);
                canvas.DrawLine(x, 75, x, 181, accent);
            }
            accent.SetStyle(global::Android.Graphics.Paint.Style.Fill);
            foreach (int i in new[] { 1, 2, 4, 5, 6 })
            {
                float keyWidth = 172f / 7f;
                float left = 42 + i * keyWidth - keyWidth * 0.28f;
                canvas.DrawRoundRect(left, 75, left + keyWidth * 0.56f, 137, 5, 5, accent);
            }
            accent.TextSize = 34f;
            canvas.DrawText("MIDI", size / 2f, 226, accent);
        }
        else
        {
            // Nota musical dibujada con primitivas para evitar depender de
            // glyphs que cambian entre fabricantes.
            accent.StrokeWidth = 18f;
            accent.StrokeCap = global::Android.Graphics.Paint.Cap.Round;
            canvas.DrawLine(148, 58, 148, 161, accent);
            canvas.DrawLine(148, 58, 205, 44, accent);
            canvas.DrawLine(205, 44, 205, 143, accent);
            canvas.DrawCircle(120, 166, 30, accent);
            canvas.DrawCircle(177, 148, 30, accent);
            accent.TextSize = 34f;
            canvas.DrawText("MP3", size / 2f, 226, accent);
        }

        return bitmap;
    }

    private PendingIntent BuildOpenAppPendingIntent()
    {
        // Reutiliza la Activity existente (MainActivity es SingleTop) en vez
        // de crear otra pila de navegación al tocar la notificación.
        var intent = new Intent(this, typeof(MainActivity));
        intent.SetAction(Intent.ActionMain);
        intent.AddCategory(Intent.CategoryLauncher);
        intent.SetFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);

        var flags = PendingIntentFlags.UpdateCurrent;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
            flags |= PendingIntentFlags.Immutable;

        return PendingIntent.GetActivity(this, 0, intent, flags)!;
    }

    private NotificationCompat.Action BuildAction(int icon, string title, long mediaAction)
    {
        var pendingIntent = MediaButtonReceiver.BuildMediaButtonPendingIntent(this, mediaAction);
        var iconCompat = IconCompat.CreateWithResource(this, icon);
        return new NotificationCompat.Action.Builder(iconCompat, new Java.Lang.String(title), pendingIntent).Build();
    }

    private void PushNotification(bool force = false)
    {
        var now = DateTime.UtcNow;
        if (!force && now - _lastNotificationPush < NotificationThrottle)
            return;

        _lastNotificationPush = now;
        _lastPushedIsPlaying = _isPlaying;

        var notification = BuildNotification();

        if (_isPlaying || (_taskWasRemoved && PlaybackCloseBehaviorSettings.Current != PlaybackCloseBehavior.StopImmediately))
        {
            // Al cerrar desde recientes en un modo de continuidad mantenemos
            // el servicio en foreground incluso si estaba pausado. Así el
            // sistema no elimina silenciosamente el dueño del estado antes
            // de que el usuario vuelva a abrir la app.
            StartForeground(NotificationId, notification);
        }
        else
        {
            // Pausado: seguimos mostrando la notificación (con ▶ para
            // reanudar) pero dejamos de ser "foreground" estrictamente, para
            // que el sistema pueda recuperar recursos si hace falta.
            StopForeground(StopForegroundFlags.Detach);
            NotificationManagerCompat.From(this).Notify(NotificationId, notification);
        }
    }

    /// <summary>
    /// Traduce comandos de la sesión (notificación, lockscreen, botón de
    /// auriculares) a los eventos estáticos de MediaSessionBridge, que
    /// MainPage escucha para ejecutar la misma lógica que sus propios
    /// botones de Play/Pause/Prev/Next/Stop.
    /// </summary>
    private class SessionCallback : MediaSessionCompat.Callback
    {
        public override void OnPlay() => MediaSessionBridge.RaisePlayPause();
        public override void OnPause() => MediaSessionBridge.RaisePlayPause();
        public override void OnSkipToNext() => MediaSessionBridge.RaiseNext();
        public override void OnSkipToPrevious() => MediaSessionBridge.RaisePrev();
        public override void OnStop() => MediaSessionBridge.RaiseStop();
        public override void OnSeekTo(long pos) => MediaSessionBridge.RaiseSeek(Math.Max(0L, pos));
    }
}