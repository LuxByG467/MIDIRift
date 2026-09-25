using System;

namespace MIDIRift;

/// <summary>
/// Estado compartido de "¿la app está visible ahora mismo?", alimentado
/// desde el código específico de cada plataforma (en Android,
/// MainActivity.OnStart/OnStop). Vive en el proyecto compartido —sin
/// #if ANDROID— para que MainPage lo pueda consultar sin importar la
/// plataforma; en las plataformas que no lo alimentan (Desktop/iOS por
/// ahora) queda simplemente en false para siempre, que es el comportamiento
/// correcto (nunca "en segundo plano" para ellas todavía).
///
/// Se usa OnStart/OnStop (visibilidad real) y no OnResume/OnPause (foco):
/// apagar la pantalla con la app en primer plano dispara OnPause pero NO
/// OnStop, así que la reproducción sigue actualizando su UI a pantalla
/// completa como siempre — coincide con el comportamiento ya validado de
/// "pantalla apagada + app en primer plano = todo sigue igual". Recién
/// cuando el usuario cambia a otra app (Home, recientes, otra app) se
/// dispara OnStop y acá se apaga el trabajo de UI que no tiene sentido
/// mientras nadie la está mirando.
/// </summary>
public static class AppLifecycleState
{
    public static bool IsInBackground { get; private set; }

    /// <summary>Se dispara cuando cambia el estado, con el nuevo valor de IsInBackground.</summary>
    public static event Action<bool>? Changed;

    public static void SetInBackground(bool value)
    {
        if (IsInBackground == value) return;
        IsInBackground = value;
        Changed?.Invoke(value);
    }
}
