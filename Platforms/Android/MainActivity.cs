using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;

namespace MIDIRift
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        public static event Action? UserInteraction;
        private static WeakReference<MainActivity>? _current;
        private static bool _keepScreenOnRequested;

        // Fuente simple y fiable para el Quick Settings Tile. El proceso puede
        // seguir vivo por MediaPlaybackService aunque ya no exista una Activity
        // visible, así que no usamos "proceso vivo" como sinónimo de "app abierta".
        public static bool IsVisible { get; private set; }

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            // Do not let Android restore the previous Fragment/Activity state here.
            // MAUI rebuilds its own visual/navigation tree, and restoring stale state
            // after the task was removed while MediaPlaybackService stayed alive can
            // make the relaunched Activity close immediately.
            base.OnCreate(null);
            _current = new WeakReference<MainActivity>(this);
            ApplyKeepScreenOn();
        }

        public override bool DispatchTouchEvent(MotionEvent? ev)
        {
            if (ev?.ActionMasked == MotionEventActions.Down)
                UserInteraction?.Invoke();

            return base.DispatchTouchEvent(ev);
        }

        public static void SetKeepScreenOn(bool enabled)
        {
            _keepScreenOnRequested = enabled;
            if (_current != null && _current.TryGetTarget(out var activity))
                activity.RunOnUiThread(activity.ApplyKeepScreenOn);
        }

        private void ApplyKeepScreenOn()
        {
            if (_keepScreenOnRequested)
                Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
            else
                Window?.ClearFlags(WindowManagerFlags.KeepScreenOn);
        }

        // OnStart/OnStop reflejan visibilidad real (no foco): apagar la
        // pantalla con la app al frente NO dispara OnStop, solo OnPause.
        // Ver el comentario de AppLifecycleState para el porqué de esa
        // elección.
        protected override void OnStart()
        {
            base.OnStart();
            IsVisible = true;
            AppLifecycleState.SetInBackground(false);
            PlaybackLifecycleCoordinator.MarkTaskVisible();
            MediaSessionBridge.MarkTaskVisible();
            ApplyKeepScreenOn();
            MidiRiftQuickSettingsTileService.RequestRefresh(this);
        }

        protected override void OnStop()
        {
            base.OnStop();
            IsVisible = false;
            AppLifecycleState.SetInBackground(true);
            Window?.ClearFlags(WindowManagerFlags.KeepScreenOn);
            MidiRiftQuickSettingsTileService.RequestRefresh(this);
        }
    }
}
