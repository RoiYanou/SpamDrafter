using SpamDrafter.Simulator.Data;
using SpamDrafter.Simulator.Engine;
using SpamDrafter.Simulator.Players;

namespace SpamDrafter.Simulator.Tests;

/// <summary>Phase 8, lot A : statistiques de duel.</summary>
public class Phase8LotATests
{
    private static readonly Lazy<SimKnowledge?> Kb = new(() =>
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "knowledge", "1.30");
            if (File.Exists(Path.Combine(p, "manifest.json"))) return SimKnowledge.Load(p);
        }
        return null;
    });

    [Fact]
    public void Un_coup_compte_les_degats_faits_et_subis_et_le_lancer()
    {
        var w = new TestWorld();
        var duel = w.Duel([w.Card("A", abilities: w.Strike())], [w.Card("B")]);
        duel.Stats = new DuelStats(4);
        var a = duel.Players[0].Board[0];
        var b = duel.Players[1].Board[0];
        duel.Apply(duel.LegalActions().First(o => o.Kind == ActionKind.Cast && o.Cast!.Target == b));
        var lost = b.HpMax - b.Hp;
        Assert.True(lost > 0);
        Assert.Equal(lost, duel.Stats.Of(a).DamageDone);
        Assert.Equal(lost, duel.Stats.Of(b).DamageTaken);
        Assert.Equal(1, duel.Stats.Of(a).Casts);
        Assert.Equal(0, duel.Stats.Of(b).DamageDone);
    }

    [Fact]
    public void Une_elimination_est_comptee()
    {
        var w = new TestWorld();
        var duel = w.Duel([w.Card("A", abilities: w.Strike(value: 5000))], [w.Card("B", hp: 100), w.Card("B2")]);
        duel.Stats = new DuelStats(4);
        var a = duel.Players[0].Board[0];
        var b = duel.Players[1].Board.First(c => c.Def.Id == "B");
        duel.Apply(duel.LegalActions().First(o => o.Kind == ActionKind.Cast && o.Cast!.Target == b));
        Assert.False(b.IsAlive);
        Assert.Equal(1, duel.Stats.Of(a).Kills);
        Assert.True(duel.Stats.Of(b).Died);
        Assert.Equal(100, duel.Stats.Of(a).DamageDone);   // on ne compte que les PV réellement retirés
    }

    [Fact]
    public void La_place_dans_le_deck_vient_de_l_identifiant()
    {
        var w = new TestWorld();
        var duel = w.Duel([w.Card("A"), w.Card("A2"), w.Card("A3")], [w.Card("B")]);
        foreach (var c in duel.Players[0].Board)
            Assert.Equal(c.Def.Id switch { "A" => 0, "A2" => 1, _ => 2 }, DuelStats.DeckIndex(c));
    }

    [Fact]
    public void Les_copies_de_l_IA_ne_relevent_pas_de_statistiques()
    {
        var w = new TestWorld();
        var duel = w.Duel([w.Card("A", abilities: w.Strike())], [w.Card("B")]);
        duel.Stats = new DuelStats(4);
        Assert.Null(duel.Clone().Stats);
        Assert.Null(duel.Copy().Stats);
    }

    [Fact]
    public void Statistiques_coherentes_et_sans_effet_sur_la_partie()
    {
        if (Kb.Value is not { } kb) return;
        var rng = new Rng(4242);
        for (var g = 0; g < 60; g++)
        {
            var d0 = Bench.RandomDeck(kb, rng);
            var d1 = Bench.RandomDeck(kb, rng);
            ulong seed = rng.NextULong(), s0 = rng.NextULong(), s1 = rng.NextULong();
            var with = new Duel(kb, d0, d1, seed) { Log = { Enabled = false }, Stats = new DuelStats(4) };
            var without = new Duel(kb, d0, d1, seed) { Log = { Enabled = false } };
            DuelRunner.Play(with, new FastPlayer(s0), new FastPlayer(s1));
            DuelRunner.Play(without, new FastPlayer(s0), new FastPlayer(s1));
            Assert.Equal(without.StateKey(), with.StateKey());
            for (var p = 0; p < 2; p++)
            {
                var mine = with.Stats!.Cards[p];
                var theirs = with.Stats.Cards[1 - p];
                Assert.Equal(with.Players[p].KillCount, mine.Sum(c => c.Kills));
                Assert.True(mine.Sum(c => c.DamageDone) <= theirs.Sum(c => c.DamageTaken));
                Assert.All(mine, c => Assert.True(c.DamageDone >= 0 && c.HealingDone >= 0 && c.DamageTaken >= 0));
            }
            Assert.Equal(with.Stats.Purchases.Count, with.Stats.Cards.Sum(s => s.Sum(c => c.ToxBought)));
        }
    }
}
