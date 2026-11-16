using SpamDrafter.Simulator.Data;
using SpamDrafter.Simulator.Engine;
using SpamDrafter.Simulator.Players;
using Xunit.Abstractions;
using static SpamDrafter.Simulator.Tests.TestWorld;

namespace SpamDrafter.Simulator.Tests;

/// <summary>Phase 7, lot C : politique rapide et joueur par recherche.</summary>
public class Phase7LotCTests(ITestOutputHelper output)
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
    public void La_politique_rapide_ne_frappe_pas_ses_allies()
    {
        var w = new TestWorld();
        var duel = w.Duel([w.Card("A", abilities: w.Strike()), w.Card("A2")], [w.Card("B")]);
        var choice = new FastPlayer(1).Choose(duel, duel.LegalActions());
        Assert.Equal(1, choice!.Cast!.Target!.Owner);
    }

    [Fact]
    public void La_politique_rapide_soigne_ses_allies()
    {
        var w = new TestWorld();
        var heal = w.Ability("Soin", "SelectTarget", [Act("healTarget", 100, useMiss: false)]);
        var duel = w.Duel([w.Card("A", abilities: heal), w.Card("A2")], [w.Card("B")]);
        duel.Players[0].Board[1].Damage = 300;
        var choice = new FastPlayer(1).Choose(duel, duel.LegalActions());
        Assert.Equal("A2", choice!.Cast!.Target!.Def.Id);
    }

    [Fact]
    public void Reflechir_ne_modifie_pas_la_vraie_partie()
    {
        if (Kb.Value is not { } kb) return;
        var rng = new Rng(9001);
        for (var g = 0; g < 10; g++)
        {
            var duel = new Duel(kb, Bench.RandomDeck(kb, rng), Bench.RandomDeck(kb, rng), rng.NextULong());
            duel.Log.Enabled = false;
            duel.Start();
            var before = duel.StateKey();
            new SearchPlayer(rng.NextULong()).Choose(duel, duel.LegalActions());
            Assert.Equal(before, duel.StateKey());
        }
    }

    [Fact]
    public void La_recherche_finit_une_carte_qu_elle_peut_tuer()
    {
        var w = new TestWorld();
        var duel = w.Duel([w.Card("A", abilities: w.Strike(value: 150))],
            [w.Card("Fort", hp: 1000), w.Card("Faible", hp: 120)]);
        var choice = new SearchPlayer(3).Choose(duel, duel.LegalActions());
        Assert.Equal("Faible", choice!.Cast!.Target!.Def.Id);
    }

    [Fact]
    public void La_recherche_ne_distingue_pas_les_cartes_cachees_par_la_fumee()
    {
        var targets = new HashSet<string>();
        for (ulong seed = 1; seed <= 30; seed++)
        {
            var w = new TestWorld();
            var duel = w.Duel([w.Card("A", abilities: w.Strike(value: 150))],
                [w.Card("Fort", hp: 1000), w.Card("Faible", hp: 120)]);
            foreach (var c in duel.Players[1].Board) c.AddStatus(StatusKind.Smoked, 0, 0);
            var choice = new SearchPlayer(seed).Choose(duel, duel.LegalActions());
            targets.Add(choice!.Cast!.Target!.Def.Id);
        }
        Assert.Equal(["Faible", "Fort"], targets.Order());
    }

    [Trait("Categorie", "Lent")]
    [Fact]
    public void La_recherche_joue_des_parties_completes_reproductibles()
    {
        if (Kb.Value is not { } kb) return;
        var r1 = Bench.Run(kb, s => new SearchPlayer(s), s => new RandomPlayer(s), 20, 4);
        var r2 = Bench.Run(kb, s => new SearchPlayer(s), s => new RandomPlayer(s), 20, 4);
        output.WriteLine(r1.Summary("recherche", "hasard"));
        Assert.Equal(0, r1.Errors);
        Assert.Equal(0, r1.Unfinished);
        Assert.Equal((r1.WinsA, r1.WinsB, r1.Draws), (r2.WinsA, r2.WinsB, r2.Draws));
        Assert.True(r1.ScoreA > 0.6, $"score {r1.ScoreA}");
    }
}
