using Avalonia;
using Velopack;

namespace SpamDrafter.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack doit passer en premier : il gère l'installation, la désinstallation et les redémarrages
        // après mise à jour, puis rend la main (sans effet quand l'application n'est pas installée).
        VelopackApp.Build().Run();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
