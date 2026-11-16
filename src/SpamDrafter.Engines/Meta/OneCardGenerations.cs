using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpamDrafter.Engines.Cooking;
using SpamDrafter.Simulator.Data;
using SpamDrafter.Simulator.Engine;
using SpamDrafter.Simulator.Players;

namespace SpamDrafter.Engines.Meta;

/// <summary>Avancement du pré-calcul : cartes faites, total, carte en cours.</summary>
public sealed record PrecomputeProgress(int Done, int Total, string CardId);

/// <summary>
/// Pré-calcul des générations à une carte (phase 9, D-050) : pour chaque carte, les propositions du Grand Cuisinier
/// (réglage « Normal », talent au choix du Cuisinier) contre la méta synthétique (D-052, D-054). Calculé par le Studio
/// à chaque version et livré dans le paquet de données : l'application affiche alors le résultat tout de suite.
/// Le fichier est enregistré après chaque carte : un calcul interrompu reprend là où il s'était arrêté.
/// </summary>
public static class OneCardGenerations
{
    public const int FormatVersion = 1;
    public static string DefaultPath(string knowledgeDir) => Path.Combine(knowledgeDir, "meta", "generations-1-carte.json");

    public sealed record FileSlot(string Card, string? Talent);
    public sealed record FileProposal(IReadOnlyList<FileSlot> Cards, double Score, double Margin95, int Games, int Opponents);
    public sealed record FileCard(string Card, double Seconds, IReadOnlyList<FileProposal> Proposals);
    /// <param name="MetaSha256">Empreinte du fichier de méta synthétique utilisé : si la méta change, tout est à refaire.</param>
    /// <param name="PlayerSha256">Empreinte des poids du Grand Joueur des données (null = poids intégrés) : s'ils changent,
    /// tout est à refaire (phase 11, lot E).</param>
    public sealed record FileContent(int FormatVersion, string GameVersion, string Effort, ulong Seed, string MetaSha256,
        double Minutes, IReadOnlyList<FileCard> Cards, string? PlayerSha256 = null);

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string FileSha256(string path)
    {
        using var f = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(f));
    }

    /// <summary>
    /// Calcule les cartes manquantes de <paramref name="path"/> (toutes si le fichier n'existe pas, ou s'il a été fait
    /// avec une autre méta, une autre version ou d'autres réglages) et l'enregistre après chaque carte.
    /// </summary>
    /// <param name="cards">Cartes à traiter (identifiants) ; null = toutes les cartes de la base.</param>
    /// <param name="cookOverride">Réglages du Cuisinier à la place de ceux de <paramref name="effort"/> (tests).</param>
    /// <returns>Le contenu final et le nombre de cartes calculées pendant cet appel.</returns>
    public static (FileContent Content, int Computed) Build(SimKnowledge kb, string metaPath, string path,
        Effort effort = Effort.Normal, ulong seed = 1, IReadOnlyList<string>? cards = null,
        IProgress<PrecomputeProgress>? progress = null, CancellationToken cancel = default, CookSettings? cookOverride = null)
    {
        var meta = SyntheticMeta.Load(metaPath, kb).Select(m => m.Deck).ToList();
        var metaSha = FileSha256(metaPath);
        var playerSha = PlayerWeightsFile.Fingerprint(kb.PlayerWeights);
        var effortName = effort.ToString().ToLowerInvariant();
        var targets = (cards ?? kb.Cards.Select(c => c.Id).ToList()).Order(StringComparer.Ordinal).ToList();

        var previous = File.Exists(path) ? Read(path) : null;
        var done = previous is not null && previous.GameVersion == kb.GameVersion && previous.MetaSha256 == metaSha &&
                   previous.PlayerSha256 == playerSha &&
                   previous.Effort == effortName && previous.Seed == seed && previous.FormatVersion == FormatVersion
            ? previous.Cards.ToDictionary(c => c.Card)
            : new Dictionary<string, FileCard>();
        var minutes = done.Count > 0 ? previous!.Minutes : 0;

        var settings = (cookOverride ?? Coordinator.CookSettingsFor(effort)) with { Seed = seed, Meta = meta };
        var computed = 0;
        var total = Stopwatch.StartNew();
        foreach (var id in targets)
        {
            cancel.ThrowIfCancellationRequested();
            if (done.ContainsKey(id)) continue;
            progress?.Report(new PrecomputeProgress(done.Count(d => targets.Contains(d.Key)), targets.Count, id));
            var sw = Stopwatch.StartNew();
            var res = GrandCuisinier.Cook(kb, [new FixedCard(id)], settings, cancel);
            done[id] = new FileCard(id, Math.Round(sw.Elapsed.TotalSeconds, 1), res.Proposals.Select(p => new FileProposal(
                p.Deck.Select(x => new FileSlot(x.CardId, x.TalentId)).ToList(),
                Math.Round(p.Confirmation.Score, 4), Math.Round(p.Confirmation.Margin95, 4), p.Confirmation.Counted,
                res.RandomOpponents)).ToList());
            computed++;
            Save(path, new FileContent(FormatVersion, kb.GameVersion, effortName, seed, metaSha,
                Math.Round(minutes + total.Elapsed.TotalMinutes, 1), Ordered(done), playerSha));
        }
        var content = new FileContent(FormatVersion, kb.GameVersion, effortName, seed, metaSha,
            Math.Round(minutes + total.Elapsed.TotalMinutes, 1), Ordered(done), playerSha);
        if (computed > 0 || !File.Exists(path)) Save(path, content);
        return (content, computed);
    }

    private static List<FileCard> Ordered(Dictionary<string, FileCard> d) =>
        d.Values.OrderBy(c => c.Card, StringComparer.Ordinal).ToList();

    public static FileContent Read(string path) =>
        JsonSerializer.Deserialize<FileContent>(File.ReadAllText(path), Json)
        ?? throw new InvalidDataException($"Pré-calcul illisible : {path}.");

    public static void Save(string path, FileContent content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(content, Json));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>
    /// Lit le pré-calcul pour l'application : propositions par carte. Règle n° 7 : refuse un fichier d'une autre
    /// version de SPAM, ou qui cite une carte ou un talent inconnus de la base.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<GeneratedDeck>> Load(string path, SimKnowledge kb)
    {
        var content = Read(path);
        if (content.FormatVersion != FormatVersion)
            throw new InvalidDataException($"Pré-calcul au format {content.FormatVersion}, non pris en charge.");
        if (content.GameVersion != kb.GameVersion)
            throw new InvalidDataException($"Le pré-calcul {path} est pour SPAM {content.GameVersion}, la base est en {kb.GameVersion}.");
        var ids = kb.Cards.Select(c => c.Id).ToHashSet();
        var result = new Dictionary<string, IReadOnlyList<GeneratedDeck>>();
        foreach (var c in content.Cards)
        {
            foreach (var p in c.Proposals)
                foreach (var s in p.Cards)
                    if (!ids.Contains(s.Card) || (s.Talent is not null && !kb.Card(s.Card).Talents.Contains(s.Talent)))
                        throw new InvalidDataException($"Pré-calcul : carte ou talent inconnu ({s.Card} / {s.Talent}).");
            result[c.Card] = c.Proposals.Select(p => new GeneratedDeck(
                p.Cards.Select(s => new DuelSlot(s.Card, s.Talent)).ToList(), p.Score, p.Margin95, p.Games, p.Opponents,
                UsesMeta: true, Precomputed: true)).ToList();
        }
        return result;
    }
}
