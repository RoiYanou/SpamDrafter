using System.IO.Compression;
using SpamDrafter.Core.DataPackages;

namespace SpamDrafter.Core.Tests;

/// <summary>Phase 10, lot B (D-060) : import d'un paquet de données depuis un fichier, sans source de mises à jour.</summary>
public class DataImportTests
{
    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "spamdrafter-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    private static string Package(int revision, string cards = "[1,2,3]", string gameVersion = "1.30")
    {
        var kb = TempDir();
        File.WriteAllText(Path.Combine(kb, "manifest.json"), $$"""{ "formatVersion": "1", "gameVersion": "{{gameVersion}}" }""");
        File.WriteAllText(Path.Combine(kb, "cards.json"), cards);
        Directory.CreateDirectory(Path.Combine(kb, "images", "cards"));
        File.WriteAllBytes(Path.Combine(kb, "images", "cards", "a.webp"), [1, 2, 3]);
        var feed = TempDir();
        var e = DataPackageBuilder.Build(kb, revision, feed);
        return Path.Combine(feed, e.File);
    }

    [Fact]
    public void Un_paquet_choisi_par_le_joueur_s_installe_sans_source()
    {
        var store = new DataStore(Path.Combine(TempDir(), "data"));
        var m = store.ImportFile(Package(4));
        Assert.Equal(4, m.Revision);
        var active = store.FindActive();
        Assert.NotNull(active);
        Assert.Equal("[1,2,3]", File.ReadAllText(Path.Combine(active!.Value.Directory, "cards.json")));
    }

    [Fact]
    public void Un_paquet_plus_recent_remplace_un_paquet_egal_ou_ancien_est_refuse()
    {
        var store = new DataStore(Path.Combine(TempDir(), "data"));
        store.ImportFile(Package(3));
        Assert.Throws<InvalidDataException>(() => store.ImportFile(Package(3, "[9]")));
        Assert.Throws<InvalidDataException>(() => store.ImportFile(Package(2, "[9]")));
        Assert.Equal("[1,2,3]", File.ReadAllText(Path.Combine(store.FindActive()!.Value.Directory, "cards.json")));

        store.ImportFile(Package(1, "[5]", gameVersion: "1.31"));   // version de SPAM plus récente
        Assert.Equal("[5]", File.ReadAllText(Path.Combine(store.FindActive()!.Value.Directory, "cards.json")));
    }

    [Fact]
    public void Un_fichier_qui_n_est_pas_un_paquet_est_refuse_proprement()
    {
        var store = new DataStore(Path.Combine(TempDir(), "data"));
        var dir = TempDir();
        var notZip = Path.Combine(dir, "texte.zip");
        File.WriteAllText(notZip, "pas une archive");
        var e1 = Assert.Throws<InvalidDataException>(() => store.ImportFile(notZip));
        Assert.Contains("SpamDrafter", e1.Message);

        var otherZip = Path.Combine(dir, "autre.zip");
        using (var z = ZipFile.Open(otherZip, ZipArchiveMode.Create)) z.CreateEntry("photo.jpg");
        var e2 = Assert.Throws<InvalidDataException>(() => store.ImportFile(otherZip));
        Assert.Contains("paquet.json", e2.Message);
        Assert.Null(store.FindActive());
    }

    [Fact]
    public void Un_paquet_modifie_est_refuse_par_les_empreintes()
    {
        var store = new DataStore(Path.Combine(TempDir(), "data"));
        var zip = Package(1);
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Update))
        {
            z.GetEntry("cards.json")!.Delete();
            using var w = new StreamWriter(z.CreateEntry("cards.json").Open());
            w.Write("[666]");
        }
        Assert.Throws<InvalidDataException>(() => store.ImportFile(zip));
        Assert.Null(store.FindActive());
    }
}
