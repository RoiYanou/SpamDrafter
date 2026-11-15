using System.Net;
using SpamDrafter.Core.DataPackages;
using SpamDrafter.Core.Updates;

namespace SpamDrafter.Core.Tests;

/// <summary>
/// Phase 10, lot D : source des données sur le web, comme GitHub Releases, qui répond à
/// « …/releases/download/donnees/fichier » par une redirection vers le vrai fichier.
/// </summary>
public class DataFeedWebTests
{
    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "spamdrafter-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void L_adresse_GitHub_des_donnees_suit_le_modele_des_releases()
    {
        Assert.Equal("https://github.com/RoiYanou/SpamDrafter/releases/download/donnees", UpdateSource.GitHubDataFeed);
    }

    [Fact]
    public async Task Les_donnees_se_telechargent_a_travers_une_redirection()
    {
        // Paquet factice publié dans un dossier servi par un petit serveur local.
        var kb = TempDir();
        File.WriteAllText(Path.Combine(kb, "manifest.json"), """{ "formatVersion": "1", "gameVersion": "1.30" }""");
        File.WriteAllText(Path.Combine(kb, "cards.json"), "[7]");
        var served = TempDir();
        DataPackageBuilder.Build(kb, 1, served);

        var port = Random.Shared.Next(20000, 40000);
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var redirects = 0;
        var server = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); } catch { return; }
                var path = ctx.Request.Url!.AbsolutePath;
                if (path.StartsWith("/releases/download/donnees/"))
                {
                    Interlocked.Increment(ref redirects);
                    ctx.Response.StatusCode = 302;
                    ctx.Response.RedirectLocation = "/objets/" + path["/releases/download/donnees/".Length..];
                }
                else if (path.StartsWith("/objets/") && File.Exists(Path.Combine(served, path["/objets/".Length..])))
                {
                    var bytes = await File.ReadAllBytesAsync(Path.Combine(served, path["/objets/".Length..]));
                    ctx.Response.ContentLength64 = bytes.Length;
                    await ctx.Response.OutputStream.WriteAsync(bytes);
                }
                else ctx.Response.StatusCode = 404;
                ctx.Response.Close();
            }
        });

        try
        {
            var store = new DataStore(Path.Combine(TempDir(), "data"));
            var updater = new DataUpdater(new DataFeed($"http://127.0.0.1:{port}/releases/download/donnees"), store);
            var entry = await updater.CheckAsync();
            Assert.NotNull(entry);
            await updater.DownloadAndInstallAsync(entry!);
            Assert.Equal("[7]", File.ReadAllText(Path.Combine(store.FindActive()!.Value.Directory, "cards.json")));
            Assert.True(redirects >= 2);   // index et paquet
        }
        finally
        {
            listener.Stop();
            await server;
        }
    }
}
