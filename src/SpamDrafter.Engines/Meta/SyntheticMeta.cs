using System.Text.Json;
using System.Text.Json.Serialization;
using SpamDrafter.Engines.Cooking;
using SpamDrafter.Engines.Evaluation;
using SpamDrafter.Simulator.Data;
using SpamDrafter.Simulator.Engine;

namespace SpamDrafter.Engines.Meta;

/// <summary>Réglages du calcul de la méta synthétique (réglages de l'outil, pas du jeu).</summary>
public sealed record MetaSettings
{
    /// <summary>Nombre de decks de la méta.</summary>
    public int Size { get; init; } = 20;
    /// <summary>Nombre maximal de tours d'amélioration après le départ.</summary>
    public int MaxRounds { get; init; } = 3;
    /// <summary>La méta est stable quand au moins cette part de ses decks est inchangée d'un tour à l'autre.</summary>
    public double StableShare { get; init; } = 0.75;
    /// <summary>Réglage du Cuisinier pour chaque carte (une seule proposition est gardée par carte).</summary>
    public CookSettings Cook { get; init; } = CookSettings.Fast with { Proposals = 1 };
    /// <summary>Cartes de départ : null = toutes (identifiants). Pour des essais plus courts.</summary>
    public IReadOnlyList<string>? Cards { get; init; }
    /// <summary>Classement final des candidats : combien de candidats (× taille de la méta) passent au joueur fin.</summary>
    public double FinalistsFactor { get; init; } = 3;
    /// <summary>Joueur du premier classement (tous les candidats).</summary>
    public string RankPlayer { get; init; } = "rapide";
    public int RankPairs { get; init; } = 2;
    /// <summary>Joueur du classement final (les finalistes entre eux et contre la méta en place).</summary>
    public string FinalPlayer { get; init; } = "heuristique";
    public int FinalPairs { get; init; } = 1;
    /// <summary>Deux decks de la méta diffèrent d'au moins ce nombre de cartes (méta variée).</summary>
    public int MinDifference { get; init; } = 2;
    public ulong Seed { get; init; } = 1;

    public static MetaSettings Default { get; } = new();
}

/// <summary>Un deck de la méta et son score au classement final (contre les autres finalistes et la méta précédente).</summary>
public sealed record MetaDeck(IReadOnlyList<DuelSlot> Deck, double Score, string FromCard);

/// <summary>Bilan d'un tour : méta obtenue, decks gardés du tour précédent, duels, durée.</summary>
public sealed record MetaRound(int Round, IReadOnlyList<MetaDeck> Meta, int Kept, int Candidates, int Duels, TimeSpan Elapsed, int Errors);

public sealed record MetaProgress(int Round, int MaxRound, string Step, double Fraction);

public sealed record MetaResult(IReadOnlyList<MetaDeck> Meta, IReadOnlyList<MetaRound> Rounds, bool Stable, TimeSpan Elapsed)
{
    public int Errors => Rounds.Sum(r => r.Errors);
}

/// <summary>
/// Méta synthétique (phase 8, lot I, D-052) : le Cuisinier calcule lui-même les decks à battre, sans voir de deck
/// réellement joué.
/// <list type="number">
/// <item>Départ : pour chaque carte, le Cuisinier génère son meilleur deck contre des decks au hasard ;</item>
/// <item>Classement : tous ces decks s'affrontent (joueur rapide), puis les meilleurs, choisis variés, s'affrontent avec
/// le joueur heuristique ; les premiers forment la méta ;</item>
/// <item>Tours suivants : pour chaque carte, le Cuisinier génère contre la méta ; les nouveaux decks et la méta en place
/// sont reclassés ensemble ; les gagnants forment la nouvelle méta. On s'arrête quand la méta change peu (stable)
/// ou après <see cref="MetaSettings.MaxRounds"/> tours.</item>
/// </list>
/// Toutes les cartes et tous les talents sont permis (pas de limite de collection). Même base + mêmes réglages = même méta.
/// </summary>
public static class SyntheticMeta
{
    public static MetaResult Build(SimKnowledge kb, MetaSettings? settings = null, IReadOnlyList<MetaDeck>? start = null,
        CancellationToken cancel = default, IProgress<MetaProgress>? progress = null, Action<MetaRound>? roundDone = null)
    {
        var s = settings ?? MetaSettings.Default;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var cards = (s.Cards ?? kb.Cards.Select(c => c.Id).ToList()).Order(StringComparer.Ordinal).ToList();
        if (cards.Count < 2) throw new ArgumentException("Il faut au moins 2 cartes de départ.");
        var rounds = new List<MetaRound>();
        IReadOnlyList<MetaDeck>? meta = start is { Count: >= 2 } ? start : null;
        var first = meta is null ? 0 : 1;
        var stable = false;
        for (var round = first; round <= s.MaxRounds; round++)
        {
            var r = Round(kb, s, cards, meta, round, cancel, progress);
            rounds.Add(r);
            roundDone?.Invoke(r);
            var previous = meta;
            meta = r.Meta;
            if (previous is not null && r.Kept >= Math.Ceiling(s.StableShare * r.Meta.Count)) { stable = true; break; }
        }
        return new MetaResult(meta!, rounds, stable, watch.Elapsed);
    }

