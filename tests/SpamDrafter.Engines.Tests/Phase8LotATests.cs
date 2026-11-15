using SpamDrafter.Engines.Evaluation;
using SpamDrafter.Simulator.Data;
using SpamDrafter.Simulator.Engine;
using SpamDrafter.Simulator.Players;

namespace SpamDrafter.Engines.Tests;

/// <summary>Phase 8, lot A : évaluation d'un deck contre un groupe de référence.</summary>
public class Phase8LotATests
{
    internal static readonly Lazy<SimKnowledge?> Kb = new(() =>
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "knowledge", "1.30");
            if (File.Exists(Path.Combine(p, "manifest.json"))) return SimKnowledge.Load(p);
        }
        return null;
    });

    [Fact]
    public void Spearman_ordre_identique_inverse_et_ex_aequo()
    {
        Assert.Equal(1, Statistics.Spearman([1, 2, 3, 4], [10, 20, 30, 40]), 9);
        Assert.Equal(-1, Statistics.Spearman([1, 2, 3, 4], [4, 3, 2, 1]), 9);
        Assert.Equal([1.5, 1.5, 3], Statistics.Ranks([5, 5, 7]));
        Assert.Equal(1, Statistics.Spearman([1, 2, 3], [1, 4, 9]), 9);   // seul l'ordre compte
    }

    [Fact]
    public void Le_groupe_de_reference_est_reproductible()
    {
        if (Kb.Value is not { } kb) return;
        var a = ReferenceField.Random(kb, 10, 7);
        var b = ReferenceField.Random(kb, 10, 7);
        var c = ReferenceField.Random(kb, 10, 8);
        Assert.Equal(a.Decks.Select(Key), b.Decks.Select(Key));
        Assert.NotEqual(a.Decks.Select(Key), c.Decks.Select(Key));
        Assert.All(a.Decks, d => Assert.Equal(d.Count, d.Select(s => s.CardId).Distinct().Count()));   // D-020
    }

    [Fact]
    public void L_evaluation_est_reproductible_en_parallele_ou_non()
    {
        if (Kb.Value is not { } kb) return;
        var field = ReferenceField.Random(kb, 6, 11);
        var deck = Bench.RandomDeck(kb, new Rng(3));
        var s = new EvaluationSettings { Player = "rapide", PairsPerOpponent = 2, Seed = 99 };
        var a = DeckEvaluator.Evaluate(kb, deck, field, s);
        var b = DeckEvaluator.Evaluate(kb, deck, field, s with { Parallel = false });
        Assert.Equal(24, a.Games.Count);
        Assert.Equal(0, a.Errors);
        Assert.Equal(a.Games.Select(g => (g.Opponent, g.Pair, g.Seat, g.Outcome, g.Rounds)),
                     b.Games.Select(g => (g.Opponent, g.Pair, g.Seat, g.Outcome, g.Rounds)));
        Assert.Equal(a.CardAverages(), b.CardAverages());
    }

    [Fact]
    public void Les_graines_ne_dependent_que_de_l_adversaire_et_de_la_paire()
    {
        Assert.Equal(DeckEvaluator.Seeds(5, 2, 1), DeckEvaluator.Seeds(5, 2, 1));
        Assert.NotEqual(DeckEvaluator.Seeds(5, 2, 1), DeckEvaluator.Seeds(5, 1, 2));
        Assert.NotEqual(DeckEvaluator.Seeds(5, 2, 1), DeckEvaluator.Seeds(6, 2, 1));
    }

    [Fact]
    public void Un_deck_contre_lui_meme_fait_environ_50_pour_cent()
    {
        if (Kb.Value is not { } kb) return;
        var deck = Bench.RandomDeck(kb, new Rng(21));
        var ev = DeckEvaluator.Evaluate(kb, deck, ReferenceField.Of([deck, deck, deck, deck, deck]),
            new EvaluationSettings { Player = "rapide", PairsPerOpponent = 40 });
        Assert.InRange(ev.Score, 0.40, 0.60);
    }

    [Fact]
    public void Les_moyennes_par_carte_suivent_le_deck()
    {
        if (Kb.Value is not { } kb) return;
        var deck = Bench.RandomDeck(kb, new Rng(5));
        var field = ReferenceField.Random(kb, 5, 1);
        var ev = DeckEvaluator.Evaluate(kb, deck, field, new EvaluationSettings { Player = "rapide" });
        var avg = ev.CardAverages();
        Assert.Equal(deck, avg.Select(c => c.Slot));
        Assert.All(avg, c => Assert.InRange(c.DeathRate, 0, 1));
        Assert.True(avg.Sum(c => c.Casts) > 0);
        var noStats = DeckEvaluator.Evaluate(kb, deck, field, new EvaluationSettings { Player = "rapide", CollectStats = false });
        Assert.Empty(noStats.CardAverages());
        Assert.Equal(ev.Score, noStats.Score);   // relever les statistiques ne change pas les parties
    }

    [Fact]
    public void L_evaluation_peut_etre_annulee()
    {
        if (Kb.Value is not { } kb) return;
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            DeckEvaluator.Evaluate(kb, Bench.RandomDeck(kb, new Rng(1)), ReferenceField.Random(kb, 5, 1),
                new EvaluationSettings { Player = "rapide" }, cts.Token));
    }

    [Fact]
    public void Un_joueur_inconnu_est_refuse()
    {
        if (Kb.Value is not { } kb) return;
        Assert.Throws<ArgumentException>(() => DeckEvaluator.Evaluate(kb, Bench.RandomDeck(kb, new Rng(1)),
            ReferenceField.Random(kb, 1, 1), new EvaluationSettings { Player = "inconnu" }));
    }

    private static string Key(IReadOnlyList<DuelSlot> d) => string.Join(",", d.Select(s => s.CardId + "/" + s.TalentId));
}
