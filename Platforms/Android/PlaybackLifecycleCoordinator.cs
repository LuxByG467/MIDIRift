using System;
using Microsoft.Maui.ApplicationModel;

namespace MIDIRift;

/// <summary>
/// Puente de ciclo de vida entre el foreground service y el único dueño de
/// los motores (MainPage). El servicio nunca crea motores: solo solicita al
/// dueño existente que los detenga. Así no aparece una ruta de audio paralela.
/// </summary>
public static class PlaybackLifecycleCoordinator
{
    private static readonly object Gate = new();
    private static Action? _stopAndRelease;

    public static bool TaskWasRemoved { get; private set; }
    public static bool StopWhenQueueFinishes { get; private set; }

    public static void RegisterOwner(Action stopAndRelease)
    {
        lock (Gate)
            _stopAndRelease = stopAndRelease;
    }

    public static void MarkTaskVisible()
    {
        TaskWasRemoved = false;
        StopWhenQueueFinishes = false;
    }

    public static void HandleTaskRemoved()
    {
        TaskWasRemoved = true;
        switch (PlaybackCloseBehaviorSettings.Current)
        {
            case PlaybackCloseBehavior.StopImmediately:
                RequestStopAndRelease();
                break;
            case PlaybackCloseBehavior.ContinueUntilFinished:
                StopWhenQueueFinishes = true;
                break;
            default:
                StopWhenQueueFinishes = false;
                break;
        }
    }

    public static void PlaybackFullyFinished()
    {
        if (StopWhenQueueFinishes)
        {
            StopWhenQueueFinishes = false;
            MediaSessionBridge.Stop();
        }
    }

    private static void RequestStopAndRelease()
    {
        Action? action;
        lock (Gate) action = _stopAndRelease;

        if (action == null)
        {
            MediaSessionBridge.Stop();
            return;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            action();
            MediaSessionBridge.Stop();
        });
    }
}
