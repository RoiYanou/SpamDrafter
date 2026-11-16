using System.IO.Compression;
using SpamDrafter.Core.DataPackages;

namespace SpamDrafter.Core.Tests;

/// <summary>Paquet de données séparé (phase 9, lot B) : fabrication, vérification, installation, mise à jour.</summary>
public class DataPackageTests
{
    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "spamdrafter-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    /// <summary>Petite base factice : manifeste, un JSON, une « image », un sous-dossier.</summary>
    private static string FakeKnowledge(string gameVersion = "1.30", string format = "1", string cards = "[1,2,3]")
    {
        var d = TempDir();
        File.WriteAllText(Path.Combine(d, "manifest.json"), $$"""{ "formatVersion": "{{format}}", "gameVersion": "{{gameVersion}}" }""");
        File.WriteAllText(Path.Combine(d, "cards.json"), cards);
        Directory.CreateDirectory(Path.Combine(d, "images", "cards"));
        File.WriteAllBytes(Path.Combine(d, "images", "cards", "a.png"), [1, 2, 3, 4, 5]);
        return d;
    }

    [Fact]
    public void BuildWritesZipAndIndex()
    {
        var feed = TempDir();
        var e = DataPackageBuilder.Build(FakeKnowledge(), 1, feed);
        Assert.Equal("1.30", e.GameVersion);
        Assert.Equal("SpamDrafter-donnees-1.30-r1.zip", e.File);
        Assert.Equal(DataPackage.Sha256(Path.Combine(feed, e.File)), e.Sha256);
        var index = DataPackage.ReadJson<PackageIndex>(Path.Combine(feed, DataPackage.IndexFile));
        Assert.Single(index.Packages);
        using var zip = ZipFile.OpenRead(Path.Combine(feed, e.File));
        Assert.Contains(zip.Entries, x => x.FullName == "images/cards/a.png");
        Assert.Contains(zip.Entries, x => x.FullName == DataPackage.ManifestFile);
    }

    [Trait("Categorie", "Lent")]
    [Fact]
    public async Task FirstInstallThenNewerRevision()
    {
        var feed = TempDir();
        var store = new DataStore(Path.Combine(TempDir(), "data"));
        var updater = new DataUpdater(new DataFeed(feed), store);

        Assert.Null(await updater.CheckAsync());                       // pas d'index : rien à faire
        DataPackageBuilder.Build(FakeKnowledge(), 1, feed);
        var first = await updater.CheckAsync();
        Assert.NotNull(first);                                         // rien d'installé : le paquet est proposé
        var installed = await updater.DownloadAndInstallAsync(first!);
        Assert.Equal(1, installed.Revision);
        var active = store.FindActive();
        Assert.NotNull(active);
        Assert.Equal("[1,2,3]", File.ReadAllText(Path.Combine(active!.Value.Directory, "cards.json")));
        Assert.Null(await updater.CheckAsync());                       // à jour
        Assert.False(Directory.Exists(Path.Combine(store.Root, ".telechargement")));

        DataPackageBuilder.Build(FakeKnowledge(cards: "[4]"), 2, feed);
        var second = await updater.CheckAsync();
        Assert.Equal(2, second!.Revision);
        await updater.DownloadAndInstallAsync(second);
        Assert.Equal("[4]", File.ReadAllText(Path.Combine(store.FindActive()!.Value.Directory, "cards.json")));
        Assert.Equal(2, DataPackage.ReadJson<PackageIndex>(Path.Combine(feed, DataPackage.IndexFile)).Packages.Count);

        Assert.Equal(1, store.CleanUp());                              // l'ancienne révision est supprimée
        Assert.Equal(["1.30-r2"], Directory.EnumerateDirectories(store.Root).Select(Path.GetFileName));
    }

    [Fact]
    public void DamagedZipIsRefusedAndActiveDataKept()
    {
        var feed = TempDir();
        var store = new DataStore(Path.Combine(TempDir(), "data"));
        var e1 = DataPackageBuilder.Build(FakeKnowledge(), 1, feed);
        store.Install(Path.Combine(feed, e1.File), e1.Sha256);
        var e2 = DataPackageBuilder.Build(FakeKnowledge(cards: "[9]"), 2, feed);
        var zip2 = Path.Combine(feed, e2.File);
        using (var f = File.OpenWrite(zip2)) { f.Seek(10, SeekOrigin.Begin); f.WriteByte(0xFF); }

        Assert.Throws<InvalidDataException>(() => store.Install(zip2, e2.Sha256));
        var active = store.FindActive();
        Assert.Equal(1, active!.Value.Manifest.Revision);
        Assert.Single(Directory.EnumerateDirectories(store.Root));   // pas de reste d'installation
    }

    [Fact]
    public void FileNotMatchingManifestIsRefused()
    {
        var feed = TempDir();
        var e = DataPackageBuilder.Build(FakeKnowledge(), 1, feed);
        var zipPath = Path.Combine(feed, e.File);
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update))
        {
            zip.GetEntry("cards.json")!.Delete();
            using var w = new StreamWriter(zip.CreateEntry("cards.json").Open());
            w.Write("[6,6,6]");   // même taille, autre contenu
        }
        var store = new DataStore(Path.Combine(TempDir(), "data"));
        var ex = Assert.Throws<InvalidDataException>(() => store.Install(zipPath, expectedSha256: null));
        Assert.Contains("cards.json", ex.Message);
        Assert.Null(store.FindActive());
    }

    [Fact]
    public void UnexpectedOrEscapingEntryIsRefused()
    {
        var feed = TempDir();
        var e = DataPackageBuilder.Build(FakeKnowledge(), 1, feed);
        var zipPath = Path.Combine(feed, e.File);
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update))
            using (var w = new StreamWriter(zip.CreateEntry("../dehors.txt").Open())) w.Write("x");
        var store = new DataStore(Path.Combine(TempDir(), "data"));
        Assert.Throws<InvalidDataException>(() => store.Install(zipPath, null));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(store.Root)!, "dehors.txt")));
    }

    [Fact]
    public void IncompatibleFormatIsIgnoredAndRefused()
    {
        var feed = TempDir();
        var e = DataPackageBuilder.Build(FakeKnowledge(format: "2"), 1, feed);
        var index = DataPackage.ReadJson<PackageIndex>(Path.Combine(feed, DataPackage.IndexFile));
        Assert.Null(index.Latest("1"));
        var store = new DataStore(Path.Combine(TempDir(), "data"));
        Assert.Throws<InvalidDataException>(() => store.Install(Path.Combine(feed, e.File), e.Sha256));
    }

    [Fact]
    public void LatestPrefersNewerGameVersionThenRevision()
    {
        var index = new PackageIndex(1, [
            new("1.30", 5, "1", "a", 0, ""), new("1.31", 1, "1", "b", 0, ""), new("1.9", 9, "1", "c", 0, ""),
        ]);
        Assert.Equal("1.31", index.Latest("1")!.GameVersion);   // 1.31 > 1.30 > 1.9 (numérique, pas alphabétique)
    }

    [Fact]
    public void MissingFileMakesActiveDataInvalid()
    {
        var feed = TempDir();
        var store = new DataStore(Path.Combine(TempDir(), "data"));
        var e = DataPackageBuilder.Build(FakeKnowledge(), 1, feed);
        store.Install(Path.Combine(feed, e.File), e.Sha256);
        var dir = store.FindActive()!.Value.Directory;
        File.Delete(Path.Combine(dir, "images", "cards", "a.png"));
        Assert.Null(store.FindActive());
        Assert.Equal(0, store.CleanUp());   // rien d'actif valide : on ne supprime pas les données
    }
}
