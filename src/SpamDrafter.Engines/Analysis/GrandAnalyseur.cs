using System.Collections.Concurrent;
using SpamDrafter.Engines.Evaluation;
using SpamDrafter.Simulator.Data;
using SpamDrafter.Simulator.Engine;

namespace SpamDrafter.Engines.Analysis;

/// <summary>Réglages du Grand Analyseur (réglages de l'outil, pas du jeu). Budget en nombre de duels.</summary>
public sealed record AnalysisSettings
{
    /// <summary>Joueur de la force, des talents et des Tox (le joueur rapide ne passe pas par la boutique).</summary>
    public string Player { get; init; } = "heuristique";
    /// <summary>Adversaires de chaque mesure (au hasard pour la force, de même niveau pour le reste).</summary>
    public int Opponents { get; init; } = 30;
    public int Pairs { get; init; } = 2;
    /// <summary>Joueur des mesures nombreuses (classement du groupe, rôles, synergies, adversaires dangereux).</summary>
    public string RolePlayer { get; init; } = "rapide";
    /// <summary>Cartes de remplacement essayées pour mesurer le rôle d'une carte (ou d'une paire).</summary>
    public int RoleVariants { get; init; } = 8;
    /// <summary>Grand groupe de decks au hasard : classé une fois (puis gardé en mémoire), il fournit les adversaires
    /// de même niveau et le repérage des cartes adverses dangereuses.</summary>
    public int PoolSize { get; init; } = 300;
    /// <summary>Adversaires au hasard qui servent à classer le grand groupe et le deck (joueur rapide, une paire chacun).</summary>
    public int CalibrationOpponents { get; init; } = 16;
    /// <summary>Nombre minimal de duels contre une carte adverse pour en parler.</summary>
    public int DangerMinGames { get; init; } = 10;
    /// <summary>Nombre minimal de duels avec achat d'un Tox pour en parler.</summary>
    public int ToxMinGames { get; init; } = 15;
    /// <summary>Graine des groupes d'adversaires.</summary>
    public ulong FieldSeed { get; init; } = 2026;
    /// <summary>Graine des duels et des cartes de remplacement.</summary>
    public ulong Seed { get; init; } = 1;
    public bool Parallel { get; init; } = true;
    /// <summary>Joueur des essais de talents (null = <see cref="Player"/>). Les talents se déclenchent seuls :
    /// le joueur rapide les joue, sur beaucoup plus de duels pour le même temps.</summary>
    public string? TalentPlayer { get; init; }
    /// <summary>Paires de duels par adversaire pour les essais de talents (null = <see cref="Pairs"/>).</summary>
    public int? TalentPairs { get; init; }
    /// <summary>Mesurer les liens entre paires de cartes (la mesure la plus longue ; inutile pour la fiche).</summary>
    public bool Synergies { get; init; } = true;
    /// <summary>
    /// Decks de référence (« méta », D-050). S'il y en a au moins 2, ils remplacent les decks tirés au hasard :
    /// force contre eux, rang parmi eux, talents / Tox / rôles mesurés contre eux, et score contre chacun d'eux.
    /// </summary>
    public IReadOnlyList<NamedDeck>? Meta { get; init; }
    /// <summary>Vrai si <see cref="Meta"/> est la méta synthétique (D-052, D-054) et non des decks réellement joués.</summary>
    public bool MetaIsSynthetic { get; init; }
    /// <summary>Nom des adversaires dans les rapports.</summary>
    public string MetaPlural => MetaIsSynthetic ? "decks de la méta synthétique" : "decks de référence";
    public string MetaSingular => MetaIsSynthetic ? "deck de la méta synthétique" : "deck de référence";
    /// <summary>Paires de duels contre chaque deck de référence pour le score deck par deck (joueur rapide).</summary>
    public int MetaPairs { get; init; } = 10;
    /// <summary>Paires de duels par adversaire pour classer les decks de référence entre eux (joueur rapide).</summary>
    public int MetaRatingPairs { get; init; } = 2;

