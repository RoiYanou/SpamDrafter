using System.IO.Compression;
using SpamDrafter.Core.Knowledge;

namespace SpamDrafter.Core.DataPackages;

/// <summary>
/// Données installées sur le PC du joueur : un dossier par paquet (« 1.30-r1 ») dans <see cref="Root"/>,
/// et « actif.json » qui désigne celui à utiliser. Hors du dossier du programme : une mise à jour
/// du programme n'y touche pas.
/// </summary>
public sealed class DataStore
{
    public const string ActiveFile = "actif.json";
    private const string InstallPrefix = ".installation-";
    private sealed record Active(string Folder);

    public DataStore(string root) { Root = root; }
    public string Root { get; }

    /// <summary>%LOCALAPPDATA%\SpamDrafter\data, ou la variable SPAMDRAFTER_DATA (essais, sans toucher aux données installées).</summary>
    public static string DefaultRoot =>
        Environment.GetEnvironmentVariable("SPAMDRAFTER_DATA") is { Length: > 0 } custom ? custom
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpamDrafter", "data");

    /// <summary>Le paquet actif s'il est complet (fichiers présents, bonnes tailles) et lisible par ce programme.</summary>
    public (string Directory, PackageManifest Manifest)? FindActive()
    {
        try
        {
            var activePath = Path.Combine(Root, ActiveFile);
            if (!File.Exists(activePath)) return null;
            var dir = Path.Combine(Root, DataPackage.ReadJson<Active>(activePath).Folder);
            return CheckQuick(dir, out var manifest) is null ? (dir, manifest!) : null;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Contrôle rapide (sans empreintes) : renvoie le problème trouvé, ou null si tout va bien.</summary>
    public static string? CheckQuick(string dir, out PackageManifest? manifest)
    {
        manifest = null;
        var mp = Path.Combine(dir, DataPackage.ManifestFile);
        if (!File.Exists(mp)) return "paquet.json absent";
        manifest = DataPackage.ReadJson<PackageManifest>(mp);
        var problem = CheckFormats(manifest);
        if (problem is not null) return problem;
        foreach (var f in manifest.Files)
        {
            var fi = new FileInfo(Path.Combine(dir, f.Path));
            if (!fi.Exists) return $"fichier manquant : {f.Path}";
            if (fi.Length != f.Size) return $"fichier abîmé : {f.Path}";
        }
        return null;
    }

    private static string? CheckFormats(PackageManifest m)
    {
        if (m.PackageFormat != DataPackage.PackageFormat) return $"format de paquet {m.PackageFormat} non pris en charge";
        if (m.KnowledgeFormat != KnowledgeBase.SupportedFormatVersion)
            return $"ces données demandent une autre version de SpamDrafter (format {m.KnowledgeFormat}, attendu {KnowledgeBase.SupportedFormatVersion})";
        return null;
    }

    /// <summary>Lit paquet.json dans un .zip sans l'extraire.</summary>
    public static PackageManifest ReadManifest(string zipPath)
    {
        ZipArchive zip;
        try { zip = ZipFile.OpenRead(zipPath); }
        catch (InvalidDataException e)
        {
            throw new InvalidDataException("Ce fichier n'est pas un paquet de données SpamDrafter (archive .zip illisible).", e);
        }
        using var _ = zip;
        var me = zip.GetEntry(DataPackage.ManifestFile)
                 ?? throw new InvalidDataException("Ce fichier n'est pas un paquet de données SpamDrafter (paquet.json absent).");
        using var s = me.Open();
        return System.Text.Json.JsonSerializer.Deserialize<PackageManifest>(s, DataPackage.Json)
               ?? throw new InvalidDataException("paquet.json invalide.");
    }

    /// <summary>
    /// Importe un paquet choisi par le joueur (D-060, installation sans accès à la source des mises à jour).
    /// Mêmes contrôles qu'un téléchargement (formats, empreinte de chaque fichier, chemins), sauf l'empreinte du .zip
    /// qui n'est connue que par donnees.json. Refuse un paquet qui n'est pas plus récent que les données installées.
    /// </summary>
    public PackageManifest ImportFile(string zipPath, IProgress<int>? progress = null)
    {
        var m = ReadManifest(zipPath);
        if (CheckFormats(m) is { } problem) throw new InvalidDataException("Paquet refusé : " + problem + ".");
        if (FindActive() is { } active && !IsNewer(m, active.Manifest))
            throw new InvalidDataException($"Ces données (SPAM {m.GameVersion}, révision {m.Revision}) ne sont pas plus récentes " +
                                           $"que celles déjà installées (SPAM {active.Manifest.GameVersion}, révision {active.Manifest.Revision}).");
        return Install(zipPath, null, progress);
    }

    public static bool IsNewer(PackageManifest candidate, PackageManifest installed)
    {
        var c = DataPackage.ParseGameVersion(candidate.GameVersion);
        var i = DataPackage.ParseGameVersion(installed.GameVersion);
        return c > i || (c == i && candidate.Revision > installed.Revision);
    }

    /// <summary>
    /// Installe un paquet téléchargé : contrôle l'empreinte du .zip, extrait dans un dossier provisoire,
    /// contrôle l'empreinte de chaque fichier, puis l'active. En cas d'échec, les données actives restent intactes.
    /// </summary>
    public PackageManifest Install(string zipPath, string? expectedSha256, IProgress<int>? progress = null)
    {
        if (expectedSha256 is not null &&
            !string.Equals(DataPackage.Sha256(zipPath), expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Empreinte du paquet incorrecte : fichier abîmé ou incomplet.");

        Directory.CreateDirectory(Root);
        var tmp = Path.Combine(Root, InstallPrefix + Guid.NewGuid().ToString("N"));
        var tmpFull = Path.GetFullPath(tmp) + Path.DirectorySeparatorChar;
        try
        {
            PackageManifest manifest;
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                var me = zip.GetEntry(DataPackage.ManifestFile) ?? throw new InvalidDataException("paquet.json absent du paquet.");
                using (var s = me.Open())
                    manifest = System.Text.Json.JsonSerializer.Deserialize<PackageManifest>(s, DataPackage.Json)
                        ?? throw new InvalidDataException("paquet.json invalide.");
                if (CheckFormats(manifest) is { } problem) throw new InvalidDataException("Paquet refusé : " + problem + ".");

                var expected = manifest.Files.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
                var done = 0;
                foreach (var e in zip.Entries)
                {
                    if (e.FullName.EndsWith('/')) continue;
                    var target = Path.GetFullPath(Path.Combine(tmp, e.FullName));
                    if (!target.StartsWith(tmpFull, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"Chemin interdit dans le paquet : {e.FullName}");
                    if (e.FullName != DataPackage.ManifestFile && !expected.Contains(e.FullName))
                        throw new InvalidDataException($"Fichier inattendu dans le paquet : {e.FullName}");
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    e.ExtractToFile(target);
                    progress?.Report(++done * 90 / Math.Max(1, zip.Entries.Count));
                }
            }
            foreach (var f in manifest.Files)
            {
                var path = Path.Combine(tmp, f.Path);
                if (!File.Exists(path)) throw new InvalidDataException($"Fichier manquant dans le paquet : {f.Path}");
                if (new FileInfo(path).Length != f.Size || DataPackage.Sha256(path) != f.Sha256)
                    throw new InvalidDataException($"Empreinte incorrecte : {f.Path}");
            }
            var kb = DataPackage.ReadJson<Manifest>(Path.Combine(tmp, "manifest.json"));
            if (kb.GameVersion != manifest.GameVersion)
                throw new InvalidDataException($"Version incohérente : paquet {manifest.GameVersion}, données {kb.GameVersion}.");
            progress?.Report(100);

            var folder = DataPackage.FolderName(manifest.GameVersion, manifest.Revision);
            var dest = Path.Combine(Root, folder);
            if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
            Directory.Move(tmp, dest);
            DataPackage.WriteJson(Path.Combine(Root, ActiveFile), new Active(folder));
            return manifest;
        }
        finally
        {
            if (Directory.Exists(tmp)) TryDelete(tmp);
        }
    }

    /// <summary>Supprime les anciens paquets et les installations interrompues (au démarrage, avant tout chargement).</summary>
    public int CleanUp()
    {
        if (!Directory.Exists(Root)) return 0;
        var active = FindActive()?.Directory;
        var removed = 0;
        foreach (var d in Directory.EnumerateDirectories(Root))
        {
            var name = Path.GetFileName(d);
            var isActive = active is not null &&
                string.Equals(Path.GetFullPath(d), Path.GetFullPath(active), StringComparison.OrdinalIgnoreCase);
            // Sans paquet actif valide, on ne jette que les restes d'installation.
            var drop = !isActive && (active is not null || name.StartsWith(InstallPrefix) || name == ".telechargement");
            if (drop && TryDelete(d)) removed++;
        }
        return removed;
    }

    private static bool TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); return true; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }
}
