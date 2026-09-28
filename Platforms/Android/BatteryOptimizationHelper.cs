using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;

namespace MIDIRift;

/// <summary>
/// Pide la excepción de optimización de batería para que MIDIRift pueda
/// seguir avanzando la playlist en segundo plano.
///
/// El foreground service (MediaPlaybackService) alcanza en Android "de
/// fábrica", pero en MIUI/HyperOS, EMUI, ColorOS/FuntouchOS, One UI, etc.
/// el fabricante tiene su propio administrador de batería que congela
/// procesos en segundo plano IGNORANDO el foreground service estándar,
/// salvo que el usuario dé esta excepción explícita. Sin esto: la canción
/// que ya está sonando sigue (el audio nativo no depende del hilo
/// principal), pero la detección de "canción terminada" y el avance a la
/// siguiente pista —que sí dependen del hilo principal— se congelan junto
/// con el resto del proceso.
/// </summary>
public static class BatteryOptimizationHelper
{
    public static bool IsIgnoringBatteryOptimizations(Context context)
    {
        var pm = (PowerManager?)context.GetSystemService(Context.PowerService);
        return pm != null && pm.IsIgnoringBatteryOptimizations(context.PackageName);
    }

    /// <summary>
    /// Lanza el diálogo del sistema para pedir la excepción. No hace nada si
    /// ya la tiene. Debe llamarse desde una Activity (necesita mostrar UI).
    /// </summary>
    public static void RequestExemption(Activity activity)
    {
        if (IsIgnoringBatteryOptimizations(activity)) return;

        try
        {
            var intent = new Intent(Settings.ActionRequestIgnoreBatteryOptimizations);
            intent.SetData(Android.Net.Uri.Parse("package:" + activity.PackageName));
            activity.StartActivity(intent);
        }
        catch
        {
            // Algunos OEMs (ROMs modificadas) no implementan esta acción del
            // sistema o la bloquean; en ese caso no hay mucho más que hacer
            // desde código — el usuario tiene que ir manualmente a Ajustes.
        }
    }
}
