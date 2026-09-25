using Microsoft.Maui.Controls;
using System;

namespace MIDIRift;

/// <summary>
/// Barra de navegación inferior compartida por MainPage y LibraryPage.
///
/// Cada pantalla se construye una sola vez como ShellContent persistente
/// dentro de un TabBar (ver AppShell.xaml.cs) con rutas absolutas "//library"
/// y "//nowplaying". Esta barra no decide layout ni estado de reproducción,
/// solo navega entre esas dos rutas con Shell.Current.GoToAsync — Shell ya
/// se encarga de mantener viva la instancia de cada página (así MainPage
/// sigue reproduciendo de fondo mientras se navega a Biblioteca).
///
/// Comportamiento pedido: si el usuario toca el botón de la pantalla en la
/// que ya está, pasa automáticamente a la otra. Con solo dos pantallas eso
/// equivale a "cualquier botón lleva a la pantalla que no es la actual" —
/// se implementa así de forma explícita por botón (no como un genérico
/// "togglear"), para que agregar una tercera pantalla en el futuro sea
/// cuestión de sumar un caso, no de reescribir la regla.
/// </summary>
public partial class BottomNavBar : ContentView
{
    public static readonly BindableProperty ActiveScreenProperty =
        BindableProperty.Create(
            nameof(ActiveScreen),
            typeof(string),
            typeof(BottomNavBar),
            "",
            propertyChanged: (bindable, _, _) => ((BottomNavBar)bindable).UpdateHighlight());

    /// <summary>"Library" o "NowPlaying" — qué pantalla es la que aloja esta barra.</summary>
    public string ActiveScreen
    {
        get => (string)GetValue(ActiveScreenProperty);
        set => SetValue(ActiveScreenProperty, value);
    }


    public BottomNavBar()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateHighlight();
        UpdateHighlight();
    }

    public void SetCompactMode(bool compact)
    {
        double height = compact ? 42 : 58;
        double fontSize = compact ? 12 : 14;
        LibraryButton.HeightRequest = height;
        NowPlayingButton.HeightRequest = height;
        LibraryButton.FontSize = fontSize;
        NowPlayingButton.FontSize = fontSize;
    }

    private void UpdateHighlight()
    {
        bool onLibrary = ActiveScreen == "Library";

        ApplyButtonTheme(LibraryButton, onLibrary);
        ApplyButtonTheme(NowPlayingButton, !onLibrary);
    }

    private static void ApplyButtonTheme(Button button, bool active)
    {
        button.SetDynamicResource(Button.BackgroundColorProperty, active ? "PurplePrimary" : "SurfaceBackground");
        if (active)
        {
            button.RemoveDynamicResource(Button.TextColorProperty);
            button.TextColor = Colors.White;
        }
        else
        {
            button.SetDynamicResource(Button.TextColorProperty, "TextSecondary");
        }
    }

    private async void OnLibraryClicked(object sender, EventArgs e) =>
        await GoTo(target: "Library", route: "//library");

    private async void OnNowPlayingClicked(object sender, EventArgs e) =>
        await GoTo(target: "NowPlaying", route: "//nowplaying");

    /// <summary>
    /// Si el botón tocado corresponde a la pantalla donde ya estamos,
    /// vamos a la otra en su lugar — ver comentario de clase.
    /// </summary>
    private async System.Threading.Tasks.Task GoTo(string target, string route)
    {
        bool alreadyThere = ActiveScreen == target;
        string destination = alreadyThere
            ? (target == "Library" ? "//nowplaying" : "//library")
            : route;

        if (Shell.Current != null)
            await Shell.Current.GoToAsync(destination);
    }
}
