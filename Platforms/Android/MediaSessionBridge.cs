using System;
using Android.Content;
using AndroidX.Core.Content;
using Microsoft.Maui.ApplicationModel;

namespace MIDIRift;

/// <summary>
/// Punto único desde el que el código compartido (MainPage) controla los
/// controles nativos de reproducción de Android: arranca/detiene el
/// foreground service (MediaPlaybackService) y le reenvía título,
/// posición/duración y estado de reproducción para que los refleje en la
/// notificación con MediaStyle (barra de notificaciones, pantalla de
/// bloqueo, auriculares Bluetooth).
///
/// En sentido contrario, los eventos estáticos de acá abajo son cómo
/// MediaPlaybackService le avisa a MainPage que el usuario tocó
/// Play/Pausa/Siguiente/Anterior/Stop *desde fuera de la app* — MainPage
/// los escucha y ejecuta la misma lógica que sus propios botones, así que
/// el estado nunca se desincroniza entre la UI y la notificación.
///
/// Mismo patrón que ChiptunePlayerFactory/Mp3PlayerFactory: vive en
/// Platforms/Android sin necesidad de #if ANDROID interno (el propio
/// árbol de carpetas ya lo limita a la compilación Android), pero cada
/// llamada desde MainPage.xaml.cs sí debe envolverse en #if ANDROID porque
/// el tipo no existe en las demás plataformas.
/// </summary>
public static class MediaSessionBridge
{
    public static event Action? PlayPauseRequested;
    public static event Action? NextRequested;
    public static event Action? PrevRequested;
    public static event Action? StopRequested;
    public static event Action<long>? SeekRequested;

    internal static void RaisePlayPause() => PlayPauseRequested?.Invoke();
    internal static void RaiseNext() => NextRequested?.Invoke();
    internal static void RaisePrev() => PrevRequested?.Invoke();
    internal static void RaiseStop() => StopRequested?.Invoke();
    internal static void RaiseSeek(long positionMs) => SeekRequested?.Invoke(positionMs);

    /// <summary>
    /// Arranca (o, si ya está corriendo, simplemente "toca") el foreground
    /// service. Seguro de llamar en cada carga de pista: si el servicio ya
    /// existe no se recrea la sesión, solo se re-entrega el Intent.
    /// </summary>
    public static void Start()
    {
        var context = global::Android.App.Application.Context;
        var intent = new Intent(context, typeof(MediaPlaybackService));
        ContextCompat.StartForegroundService(context, intent);

        RequestBatteryExemptionOnce();
    }

    /// <summary>
    /// Pide, una única vez por instalación, la excepción de optimización de
    /// batería (ver comentario en BatteryOptimizationHelper). Se dispara acá
    /// —al arrancar reproducción— porque es el primer momento en que tiene
    /// sentido para el usuario ("necesito esto para seguir sonando aunque
    /// cambies de app"), y porque ya tenemos garantizado que la Activity
    /// está en primer plano (se llama desde LoadMidi/LoadMp3 en respuesta a
    /// una acción del usuario).
    /// </summary>
    private static void RequestBatteryExemptionOnce()
    {
        const string key = "battery_exemption_requested";
        if (Microsoft.Maui.Storage.Preferences.Get(key, false)) return;

        if (global::Android.App.Application.Context is not { } context) return;
        if (Platform.CurrentActivity is not { } activity) return;

        Microsoft.Maui.Storage.Preferences.Set(key, true);
        BatteryOptimizationHelper.RequestExemption(activity);
    }


    public static void MarkTaskVisible() =>
        MediaPlaybackService.Instance?.MarkTaskVisible();

    public static void UpdateMetadata(string title, bool isMidi) =>
        MediaPlaybackService.Instance?.UpdateMetadata(title, isMidi);

    public static void UpdateState(bool isPlaying, double positionSeconds, double durationSeconds) =>
        MediaPlaybackService.Instance?.UpdatePlaybackState(isPlaying, positionSeconds, durationSeconds);

    /// <summary>Detiene el servicio y hace desaparecer la notificación (Stop explícito o fin de playlist).</summary>
    public static void Stop()
    {
        var context = global::Android.App.Application.Context;
        context.StopService(new Intent(context, typeof(MediaPlaybackService)));
    }
}
