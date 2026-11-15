using SpamDrafter.Simulator.Data;
using SpamDrafter.Simulator.Engine;
using SpamDrafter.Simulator.Players;

namespace SpamDrafter.Engines.Evaluation;

/// <summary>
/// Groupe de decks de référence (phase 8, lot A) : les adversaires contre lesquels on mesure la force d'un deck.
/// Decks tirés au hasard (comme le banc d'essai), toujours les mêmes pour une même graine.
/// </summary>
public sealed class ReferenceField
{
    public ulong Seed { get; }
    public IReadOnlyList<IReadOnlyList<DuelSlot>> Decks { get; }

    private ReferenceField(ulong seed, IReadOnlyList<IReadOnlyList<DuelSlot>> decks) { Seed = seed; Decks = decks; }

    public static ReferenceField Random(SimKnowledge kb, int count, ulong seed)
    {
        var rng = new Rng(seed);
        return new ReferenceField(seed, Enumerable.Range(0, count).Select(_ => (IReadOnlyList<DuelSlot>)Bench.RandomDeck(kb, rng)).ToList());
    }

    public static ReferenceField Of(IEnumerable<IReadOnlyList<DuelSlot>> decks, ulong seed = 0) => new(seed, decks.ToList());
}

/// <summary>Réglages de l'évaluation (réglages de l'outil, pas du jeu).</summary>
public sealed record EvaluationSettings
{
    /// <summary>Joueur qui joue les deux camps (voir <see cref="PlayerCatalog.Names"/>).</summary>
    public string Player { get; init; } = "heuristique";
    /// <summary>Paires de duels par adversaire (chaque paire : même graine, places échangées).</summary>
    public int PairsPerOpponent { get; init; } = 2;
    /// <summary>
    /// Graine des duels. Les graines ne dépendent que de (graine, adversaire, paire) : deux decks évalués avec
    /// la même graine affrontent les mêmes adversaires avec le même hasard, ce qui rend la comparaison plus juste.
    /// </summary>
    public ulong Seed { get; init; } = 2026;
    /// <summary>Relever les statistiques de chaque duel (pour le Grand Analyseur).</summary>
    public bool CollectStats { get; init; } = true;
    public bool Parallel { get; init; } = true;
}

/// <summary>Un duel de l'évaluation, vu du deck évalué.</summary>
public sealed record GameRecord(int Opponent, int Pair, int Seat, int Outcome, int Rounds, DuelStats? Stats, string? Error)
{
    /// <summary>Joueur (0 ou 1) qui avait le deck évalué.</summary>
    public int Me => Seat;
    public DuelStats.CardLine[]? MyCards => Stats?.Cards[Seat];
}

/// <summary>Moyennes par duel d'une carte du deck évalué.</summary>
public sealed record CardAverages(DuelSlot Slot, double DamageDone, double HealingDone, double DamageTaken, double Kills,
    double Casts, double TalentActivations, double ToxBought, double DeathRate,
    double CritHits = 0, double CritBonus = 0, double ElementGiven = 0);

/// <summary>Résultat de l'évaluation d'un deck.</summary>
public sealed record DeckEvaluation(IReadOnlyList<DuelSlot> Deck, IReadOnlyList<GameRecord> Games, TimeSpan Elapsed)
{
    public int Errors => Games.Count(g => g.Error is not null);
    public int Counted => Games.Count - Errors;
    public int Wins => Games.Count(g => g.Error is null && g.Outcome > 0);
    public int Losses => Games.Count(g => g.Error is null && g.Outcome < 0);
    public int Draws => Games.Count(g => g.Error is null && g.Outcome == 0);
    /// <summary>Score : victoires + moitié des égalités, sur les duels comptés (0 à 1).</summary>
    public double Score => Counted == 0 ? 0 : (Wins + 0.5 * Draws) / Counted;
    /// <summary>Marge d'erreur à 95 % (approximation normale).</summary>
    public double Margin95 => Counted == 0 ? 1 : 1.96 * Math.Sqrt(Score * (1 - Score) / Counted);

    /// <summary>Durée moyenne des duels (rounds).</summary>
    public double AverageRounds => Counted == 0 ? 0 : Games.Where(g => g.Error is null).Average(g => g.Rounds);

    /// <summary>Moyennes par duel de chaque carte du deck (dans l'ordre du deck) ; vide sans statistiques.</summary>
    public IReadOnlyList<CardAverages> CardAverages()
    {
        var lines = Games.Where(g => g.Error is null && g.MyCards is not null).Select(g => g.MyCards!).ToList();
        if (lines.Count == 0) return [];
        return Deck.Select((slot, i) => new CardAverages(slot,
            lines.Average(l => l[i].DamageDone), lines.Average(l => l[i].HealingDone), lines.Average(l => l[i].DamageTaken),
            lines.Average(l => l[i].Kills), lines.Average(l => l[i].Casts), lines.Average(l => l[i].TalentActivations),
            lines.Average(l => l[i].ToxBought), lines.Average(l => l[i].Died ? 1.0 : 0.0),
            lines.Average(l => l[i].CritHits), lines.Average(l => l[i].CritBonus), lines.Average(l => l[i].ElementGiven))).ToList();
    }

