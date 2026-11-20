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
        VelopackApp.Build().OnAfterUpdateFastCallback(_ => RefreshShortcutIcons()).Run();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Après une mise à jour : les raccourcis (Bureau, menu Démarrer) gardent parfois l'ancienne icône dans le cache de Windows.
    /// On réécrit leur icône (celle du programme à jour) puis on demande à Windows de rafraîchir son cache. Sans danger, jamais bloquant.
    /// </summary>
    private static void RefreshShortcutIcons()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            var script =
                "$exe=$args[0];$sh=New-Object -ComObject WScript.Shell;" +
                "$dirs=@([Environment]::GetFolderPath('Desktop'),[Environment]::GetFolderPath('CommonDesktopDirectory')," +
                "[Environment]::GetFolderPath('StartMenu'),[Environment]::GetFolderPath('Programs'));" +
                "foreach($d in $dirs){ if(-not $d -or -not (Test-Path $d)){continue}; " +
                "Get-ChildItem $d -Filter *.lnk -Recurse -ErrorAction SilentlyContinue | ForEach-Object { try { " +
                "$l=$sh.CreateShortcut($_.FullName); if($l.TargetPath -and ([IO.Path]::GetFileName($l.TargetPath) -eq 'SpamDrafter.exe' -or $l.TargetPath -like '*SpamDrafter*')){ " +
                "$l.IconLocation=$exe+',0'; $l.Save() } } catch {} } };" +
                "& ie4uinit.exe -show";
            var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-WindowStyle"); psi.ArgumentList.Add("Hidden");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add("& { " + script + " }");
            psi.ArgumentList.Add(exe);
            using var proc = System.Diagnostics.Process.Start(psi);
            proc?.WaitForExit(8000);
        }
        catch { /* l'icône est un confort : on n'empêche jamais la mise à jour */ }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
