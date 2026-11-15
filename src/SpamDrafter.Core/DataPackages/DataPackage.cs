using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpamDrafter.Core.DataPackages;

/// <summary>
/// Paquet de données du jeu (phase 9, lot B, D-016) : la base de connaissances d'une version de SPAM
/// (textes, illustrations, méta) dans un .zip séparé du programme, donc retirable.
/// Le .zip contient les fichiers de knowledge/&lt;version&gt;/ et « paquet.json » (liste des fichiers et empreintes).
/// </summary>
public static class DataPackage
{
    /// <summary>Version du format du paquet lui-même (paquet.json, donnees.json).</summary>
    public const int PackageFormat = 1;
    public const string ManifestFile = "paquet.json";
    /// <summary>Index des paquets publiés, placé dans la source des mises à jour.</summary>
    public const string IndexFile = "donnees.json";

    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string FileName(string gameVersion, int revision) => $"SpamDrafter-donnees-{gameVersion}-r{revision}.zip";
    public static string FolderName(string gameVersion, int revision) => $"{gameVersion}-r{revision}";

    public static string Sha256(string path)
    {
        using var f = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(f));
    }

    public static T ReadJson<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json)
        ?? throw new InvalidDataException($"Fichier vide ou invalide : {Path.GetFileName(path)}");

    public static void WriteJson<T>(string path, T value) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, Json));

    /// <summary>Compare deux numéros de version du jeu (« 1.30 » &lt; « 1.31 »).</summary>
    public static Version ParseGameVersion(string v) => Version.TryParse(v, out var r) ? r : new Version(0, 0);
}

/// <summary>Un fichier du paquet : chemin relatif (séparateur « / »), taille, empreinte SHA-256.</summary>
public sealed record PackageFile(string Path, long Size, string Sha256);

/// <summary>Contenu de paquet.json.</summary>
public sealed record PackageManifest(
    int PackageFormat, string GameVersion, int Revision, string KnowledgeFormat,
    DateTime CreatedAtUtc, IReadOnlyList<PackageFile> Files)
{
    public string Label => $"SPAM {GameVersion}, révision {Revision}";
}

/// <summary>Une entrée de donnees.json : un paquet publié.</summary>
public sealed record PackageEntry(
    string GameVersion, int Revision, string KnowledgeFormat, string File, long Size, string Sha256);

/// <summary>Contenu de donnees.json.</summary>
public sealed record PackageIndex(int PackageFormat, IReadOnlyList<PackageEntry> Packages)
{
    /// <summary>Le paquet le plus récent que ce programme sait lire (format de base compatible), ou null.</summary>
    public PackageEntry? Latest(string supportedKnowledgeFormat) => Packages
        .Where(p => p.KnowledgeFormat == supportedKnowledgeFormat)
        .OrderByDescending(p => DataPackage.ParseGameVersion(p.GameVersion))
        .ThenByDescending(p => p.Revision)
        .FirstOrDefault();
}
