using System.IO.Compression;
using SpamDrafter.Core.Knowledge;

namespace SpamDrafter.Core.DataPackages;

/// <summary>Fabrique un paquet de données à partir d'un dossier knowledge/&lt;version&gt;/ (utilisé par le Studio).</summary>
public static class DataPackageBuilder
{
    /// <summary>Crée le .zip dans <paramref name="outputDir"/> et met à jour donnees.json. Renvoie l'entrée publiée.</summary>
    public static PackageEntry Build(string knowledgeDir, int revision, string outputDir, DateTime? now = null)
    {
        if (revision < 1) throw new ArgumentOutOfRangeException(nameof(revision), "La révision commence à 1.");
        var manifestPath = Path.Combine(knowledgeDir, "manifest.json");
        if (!File.Exists(manifestPath)) throw new FileNotFoundException("manifest.json absent", manifestPath);
        var kb = DataPackage.ReadJson<Manifest>(manifestPath);

        var files = Directory.EnumerateFiles(knowledgeDir, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(knowledgeDir, f).Replace('\\', '/'))
            .Where(r => r != DataPackage.ManifestFile)
            .OrderBy(r => r, StringComparer.Ordinal)
            .Select(r =>
            {
                var full = Path.Combine(knowledgeDir, r);
                return new PackageFile(r, new FileInfo(full).Length, DataPackage.Sha256(full));
            })
            .ToList();
        var manifest = new PackageManifest(DataPackage.PackageFormat, kb.GameVersion, revision, kb.FormatVersion,
            now ?? DateTime.UtcNow, files);

        Directory.CreateDirectory(outputDir);
        var name = DataPackage.FileName(kb.GameVersion, revision);
        var zipPath = Path.Combine(outputDir, name);
        var tmp = zipPath + ".partial";
        if (File.Exists(tmp)) File.Delete(tmp);
        using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            foreach (var f in files)
            {
                // Les images sont déjà compressées : les stocker telles quelles est bien plus rapide.
                var stored = f.Path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                          || f.Path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                          || f.Path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);
                zip.CreateEntryFromFile(Path.Combine(knowledgeDir, f.Path), f.Path,
                    stored ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
            }
            var entry = zip.CreateEntry(DataPackage.ManifestFile, CompressionLevel.Optimal);
            using var w = entry.Open();
            System.Text.Json.JsonSerializer.Serialize(w, manifest, DataPackage.Json);
        }
        File.Move(tmp, zipPath, overwrite: true);

        var published = new PackageEntry(kb.GameVersion, revision, kb.FormatVersion, name,
            new FileInfo(zipPath).Length, DataPackage.Sha256(zipPath));
        var indexPath = Path.Combine(outputDir, DataPackage.IndexFile);
        var packages = File.Exists(indexPath)
            ? DataPackage.ReadJson<PackageIndex>(indexPath).Packages
                .Where(p => !(p.GameVersion == kb.GameVersion && p.Revision == revision)).ToList()
            : new List<PackageEntry>();
        packages.Add(published);
        DataPackage.WriteJson(indexPath, new PackageIndex(DataPackage.PackageFormat, packages));
        return published;
    }
}
