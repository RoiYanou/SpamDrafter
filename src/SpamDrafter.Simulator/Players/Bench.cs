using SpamDrafter.Simulator.Data;
using SpamDrafter.Simulator.Engine;

namespace SpamDrafter.Simulator.Players;

/// <summary>Bilan d'un banc d'essai entre deux joueurs A et B.</summary>
public sealed record BenchResult(int Games, int WinsA, int WinsB, int Draws, int Errors, int Unfinished,
    IReadOnlyList<string> ErrorSamples, TimeSpan Elapsed)
{
    /// <summary>Parties comptées (sans les erreurs).</summary>
    public int Counted => Games - Errors;

    /// <summary>Score de A : victoires + moitié des égalités, sur les parties comptées (0 à 1).</summary>
    public double ScoreA => Counted == 0 ? 0 : (WinsA + 0.5 * Draws) / Counted;

    /// <summary>Marge d'erreur à 95 % du score de A (approximation normale) : le vrai score est très probablement dans ScoreA ± Margin95.</summary>
    public double Margin95 => Counted == 0 ? 1 : 1.96 * Math.Sqrt(ScoreA * (1 - ScoreA) / Counted);

    public string Summary(string nameA, string nameB) =>
        $"{Games} duels · {nameA} : {WinsA} victoires · {nameB} : {WinsB} · égalités : {Draws}" +
        $" · erreurs : {Errors} · non terminés : {Unfinished}{Environment.NewLine}" +
        $"Score de {nameA} : {100 * ScoreA:0.0} % (± {100 * Margin95:0.0} %) · durée : {Elapsed.TotalSeconds:0.0} s";
}

/// <summary>
/// Banc d'essai (phase 7) : fait jouer N duels entre deux joueurs. Les duels vont par paires : même deck(s),
/// même graine, places échangées. Chaque joueur joue donc autant de fois en premier qu'en second, et la chance
/// du tirage compte pareil pour les deux. Résultat reproductible : même graine = même bilan.
/// </summary>
public static class Bench
{
    /// <summary>Deck au hasard : 4 cartes différentes, chacune avec un talent au hasard ou aucun (comme le Deck builder).</summary>
    public static List<DuelSlot> RandomDeck(SimKnowledge kb, Rng rng)
    {
        var ids = kb.Cards.Select(c => c.Id).Order(StringComparer.Ordinal).ToList();
        return ids.OrderBy(_ => rng.NextULong()).Take(kb.Settings.CardsPerDeck).Select(id =>
        {
            var talents = kb.Card(id).Talents;
            var k = rng.Next(0, talents.Count + 1);
            return new DuelSlot(id, k == 0 ? null : talents[k - 1]);
        }).ToList();
    }

    /// <param name="playerA">Fabrique du joueur A à partir d'une graine.</param>
    /// <param name="playerB">Fabrique du joueur B.</param>
    /// <param name="games">Nombre de duels (arrondi au nombre pair supérieur).</param>
    /// <param name="decks">Decks de A et de B pour une paire ; par défaut, un même deck au hasard pour les deux.</param>
    public static BenchResult Run(SimKnowledge kb, Func<ulong, IDuelPlayer> playerA, Func<ulong, IDuelPlayer> playerB,
        int games, ulong seed, Func<Rng, (IReadOnlyList<DuelSlot> A, IReadOnlyList<DuelSlot> B)>? decks = null,
        bool parallel = true)
    {
        var pairs = (games + 1) / 2;
        decks ??= r => { var d = RandomDeck(kb, r); return (d, d); };
        // Tout le hasard est tiré d'avance, dans l'ordre : le bilan ne dépend pas de l'ordre d'exécution.
        var rng = new Rng(seed);
        var plans = Enumerable.Range(0, pairs).Select(_ =>
        {
            var (a, b) = decks(rng);
            return (A: a, B: b, Duel: rng.NextULong(), PA: rng.NextULong(), PB: rng.NextULong());
        }).ToList();

        var outcomes = new (int Score, bool Finished, string? Error)[pairs * 2];   // Score : 1 = A, −1 = B, 0 = égalité
        var watch = System.Diagnostics.Stopwatch.StartNew();
        void PlayPair(int i)
        {
            var plan = plans[i];
            for (var seat = 0; seat < 2; seat++)   // seat 0 : A est le joueur 1 ; seat 1 : A est le joueur 2
            {
                var g = 2 * i + seat;
                try
                {
                    var (d0, d1) = seat == 0 ? (plan.A, plan.B) : (plan.B, plan.A);
                    var duel = new Duel(kb, d0, d1, plan.Duel) { Log = { Enabled = false } };
                    var a = playerA(plan.PA);
                    var b = playerB(plan.PB);
                    var r = seat == 0 ? DuelRunner.Play(duel, a, b) : DuelRunner.Play(duel, b, a);
                    var aId = seat;
                    var score = r.Winner is null or -1 ? 0 : r.Winner == aId ? 1 : -1;
                    outcomes[g] = (score, r.Finished, null);
                }
                catch (Exception e)
                {
                    outcomes[g] = (0, false, $"duel {g} (graine {plan.Duel}) : {e.GetType().Name} : {e.Message}");
                }
            }
        }
        if (parallel) ComputeLimits.For(pairs, PlayPair);
        else for (var i = 0; i < pairs; i++) PlayPair(i);
        watch.Stop();

        var errors = outcomes.Where(o => o.Error is not null).ToList();
        var ok = outcomes.Where(o => o.Error is null).ToList();
        return new BenchResult(outcomes.Length,
            ok.Count(o => o.Score == 1), ok.Count(o => o.Score == -1), ok.Count(o => o.Score == 0),
            errors.Count, ok.Count(o => !o.Finished),
            errors.Select(o => o.Error!).Take(5).ToList(), watch.Elapsed);
    }
}