    public static AnalysisSettings Default { get; } = new();
    /// <summary>Fiche joueur (lot E, D-050) : sans les liens entre cartes, talents essayés avec le joueur rapide (≤ 20 s visé).</summary>
    public static AnalysisSettings Sheet { get; } = new() { TalentPlayer = "rapide", TalentPairs = 8, Synergies = false };
    public static AnalysisSettings Fast { get; } = new() { Pairs = 1, RoleVariants = 4, DangerMinGames = 8, ToxMinGames = 10 };
    public static AnalysisSettings Precise { get; } = new() { Opponents = 40, Pairs = 3, RoleVariants = 12, PoolSize = 500 };
}

/// <summary>Une valeur mesurée : moyenne, marge d'erreur à 95 %, nombre de duels.</summary>
public sealed record Measured(double Value, double Margin95, int Games)
{
    /// <summary>La valeur est nettement positive (la marge n'atteint pas 0).</summary>
    public bool ClearlyPositive => Value - Margin95 > 0;
    public bool ClearlyNegative => Value + Margin95 < 0;

    /// <summary>Moyenne et marge d'une série de valeurs (une par duel).</summary>
    public static Measured Of(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return new Measured(0, 1, 0);
        var m = values.Average();
        var sd = values.Count < 2 ? 0 : Math.Sqrt(values.Sum(v => (v - m) * (v - m)) / (values.Count - 1));
        return new Measured(m, 1.96 * sd / Math.Sqrt(values.Count), values.Count);
    }

    /// <summary>
    /// Score (valeurs 0, ½ ou 1) : la marge est calculée comme si on avait ajouté une victoire et une défaite
    /// (règle de Laplace), pour qu'un score de 0 % ou 100 % sur peu de duels n'ait pas une marge de 0.
    /// </summary>
    public static Measured OfScores(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return new Measured(0, 1, 0);
        var m = values.Average();
        var p = (values.Sum() + 1) / (values.Count + 2);
        return new Measured(m, 1.96 * Math.Sqrt(p * (1 - p) / values.Count), values.Count);
    }
}

/// <summary>Rôle d'une carte : ce que le deck perd quand on la remplace par une autre carte au hasard.</summary>
public sealed record CardRole(int Index, DuelSlot Slot, Measured Contribution, CardAverages Averages,
    double DamageShare, double HealingShare, double KillShare, double DamageTakenShare);

/// <summary>
/// Lien entre deux cartes : deck − sans l'une − sans l'autre + sans les deux. Positif : elles se complètent
/// (chacune vaut plus quand l'autre est là). Négatif : elles font double emploi (l'une compense l'absence de l'autre).
/// </summary>
public sealed record PairSynergy(int I, int J, Measured Interaction);

/// <summary>Un autre choix de talent pour une carte : écart de score avec le talent actuel (à graines égales).</summary>
public sealed record TalentOption(string? TalentId, Measured Difference);

public sealed record CardTalents(int Index, string CardId, string? Current, IReadOnlyList<TalentOption> Options)
{
    /// <summary>Meilleur choix mesuré (le talent actuel si aucun autre ne fait mieux).</summary>
    public string? Best => Options.Where(o => o.Difference.Value > 0).OrderByDescending(o => o.Difference.Value)
        .Select(o => o.TalentId).DefaultIfEmpty(Current).First();
    /// <summary>Un autre talent fait nettement mieux (la marge n'atteint pas 0).</summary>
    public TalentOption? ClearlyBetter => Options.Where(o => o.Difference.ClearlyPositive).OrderByDescending(o => o.Difference.Value).FirstOrDefault();
}

/// <summary>Tox acheté par le deck : score quand il est acheté, score quand il ne l'est pas (corrélation, pas cause).</summary>
public sealed record ToxInsight(string ToxUuid, int GamesWith, double ScoreWith, int GamesWithout, double ScoreWithout,
    int MostOftenOn, double Purchases);

/// <summary>Score du deck contre les adversaires qui ont une carte donnée.</summary>
public sealed record DangerCard(string CardId, Measured Score);

