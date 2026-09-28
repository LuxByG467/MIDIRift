using Android.App;
using Android.Content;
using Android.OS;
using Android.Service.QuickSettings;

namespace MIDIRift;

/// <summary>
/// Quick Settings tile for opening MIDIRift. It deliberately does not depend
/// on a live MainActivity instance: SystemUI may recreate this TileService
/// after the application task/process has been removed.
/// </summary>
[Service(
    Name = "com.midirift.android.MidiRiftQuickSettingsTileService",
    Label = "MIDIRift",
    Permission = "android.permission.BIND_QUICK_SETTINGS_TILE",
    Exported = true,
    Icon = "@drawable/ic_qs_midirift")]
[IntentFilter(new[] { "android.service.quicksettings.action.QS_TILE" })]
public sealed class MidiRiftQuickSettingsTileService : TileService
{
    public override void OnStartListening()
    {
        base.OnStartListening();
        UpdateVisualState();
    }

    public override void OnClick()
    {
        base.OnClick();

        // Use an explicit Activity intent instead of asking PackageManager for
        // the launcher intent on every tap. This keeps the cold-start path as
        // short as possible and works even when the previous task/process was
        // removed by a cleaner.
        var intent = new Intent(this, typeof(MainActivity));
        intent.SetAction(Intent.ActionMain);
        intent.AddCategory(Intent.CategoryLauncher);
        intent.AddFlags(ActivityFlags.NewTask |
                        ActivityFlags.ClearTop |
                        ActivityFlags.SingleTop);

        if (Build.VERSION.SdkInt >= BuildVersionCodes.UpsideDownCake)
        {
            // Android 14+ requires the PendingIntent overload. SystemUI owns the
            // collapse animation, so the shade disappears as the Activity starts.
            var pendingIntent = PendingIntent.GetActivity(
                this,
                1101,
                intent,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

            if (pendingIntent is not null)
                StartActivityAndCollapse(pendingIntent);
        }
        else
        {
            // On Android 13 and older TileService can launch the explicit Intent
            // directly. Unlike PendingIntent.Send(), this API also asks SystemUI
            // to collapse Quick Settings immediately, matching a native QS tile.
#pragma warning disable CS0618 // Required compatibility path before Android 14.
            StartActivityAndCollapse(intent);
#pragma warning restore CS0618
        }
    }

    private void UpdateVisualState()
    {
        if (QsTile is null)
            return;

        QsTile.Label = "MIDIRift";
        QsTile.State = MainActivity.IsVisible ? TileState.Active : TileState.Inactive;
        QsTile.UpdateTile();
    }

    /// <summary>
    /// Asks SystemUI to bind the TileService again and refresh the tile. Safe to
    /// call when the Activity becomes visible/hidden or when its task is removed.
    /// </summary>
    public static void RequestRefresh(Context context)
    {
        try
        {
            var component = new ComponentName(
                context,
                Java.Lang.Class.FromType(typeof(MidiRiftQuickSettingsTileService)));
            RequestListeningState(context, component);
        }
        catch
        {
            // Quick Settings is optional. Never let a cosmetic refresh affect
            // playback or the Activity lifecycle.
        }
    }
}