    /// <summary>Un tour : une génération par carte (contre la méta en place, ou au hasard au départ), puis classement.</summary>
    internal static MetaRound Round(SimKnowledge kb, MetaSettings s, IReadOnlyList<string> cards, IReadOnlyList<MetaDeck>? meta,
        int round, CancellationToken cancel, IProgress<MetaProgress>? progress)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var duels = 0;
        var errors = 0;
        var metaDecks = meta?.Select(m => m.Deck).ToList();
        var candidates = new List<MetaDeck>();
        for (var k = 0; k < cards.Count; k++)
        {
            cancel.ThrowIfCancellationRequested();
            progress?.Report(new MetaProgress(round, s.MaxRounds, $"Génération pour chaque carte ({k + 1}/{cards.Count})", 0.85 * k / cards.Count));
            var cookSeed = Mix(s.Seed, (ulong)round, (ulong)k);
            var res = GrandCuisinier.Cook(kb, [new FixedCard(cards[k])], s.Cook with { Seed = cookSeed, Meta = metaDecks }, cancel);
            duels += res.Duels;
            errors += res.Proposals.Sum(p => p.Confirmation.Errors);
            foreach (var p in res.Proposals) candidates.Add(new MetaDeck(p.Deck, p.Confirmation.Score, cards[k]));
        }
        // La méta en place reste candidate : elle n'est remplacée que par mieux qu'elle.
        if (meta is not null) candidates.AddRange(meta);
        var unique = candidates.GroupBy(c => GrandCuisinier.Key(c.Deck)).Select(g => g.First()).ToList();

        // 1. Classement rapide de tous les candidats : contre la méta en place, ou entre eux au départ.
        progress?.Report(new MetaProgress(round, s.MaxRounds, "Classement de tous les candidats (joueur rapide)", 0.86));
        var rankField = ReferenceField.Of(metaDecks ?? unique.Select(c => c.Deck));
        var rankSettings = new EvaluationSettings { Player = s.RankPlayer, PairsPerOpponent = s.RankPairs, Seed = Mix(s.Seed, (ulong)round, 0xA11UL), CollectStats = false };
        var rankEvals = DeckEvaluator.EvaluateMany(kb, unique.Select(c => c.Deck).ToList(), rankField, rankSettings, cancel);
        duels += unique.Count * rankField.Decks.Count * s.RankPairs * 2;
        errors += rankEvals.Sum(e => e.Errors);
        var ranked = unique.Select((c, i) => (c, rankEvals[i].Score))
            .OrderByDescending(x => x.Score).ThenBy(x => GrandCuisinier.Key(x.c.Deck), StringComparer.Ordinal).Select(x => x.c).ToList();