/// <summary>Un deck nommé (deck de référence).</summary>
public sealed record NamedDeck(string Name, IReadOnlyList<DuelSlot> Deck);

/// <summary>Score du deck contre un deck de référence.</summary>
public sealed record MetaMatchup(string Name, IReadOnlyList<DuelSlot> Deck, Measured Score);

/// <param name="Strength">Score contre des decks au hasard, ou contre les decks de référence si <paramref name="UsesMeta"/> (joueur fin).</param>
/// <param name="Percentile">Part des decks (au hasard, ou de référence) que le deck dépasse au classement (0 à 1).</param>
/// <param name="LevelStrength">Score contre les adversaires de même niveau (joueur fin) : là où les talents et Tox sont mesurés.</param>
public sealed record AnalysisReport(
    IReadOnlyList<DuelSlot> Deck,
    AnalysisSettings Settings,
    Measured Strength,
    double Percentile,
    Measured LevelStrength,
    double AverageRounds, double AverageRoundsWins, double AverageRoundsLosses,
    IReadOnlyList<CardRole> Roles,
    IReadOnlyList<PairSynergy> Synergies,
    IReadOnlyList<CardTalents> Talents,
    IReadOnlyList<ToxInsight> Tox,
    int ToxGames,
    IReadOnlyList<DangerCard> Dangers,
    Measured DangerOverall,
    string TalentPlayer,
    bool SynergiesMeasured,
    bool UsesMeta,
    IReadOnlyList<MetaMatchup> Matchups,
    IReadOnlyDictionary<string, double> PeakLevels,
    int Duels,
    int Errors,
    TimeSpan Elapsed)
{
    /// <summary>
    /// Mesure de référence de la force, celle de la note sur 100 (D-074) : la même pour la proposition du Cuisinier, la fiche
    /// et l'analyse complète, donc la même note partout. Absente (null) : la note utilise <see cref="Strength"/>.
    /// </summary>
    public Measured? NoteStrength { get; init; }

    /// <summary>La mesure sur laquelle repose la note.</summary>
    public Measured ForNote => NoteStrength ?? Strength;
}

public enum AnalysisStage { Calibration, Strength, Talents, Roles, Dangers, Done }
public sealed record AnalysisProgress(AnalysisStage Stage, double Fraction);

/// <summary>
/// Grand Analyseur (phase 8, lot C) : mesure par simulation ce qui fait la force d'un deck. Chaque chiffre du rapport
/// vient d'une mesure (même deck + mêmes réglages = même rapport).
/// <list type="bullet">
/// <item>Force : contre des decks au hasard, plus son rang dans un grand groupe de decks au hasard ;</item>
/// <item>talents, Tox, rôles et synergies : contre des adversaires de MÊME NIVEAU (sinon, un deck très fort gagne
/// presque tout et aucune variante ne se distingue — effet plafond) ;</item>
/// <item>variantes jouées avec les mêmes graines que le deck d'origine : on compare les mêmes duels.</item>
/// </list>
/// </summary>
public static class GrandAnalyseur
{
    /// <summary>Classements du grand groupe, gardés en mémoire (ils ne dépendent pas du deck analysé).</summary>
    private static readonly ConcurrentDictionary<(SimKnowledge, ulong, int, int, string), double[]> PoolRatings = new();
    /// <summary>Classements des decks de référence entre eux, gardés en mémoire (clé : leur contenu).</summary>
    private static readonly ConcurrentDictionary<(SimKnowledge, string, ulong, int, string), double[]> MetaRatings = new();