    /// <summary>Plus haut niveau atteint par élément, en moyenne par duel (côté du deck évalué ; 0 si jamais atteint).</summary>
    public IReadOnlyDictionary<string, double> AveragePeakLevels()
    {
        var games = Games.Where(g => g.Error is null && g.Stats is not null).ToList();
        if (games.Count == 0) return new Dictionary<string, double>();
        return games.SelectMany(g => g.Stats!.PeakLevels[g.Me].Keys).Distinct()
            .ToDictionary(e => e, e => games.Average(g => g.Stats!.PeakLevels[g.Me].GetValueOrDefault(e)));
    }

    /// <summary>Score contre chaque adversaire du groupe (indice → score).</summary>
    public IReadOnlyDictionary<int, double> ScoreByOpponent() => Games.Where(g => g.Error is null).GroupBy(g => g.Opponent)
        .ToDictionary(g => g.Key, g => g.Average(x => x.Outcome > 0 ? 1.0 : x.Outcome == 0 ? 0.5 : 0.0));

    public string Summary =>
        $"{Counted} duels · victoires : {Wins} · défaites : {Losses} · égalités : {Draws} · erreurs : {Errors}{Environment.NewLine}" +
        $"Score : {100 * Score:0.0} % (± {100 * Margin95:0.0} %) · durée : {Elapsed.TotalSeconds:0.0} s";
}

/// <summary>
/// Évalue la force d'un deck (phase 8, lot A) : il affronte chaque deck du groupe de référence, les deux camps
/// étant joués par le même joueur ; les duels vont par paires (même graine, places échangées).
/// Même deck + même groupe + mêmes réglages = même résultat.
/// </summary>
public static class DeckEvaluator
{
    public static DeckEvaluation Evaluate(SimKnowledge kb, IReadOnlyList<DuelSlot> deck, ReferenceField field,
        EvaluationSettings? settings = null, CancellationToken cancel = default, IProgress<int>? progress = null) =>
        EvaluateMany(kb, [deck], field, settings, cancel, progress)[0];

    /// <summary>
    /// Évalue plusieurs decks d'un coup (tous les duels en parallèle) : même résultat que de les évaluer un par un,
    /// mais les cœurs restent occupés. <paramref name="progress"/> reçoit le nombre de duels joués.
    /// </summary>
    public static IReadOnlyList<DeckEvaluation> EvaluateMany(SimKnowledge kb, IReadOnlyList<IReadOnlyList<DuelSlot>> decks,
        ReferenceField field, EvaluationSettings? settings = null, CancellationToken cancel = default, IProgress<int>? progress = null)
    {
        var s = settings ?? new EvaluationSettings();
        var factory = PlayerCatalog.Factory(s.Player) ?? throw new ArgumentException($"Joueur inconnu : {s.Player}.");
        var perDeck = field.Decks.Count * s.PairsPerOpponent * 2;
        var records = new GameRecord[decks.Count * perDeck];
        var done = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew();

        void Play(int k)
        {
            cancel.ThrowIfCancellationRequested();
            var deck = decks[k / perDeck];
            var j = k % perDeck;
            var (o, p, seat) = (j / (2 * s.PairsPerOpponent), j / 2 % s.PairsPerOpponent, j % 2);
            var seeds = Seeds(s.Seed, o, p);
            try
            {
                var opp = field.Decks[o];
                var duel = new Duel(kb, seat == 0 ? deck : opp, seat == 0 ? opp : deck, seeds.Duel) { Log = { Enabled = false } };
                if (s.CollectStats) duel.Stats = new DuelStats(kb.Settings.CardsPerDeck);
                // Les joueurs gardent leur graine d'une place à l'autre : seul le deck change de côté.
                var r = DuelRunner.Play(duel, factory(seat == 0 ? seeds.Me : seeds.Them), factory(seat == 0 ? seeds.Them : seeds.Me));
                var outcome = !r.Finished || r.Winner is null or -1 ? 0 : r.Winner == seat ? 1 : -1;
                records[k] = new GameRecord(o, p, seat, outcome, r.Rounds, duel.Stats, r.Finished ? null : "partie non terminée");
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                records[k] = new GameRecord(o, p, seat, 0, 0, null, e.GetType().Name + " : " + e.Message);
            }
            if (progress is not null) progress.Report(Interlocked.Increment(ref done));
        }

        if (s.Parallel)
            System.Threading.Tasks.Parallel.For(0, records.Length, new ParallelOptions { CancellationToken = cancel }, Play);
        else
            for (var k = 0; k < records.Length; k++) Play(k);
        var elapsed = watch.Elapsed;
        return decks.Select((d, i) => new DeckEvaluation(d, records[(i * perDeck)..((i + 1) * perDeck)], elapsed)).ToList();
    }

    /// <summary>Graines d'une paire, tirées de (graine, adversaire, paire) seulement.</summary>
    internal static (ulong Duel, ulong Me, ulong Them) Seeds(ulong seed, int opponent, int pair)
    {
        var rng = new Rng(seed ^ (0x9E3779B97F4A7C15UL * (ulong)(opponent + 1)) ^ (0xC2B2AE3D27D4EB4FUL * (ulong)(pair + 1)));
        return (rng.NextULong(), rng.NextULong(), rng.NextULong());
    }
}
