using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpamDrafter.Simulator.Players;

/// <summary>
/// Poids du Grand Joueur rangés dans les données (phase 11, lot E) : <c>knowledge/&lt;version&gt;/meta/joueur.json</c>.
/// Réglés par le Studio (entraînement), livrés avec le paquet de données ; absents = poids intégrés au programme
/// (<see cref="HeuristicWeights.Default"/>). Ce sont des réglages de l'IA, pas du jeu (D-046).
/// </summary>
public static class PlayerWeightsFile
{
    public const int FormatVersion = 1;

    /// <summary>Contenu du fichier. Un poids absent du fichier garde sa valeur intégrée.</summary>
    public sealed record Content(int FormatVersion, string GameVersion, string? Origin, DateTime? AdoptedAt, HeuristicWeights Weights);

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions Compact = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string DefaultPath(string knowledgeDir) => Path.Combine(knowledgeDir, "meta", "joueur.json");

    public static Content Read(string path)
    {
        var c = JsonSerializer.Deserialize<Content>(File.ReadAllText(path), Json)
                ?? throw new InvalidDataException($"Poids du Grand Joueur illisibles : {path}.");
        if (c.FormatVersion != FormatVersion)
            throw new InvalidDataException($"Poids du Grand Joueur au format {c.FormatVersion}, non pris en charge : {path}.");
        if (c.Weights is null) throw new InvalidDataException($"Poids du Grand Joueur absents : {path}.");
        if (c.Weights.ToxByRarity is not { Length: 5 }) throw new InvalidDataException($"Valeurs des Tox incomplètes : {path}.");
        return c;
    }

    /// <summary>Poids de la base <paramref name="knowledgeDir"/> (null = pas de fichier : poids intégrés).</summary>
    public static HeuristicWeights? TryLoad(string knowledgeDir, string gameVersion)
    {
        var path = DefaultPath(knowledgeDir);
        if (!File.Exists(path)) return null;
        var c = Read(path);
        if (c.GameVersion != gameVersion)
            throw new InvalidDataException($"Les poids du Grand Joueur {path} sont pour SPAM {c.GameVersion}, la base est en {gameVersion}.");
        return c.Weights;
    }

    public static void Save(string path, Content content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(content, Json));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>
    /// Empreinte des poids (null = poids intégrés). Le pré-calcul la garde : s'il a été fait avec d'autres poids,
    /// il est à refaire.
    /// </summary>
    public static string? Fingerprint(HeuristicWeights? weights) => weights is null ? null
        : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(weights, Compact))));

    /// <summary>Copie indépendante (le tableau des Tox n'est pas partagé).</summary>
    public static HeuristicWeights Copy(HeuristicWeights w) => w with { ToxByRarity = [.. w.ToxByRarity] };
}