    /// <summary>
    /// Mesure de référence de la force d'un deck (D-074) : le joueur fin contre la méta (ou des decks au hasard), avec les
    /// réglages et la graine de <paramref name="s"/>. C'est exactement le calcul de la « Force » de <see cref="Analyse"/> :
    /// avec les mêmes réglages, le résultat est identique à <c>AnalysisReport.Strength</c>.
    /// </summary>
    public static Measured MeasureStrength(SimKnowledge kb, IReadOnlyList<DuelSlot> deck, AnalysisSettings s, CancellationToken cancel = default)
    {
        var field = s.Meta is { Count: >= 2 } meta ? ReferenceField.Of(meta.Select(m => m.Deck)) : ReferenceField.Random(kb, s.Opponents, s.FieldSeed);
        var main = new EvaluationSettings { Player = s.Player, PairsPerOpponent = s.Pairs, Seed = s.Seed, Parallel = s.Parallel };
        return Measured.OfScores(Outcomes(DeckEvaluator.EvaluateMany(kb, [deck], field, main, cancel)[0]));
    }

    public static AnalysisReport Analyse(SimKnowledge kb, IReadOnlyList<DuelSlot> deck, AnalysisSettings? settings = null,
        CancellationToken cancel = default, IProgress<AnalysisProgress>? progress = null)
    {
        var s = settings ?? AnalysisSettings.Default;
        if (deck.Count != kb.Settings.CardsPerDeck || deck.Select(d => d.CardId).Distinct().Count() != deck.Count)
            throw new ArgumentException($"Le deck doit contenir {kb.Settings.CardsPerDeck} cartes différentes.");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var n = deck.Count;
        var useMeta = s.Meta is { Count: >= 2 };
        var metaField = useMeta ? ReferenceField.Of(s.Meta!.Select(m => m.Deck)) : null;
        var randomField = metaField ?? ReferenceField.Random(kb, s.Opponents, s.FieldSeed);
        var pool = ReferenceField.Random(kb, s.PoolSize, s.FieldSeed ^ 0xD4A6E7UL);
        var calibration = ReferenceField.Random(kb, s.CalibrationOpponents, s.FieldSeed ^ 0xCA11B7UL);
        var main = new EvaluationSettings { Player = s.Player, PairsPerOpponent = s.Pairs, Seed = s.Seed, Parallel = s.Parallel };
        var fast = main with { Player = s.RolePlayer, CollectStats = false };
        var calib = fast with { PairsPerOpponent = 1, Seed = s.FieldSeed };
        var rng = new Rng(s.Seed ^ 0xA9A1E5UL);
        var duels = 0;
        var errors = 0;

        // Prévision du travail pour l'avancement (un duel du joueur fin compte pour ~20 duels rapides).
        var fieldSize = useMeta ? metaField!.Decks.Count : s.Opponents;
        var perDeck = fieldSize * s.Pairs * 2;
        var talentVariants = deck.Sum(d => kb.Card(d.CardId).Talents.Count);
        var poolKey = (kb, s.FieldSeed, s.PoolSize, s.CalibrationOpponents, s.RolePlayer);
        var talentPlayer = s.TalentPlayer ?? s.Player;
        var talentPairs = s.TalentPairs ?? s.Pairs;
        var talentsApart = talentPlayer != s.Player || talentPairs != s.Pairs;
        var talentWeight = talentPlayer == s.RolePlayer ? 1.0 : 20.0;
        var metaKey = (kb, useMeta ? string.Join(";", s.Meta!.Select(m => string.Join(",", m.Deck.Select(x => x.CardId + "/" + x.TalentId)))) : "",
            s.FieldSeed, s.MetaRatingPairs, s.RolePlayer);
        var planned = (useMeta ? (MetaRatings.ContainsKey(metaKey) ? 0 : fieldSize * fieldSize * s.MetaRatingPairs * 2.0) + fieldSize * s.MetaPairs * 2.0
                        : PoolRatings.ContainsKey(poolKey) ? 0 : s.PoolSize * s.CalibrationOpponents * 2.0)
                      + 20.0 * 2 * perDeck
                      + talentWeight * (talentsApart ? 1 + talentVariants : talentVariants) * fieldSize * talentPairs * 2
                      + (1 + n * s.RoleVariants + (s.Synergies ? n * (n - 1) / 2 * s.RoleVariants : 0)) * perDeck + (useMeta ? 0 : s.PoolSize * 2);
        double done = 0;
        IReadOnlyList<DeckEvaluation> Run(AnalysisStage stage, IReadOnlyList<IReadOnlyList<DuelSlot>> decks, ReferenceField f,
            EvaluationSettings es, double weight)
        {
            var start = done;
            var p = progress is null ? null : new Sync<int>(k =>
                progress.Report(new AnalysisProgress(stage, Math.Min(0.99, (start + weight * k) / planned))));
            var evals = DeckEvaluator.EvaluateMany(kb, decks, f, es, cancel, p);
            var count = decks.Count * f.Decks.Count * es.PairsPerOpponent * 2;
            done += weight * count;
            duels += count;
            errors += evals.Sum(e => e.Errors);
            return evals;
        }

        double percentile;
        ReferenceField levelField;
        if (useMeta)
        {
            // 1. Classement parmi les decks de référence : chacun (une fois, gardé en mémoire) et le deck, contre eux tous.
            var metaCalib = calib with { PairsPerOpponent = s.MetaRatingPairs };
            if (!MetaRatings.TryGetValue(metaKey, out var metaRatings))
            {
                metaRatings = Run(AnalysisStage.Calibration, metaField!.Decks, metaField, metaCalib, 1).Select(e => e.Score).ToArray();
                MetaRatings[metaKey] = metaRatings;
            }
            var metaRating = Run(AnalysisStage.Calibration, [deck], metaField!, metaCalib, 1)[0].Score;
            percentile = (metaRatings.Count(r => r < metaRating) + 0.5 * metaRatings.Count(r => r == metaRating)) / metaRatings.Length;
            // Les decks de référence sont déjà les adversaires à battre : ils servent aussi de « même niveau ».
            levelField = metaField!;
        }
        else
        {
            // 1. Classement : le grand groupe (une fois) et le deck, contre les mêmes adversaires de calibrage.
            if (!PoolRatings.TryGetValue(poolKey, out var ratings))
            {
                ratings = Run(AnalysisStage.Calibration, pool.Decks, calibration, calib, 1).Select(e => e.Score).ToArray();
                PoolRatings[poolKey] = ratings;
            }
            var rating = Run(AnalysisStage.Calibration, [deck], calibration, calib, 1)[0].Score;
            percentile = (ratings.Count(r => r < rating) + 0.5 * ratings.Count(r => r == rating)) / ratings.Length;
            // Adversaires de même niveau : les decks du groupe dont le classement est le plus proche du sien.
            levelField = ReferenceField.Of(Enumerable.Range(0, pool.Decks.Count)
                .OrderBy(i => Math.Abs(ratings[i] - rating)).ThenBy(i => i).Take(s.Opponents).Select(i => pool.Decks[i]));
        }

        // 2. Force (au hasard) ; deck et variantes de talents contre le même niveau (mêmes graines).
        var talentDecks = new List<(int Index, string? Talent, IReadOnlyList<DuelSlot> Deck)>();
        for (var i = 0; i < n; i++)
        {
            var options = new List<string?> { null };
            options.AddRange(kb.Card(deck[i].CardId).Talents);
            foreach (var t in options.Where(t => t != deck[i].TalentId))
            {
                var d = deck.ToList();
                d[i] = d[i] with { TalentId = t };
                talentDecks.Add((i, t, d));
            }
        }
        var strengthEval = Run(AnalysisStage.Strength, [deck], randomField, main, 20)[0];
        // Avec la méta, force et « même niveau » ont les mêmes adversaires : d'autres graines, pour d'autres duels.
        var levelMain = useMeta ? main with { Seed = s.Seed ^ 0x1E7E1UL } : main;
        IReadOnlyList<DeckEvaluation> levelEvals;   // le deck (joueur fin) puis, si mêmes réglages, ses variantes de talents
        IReadOnlyList<DeckEvaluation> talentEvals;  // le deck puis ses variantes, avec les réglages des talents
        if (!talentsApart)
        {
            levelEvals = Run(AnalysisStage.Talents, [deck, .. talentDecks.Select(t => t.Deck)], levelField, levelMain, 20);
            talentEvals = levelEvals;
        }
        else
        {
            levelEvals = Run(AnalysisStage.Strength, [deck], levelField, levelMain, 20);
            var ts = levelMain with { Player = talentPlayer, PairsPerOpponent = talentPairs, CollectStats = false };
            talentEvals = Run(AnalysisStage.Talents, [deck, .. talentDecks.Select(t => t.Deck)], levelField, ts, talentWeight);
        }
        var levelBase = Outcomes(levelEvals[0]);
        var talentBase = Outcomes(talentEvals[0]);
        var talents = Enumerable.Range(0, n).Select(i => new CardTalents(i, deck[i].CardId, deck[i].TalentId,
            talentDecks.Select((t, k) => (t, k)).Where(x => x.t.Index == i)
                .Select(x => new TalentOption(x.t.Talent, Paired(Outcomes(talentEvals[x.k + 1]), talentBase))).ToList())).ToList();

        // 3. Rôle de chaque carte (joueur rapide, même niveau) : la remplacer par d'autres cartes au hasard, à la même place.
        var allIds = kb.Cards.Select(c => c.Id).Order(StringComparer.Ordinal).ToArray();
        IReadOnlyList<DuelSlot> Replace(IReadOnlyList<DuelSlot> d, params int[] idx)
        {
            var r = d.ToList();
            var used = r.Select(x => x.CardId).ToHashSet();
            foreach (var i in idx)
            {
                string id;
                do id = allIds[rng.Next(0, allIds.Length)]; while (!used.Add(id));
                var t = kb.Card(id).Talents;
                var k = rng.Next(0, t.Count + 1);
                r[i] = new DuelSlot(id, k == 0 ? null : t[k - 1]);
            }
            return r;
        }
        var singles = Enumerable.Range(0, n).SelectMany(i => Enumerable.Range(0, s.RoleVariants).Select(_ => (I: i, Deck: Replace(deck, i)))).ToList();
        var pairs = (from i in Enumerable.Range(0, n) from j in Enumerable.Range(i + 1, n - i - 1) select (I: i, J: j)).ToList();
        var doubles = !s.Synergies ? []
            : pairs.SelectMany(p => Enumerable.Range(0, s.RoleVariants).Select(_ => (p.I, p.J, Deck: Replace(deck, p.I, p.J)))).ToList();
        var fastEvals = Run(AnalysisStage.Roles, [deck, .. singles.Select(x => x.Deck), .. doubles.Select(x => x.Deck)], levelField, fast, 1);
        var fastBase = Outcomes(fastEvals[0]);
        double[] MeanOf(IEnumerable<int> evalIndexes)
        {
            var list = evalIndexes.Select(k => Outcomes(fastEvals[k])).ToList();
            return Enumerable.Range(0, fastBase.Length).Select(g => list.Average(o => o[g])).ToArray();
        }
        var without = Enumerable.Range(0, n).Select(i => MeanOf(singles.Select((x, k) => (x, k)).Where(y => y.x.I == i).Select(y => y.k + 1))).ToArray();
        var withoutPair = !s.Synergies ? [] : pairs.ToDictionary(p => p, p =>
            MeanOf(doubles.Select((x, k) => (x, k)).Where(y => y.x.I == p.I && y.x.J == p.J).Select(y => y.k + 1 + singles.Count)));

        // Moyennes par carte : duels du deck contre des decks au hasard et contre le même niveau.
        var statGames = new DeckEvaluation(deck, [.. strengthEval.Games, .. levelEvals[0].Games], strengthEval.Elapsed);
        var avg = statGames.CardAverages();
        double Share(Func<CardAverages, double> f, int i) { var t = avg.Sum(f); return t <= 0 ? 0 : f(avg[i]) / t; }
        var roles = Enumerable.Range(0, n).Select(i => new CardRole(i, deck[i], Paired(fastBase, without[i]), avg[i],
            Share(a => a.DamageDone, i), Share(a => a.HealingDone, i), Share(a => a.Kills, i), Share(a => a.DamageTaken, i))).ToList();

        // 4. Liens entre cartes : base − sans i − sans j + sans i et j (à graines égales, duel par duel).
        var synergies = !s.Synergies ? [] : pairs.Select(p => new PairSynergy(p.I, p.J, Measured.Of(Enumerable.Range(0, fastBase.Length)
            .Select(g => fastBase[g] - without[p.I][g] - without[p.J][g] + withoutPair[p][g]).ToList()))).ToList();

        // 5. Tox achetés par le deck (joueur fin : le deck et ses variantes de talents, mêmes cartes).
        // (les variantes jouées par le joueur rapide n'achètent rien : seules les parties du joueur fin comptent)
        var toxGames = new[] { strengthEval }.Concat(levelEvals).SelectMany(e => e.Games).Where(g => g.Error is null && g.Stats is not null).ToList();
        var tox = toxGames.SelectMany(g => g.Stats!.Purchases.Where(p => p.Player == g.Me)).Select(p => p.ToxUuid).Distinct()
            .Select(uuid =>
            {
                var with = toxGames.Where(g => g.Stats!.Purchases.Any(p => p.Player == g.Me && p.ToxUuid == uuid)).ToList();
                var withoutTox = toxGames.Where(g => !g.Stats!.Purchases.Any(p => p.Player == g.Me && p.ToxUuid == uuid)).ToList();
                var on = with.SelectMany(g => g.Stats!.Purchases.Where(p => p.Player == g.Me && p.ToxUuid == uuid))
                    .GroupBy(p => p.DeckIndex).OrderByDescending(x => x.Count()).ThenBy(x => x.Key).First().Key;
                var purchases = with.Average(g => g.Stats!.Purchases.Count(p => p.Player == g.Me && p.ToxUuid == uuid));
                return new ToxInsight(uuid, with.Count, ScoreOf(with), withoutTox.Count, ScoreOf(withoutTox), on, purchases);
            })
            .Where(t => t.GamesWith >= s.ToxMinGames && t.GamesWithout > 0)
            .OrderByDescending(t => t.ScoreWith - t.ScoreWithout).ThenBy(t => t.ToxUuid, StringComparer.Ordinal).ToList();

        // 6. Adversaires difficiles (joueur rapide) : avec la méta, le score contre chaque deck de référence ;
        //    sans elle, les cartes adverses contre lesquelles le deck marque le moins, dans tout le grand groupe.
        List<GameRecord> dangerGames;
        List<DangerCard> dangers = [];
        List<MetaMatchup> matchups = [];
        if (useMeta)
        {
            var metaEval = Run(AnalysisStage.Dangers, [deck], metaField!, fast with { PairsPerOpponent = s.MetaPairs }, 1)[0];
            dangerGames = metaEval.Games.Where(g => g.Error is null).ToList();
            matchups = dangerGames.GroupBy(g => g.Opponent)
                .Select(x => new MetaMatchup(s.Meta![x.Key].Name, s.Meta[x.Key].Deck, Measured.OfScores(x.Select(Value).ToList())))
                .OrderBy(m => m.Score.Value).ThenBy(m => m.Name, StringComparer.Ordinal).ToList();
        }
        else
        {
            var dangerEval = Run(AnalysisStage.Dangers, [deck], pool, fast with { PairsPerOpponent = 1 }, 1)[0];
            dangerGames = dangerEval.Games.Where(g => g.Error is null).ToList();
            dangers = dangerGames.SelectMany(g => pool.Decks[g.Opponent].Select(x => (x.CardId, Outcome: Value(g))))
                .GroupBy(x => x.CardId)
                .Where(x => x.Count() >= s.DangerMinGames)
                .Select(x => new DangerCard(x.Key, Measured.OfScores(x.Select(y => y.Outcome).ToList())))
                .OrderBy(d => d.Score.Value).ThenBy(d => d.CardId, StringComparer.Ordinal).ToList();
        }

        var counted = statGames.Games.Where(g => g.Error is null).ToList();
        double Rounds(Func<GameRecord, bool> f) => counted.Where(f).Select(g => (double)g.Rounds).DefaultIfEmpty(0).Average();
        progress?.Report(new AnalysisProgress(AnalysisStage.Done, 1));
        return new AnalysisReport(deck, s, Measured.OfScores(Outcomes(strengthEval)), percentile, Measured.OfScores(levelBase),
            Rounds(_ => true), Rounds(g => g.Outcome > 0), Rounds(g => g.Outcome < 0),
            roles, synergies, talents, tox, toxGames.Count, dangers, Measured.OfScores(dangerGames.Select(Value).ToList()),
            talentPlayer, s.Synergies, useMeta, matchups, statGames.AveragePeakLevels(), duels, errors, watch.Elapsed);
    }