        // 2. Classement fin des finalistes (choisis variés) : entre eux ET contre la méta en place.
        progress?.Report(new MetaProgress(round, s.MaxRounds, "Classement des finalistes (joueur heuristique)", 0.92));
        var finalists = PickDiverse(ranked, (int)Math.Ceiling(s.FinalistsFactor * s.Size), s.MinDifference);
        var finalField = ReferenceField.Of(finalists.Select(c => c.Deck)
            .Concat((metaDecks ?? []).Where(m => finalists.All(f => GrandCuisinier.Key(f.Deck) != GrandCuisinier.Key(m)))));
        var finalSettings = new EvaluationSettings { Player = s.FinalPlayer, PairsPerOpponent = s.FinalPairs, Seed = Mix(s.Seed, (ulong)round, 0xF1AUL), CollectStats = false };
        var finalEvals = DeckEvaluator.EvaluateMany(kb, finalists.Select(c => c.Deck).ToList(), finalField, finalSettings, cancel);
        duels += finalists.Count * finalField.Decks.Count * s.FinalPairs * 2;
        errors += finalEvals.Sum(e => e.Errors);
        var byFinal = finalists.Select((c, i) => c with { Score = finalEvals[i].Score })
            .OrderByDescending(c => c.Score).ThenBy(c => GrandCuisinier.Key(c.Deck), StringComparer.Ordinal).ToList();
        var newMeta = PickDiverse(byFinal, s.Size, s.MinDifference);

        var oldKeys = (metaDecks ?? []).Select(GrandCuisinier.Key).ToHashSet();
        var kept = newMeta.Count(m => oldKeys.Contains(GrandCuisinier.Key(m.Deck)));
        progress?.Report(new MetaProgress(round, s.MaxRounds, "Tour terminé", 1));
        return new MetaRound(round, newMeta, kept, unique.Count, duels, watch.Elapsed, errors);
    }

    /// <summary>Choisit dans l'ordre les decks qui diffèrent d'au moins <paramref name="minDifference"/> cartes des précédents.</summary>
    internal static List<MetaDeck> PickDiverse(IReadOnlyList<MetaDeck> ordered, int count, int minDifference)
    {
        var chosen = new List<MetaDeck>();
        foreach (var d in ordered)
        {
            if (chosen.Count == count) break;
            var ids = d.Deck.Select(x => x.CardId).ToHashSet();
            if (chosen.All(c => ids.Except(c.Deck.Select(x => x.CardId)).Count() >= minDifference)) chosen.Add(d);
        }
        return chosen;
    }

    private static ulong Mix(ulong seed, ulong a, ulong b) =>
        new Rng(seed ^ (0x9E3779B97F4A7C15UL * (a + 1)) ^ (0xD1B54A32D192ED03UL * (b + 1))).NextULong();

    // ---------- Fichier de la méta ----------

    /// <summary>Emplacement conventionnel : <c>knowledge/&lt;version&gt;/meta/synthetique.json</c>.</summary>
    public static string DefaultPath(string knowledgeDir) => Path.Combine(knowledgeDir, "meta", "synthetique.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public sealed record FileSlot(string Card, string? Talent);
    public sealed record FileDeck(string Name, double Score, string FromCard, IReadOnlyList<FileSlot> Cards);
    public sealed record FileContent(string GameVersion, ulong Seed, int Rounds, bool Stable, double Minutes, IReadOnlyList<FileDeck> Decks);

    public static void Save(string path, SimKnowledge kb, IReadOnlyList<MetaDeck> meta, ulong seed, int rounds, bool stable, TimeSpan elapsed)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var content = new FileContent(kb.GameVersion, seed, rounds, stable, Math.Round(elapsed.TotalMinutes, 1),
            meta.Select((m, i) => new FileDeck($"Méta {i + 1:00}", Math.Round(m.Score, 4), m.FromCard,
                m.Deck.Select(x => new FileSlot(x.CardId, x.TalentId)).ToList())).ToList());
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(content, Json));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Lit une méta enregistrée. Règle n° 7 : refuse une méta calculée pour une autre version de SPAM.</summary>
    public static IReadOnlyList<MetaDeck> Load(string path, SimKnowledge kb)
    {
        var content = JsonSerializer.Deserialize<FileContent>(File.ReadAllText(path), Json)
                      ?? throw new InvalidDataException($"Méta illisible : {path}.");
        if (content.GameVersion != kb.GameVersion)
            throw new InvalidDataException($"La méta {path} est pour SPAM {content.GameVersion}, la base est en {kb.GameVersion}.");
        var ids = kb.Cards.Select(c => c.Id).ToHashSet();
        return content.Decks.Select(d =>
        {
            if (d.Cards.Any(x => !ids.Contains(x.Card))) throw new InvalidDataException($"{d.Name} : carte inconnue de la base {kb.GameVersion}.");
            return new MetaDeck(d.Cards.Select(x => new DuelSlot(x.Card, x.Talent)).ToList(), d.Score, d.FromCard);
        }).ToList();
    }
}
