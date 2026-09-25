using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace MIDIRift;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            // Registra los handlers de SKCanvasView, SKGLView, etc.
            // Esto es lo que resuelve el HandlerNotFoundException para TrackerPanel
            // ya que hereda de SKCanvasView.
            .UseSkiaSharp()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // El logger de Debug (builder.Logging.AddDebug()) se sacó porque
        // requiere el paquete Microsoft.Extensions.Logging.Debug, que no
        // está referenciado en el proyecto. Si lo querés de vuelta: Admin.
        // de paquetes NuGet -> buscar "Microsoft.Extensions.Logging.Debug"
        // -> instalar (dejá que Visual Studio elija la versión, para no
        // repetir el mismo lío de conflictos de versiones que ya tuvimos
        // con AndroidX) -> descomentar el bloque de abajo.
        //
        // #if DEBUG
        //         builder.Logging.AddDebug();
        // #endif

        return builder.Build();
    }
}