    /// <summary>
    /// Prépare le classement du grand groupe (la partie la plus longue d'une première analyse), par exemple à
    /// l'ouverture de l'application : les analyses suivantes le trouvent en mémoire.
    /// </summary>
    public static void WarmUp(SimKnowledge kb, AnalysisSettings? settings = null, CancellationToken cancel = default)
    {
        var s = settings ?? AnalysisSettings.Sheet;
        if (s.Meta is { Count: >= 2 } meta)
        {
            // Avec des decks de référence : leur classement entre eux (même clé que dans Analyse).
            var metaKey = (kb, string.Join(";", meta.Select(m => string.Join(",", m.Deck.Select(x => x.CardId + "/" + x.TalentId)))),
                s.FieldSeed, s.MetaRatingPairs, s.RolePlayer);
            if (MetaRatings.ContainsKey(metaKey)) return;
            var field = ReferenceField.Of(meta.Select(m => m.Deck));
            var es = new EvaluationSettings { Player = s.RolePlayer, PairsPerOpponent = s.MetaRatingPairs, Seed = s.FieldSeed, CollectStats = false, Parallel = s.Parallel };
            MetaRatings[metaKey] = DeckEvaluator.EvaluateMany(kb, field.Decks, field, es, cancel).Select(e => e.Score).ToArray();
            return;
        }
        var poolKey = (kb, s.FieldSeed, s.PoolSize, s.CalibrationOpponents, s.RolePlayer);
        if (PoolRatings.ContainsKey(poolKey)) return;
        var pool = ReferenceField.Random(kb, s.PoolSize, s.FieldSeed ^ 0xD4A6E7UL);
        var calibration = ReferenceField.Random(kb, s.CalibrationOpponents, s.FieldSeed ^ 0xCA11B7UL);
        var calib = new EvaluationSettings { Player = s.RolePlayer, PairsPerOpponent = 1, Seed = s.FieldSeed, CollectStats = false, Parallel = s.Parallel };
        PoolRatings[poolKey] = DeckEvaluator.EvaluateMany(kb, pool.Decks, calibration, calib, cancel).Select(e => e.Score).ToArray();
    }

    private static double Value(GameRecord g) => g.Outcome > 0 ? 1 : g.Outcome == 0 ? 0.5 : 0;
    private static double ScoreOf(IReadOnlyList<GameRecord> games) => games.Count == 0 ? 0 : games.Average(Value);

    /// <summary>Résultat de chaque duel (1, ½, 0), dans l'ordre des duels (les erreurs comptent ½ et sont signalées à part).</summary>
    internal static double[] Outcomes(DeckEvaluation e) => e.Games.Select(g => g.Error is null ? Value(g) : 0.5).ToArray();

    /// <summary>Écart a − b duel par duel (mêmes graines), avec sa marge.</summary>
    internal static Measured Paired(IReadOnlyList<double> a, IReadOnlyList<double> b) =>
        Measured.Of(Enumerable.Range(0, a.Count).Select(g => a[g] - b[g]).ToList());

    private sealed class Sync<T>(Action<T> f) : IProgress<T> { public void Report(T value) => f(value); }
}
