using SpamDrafter.Simulator.Data;
using SpamDrafter.Simulator.Engine;
using SpamDrafter.Simulator.Players;
using Xunit.Abstractions;
using static SpamDrafter.Simulator.Tests.TestWorld;

namespace SpamDrafter.Simulator.Tests;

/// <summary>Phase 7, lot B : copie directe, note de position, joueur heuristique.</summary>
public class Phase7LotBTests(ITestOutputHelper output)
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

    private static void PlayFor(Duel duel, IDuelPlayer p0, IDuelPlayer p1, int steps)
    {
        var players = new[] { p0, p1 };
        duel.Deciders[0] = p0;
        duel.Deciders[1] = p1;
        if (duel.Phase == DuelPhase.NotStarted) duel.Start();
        for (var n = 0; n < steps && duel.Phase == DuelPhase.Main; n++)
        {
            var choice = players[duel.CurrentPlayer].Choose(duel, duel.LegalActions());
            if (choice is null) duel.EndTurn(); else duel.Apply(choice);
        }
    }

    // ================================================================ copie directe

    [Fact]
    public void La_copie_directe_egale_la_copie_par_rejeu_et_continue_pareil()
    {
        if (Kb.Value is not { } kb) return;
        var rng = new Rng(8001);
        for (var g = 0; g < 300; g++)
        {
            var duel = new Duel(kb, Bench.RandomDeck(kb, rng), Bench.RandomDeck(kb, rng), rng.NextULong());
            duel.Log.Enabled = false;
            PlayFor(duel, new RandomPlayer(rng.NextULong()), new RandomPlayer(rng.NextULong()), rng.Next(0, 150));
            var clone = duel.Clone();
            var copy = duel.Copy();
            Assert.Equal(copy.StateKey(), clone.StateKey());

            // Le hasard caché et l'état interne (dernière cible, talents…) sont copiés : même suite jusqu'au bout.
            var s1 = rng.NextULong();
            var s2 = rng.NextULong();
            var r1 = DuelRunner.Play(copy, new RandomPlayer(s1), new RandomPlayer(s2));
            var r2 = DuelRunner.Play(clone, new RandomPlayer(s1), new RandomPlayer(s2));
            Assert.Equal(r1, r2);
            Assert.Equal(copy.StateKey(), clone.StateKey());
            // La copie directe garde l'historique : sa copie par rejeu lui est identique.
            Assert.Equal(clone.StateKey(), clone.Copy().StateKey());
        }
    }

    [Fact]
    public void Jouer_la_copie_directe_ne_touche_pas_l_original()
    {
        if (Kb.Value is not { } kb) return;
        var rng = new Rng(8002);
        for (var g = 0; g < 60; g++)
        {
            var duel = new Duel(kb, Bench.RandomDeck(kb, rng), Bench.RandomDeck(kb, rng), rng.NextULong());
            duel.Log.Enabled = false;
            PlayFor(duel, new RandomPlayer(rng.NextULong()), new RandomPlayer(rng.NextULong()), rng.Next(0, 100));
            var before = duel.StateKey();
            var clone = duel.Clone(reseed: rng.NextULong());
            DuelRunner.Play(clone, new RandomPlayer(rng.NextULong()), new RandomPlayer(rng.NextULong()));
            Assert.Equal(before, duel.StateKey());
        }
    }

    [Fact]
    public void La_copie_directe_est_rapide()
    {
        if (Kb.Value is not { } kb) return;
        var rng = new Rng(8003);
        var duels = Enumerable.Range(0, 20).Select(_ =>
        {
            var d = new Duel(kb, Bench.RandomDeck(kb, rng), Bench.RandomDeck(kb, rng), rng.NextULong());
            d.Log.Enabled = false;
            PlayFor(d, new RandomPlayer(rng.NextULong()), new RandomPlayer(rng.NextULong()), 60);
            return d;
        }).ToList();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var n = 0;
        for (var k = 0; k < 200; k++) foreach (var d in duels) { d.Clone(); n++; }
        watch.Stop();
        var avg = watch.Elapsed.TotalMilliseconds / n;
        output.WriteLine($"Copie directe : {avg * 1000:0.0} µs en moyenne");
        Assert.True(avg < 1, $"copie directe trop lente : {avg} ms");
    }

    [Fact]
    public void Pas_de_copie_directe_pendant_un_lancer()
    {
        var w = new TestWorld();
        Exception? caught = null;
        var probe = new ProbeDecider(d => { try { d.Clone(); } catch (InvalidOperationException e) { caught = e; } });
        var chain = w.Ability("Suite", "SelectTarget", [Act("damage", 10)]);
        var main = w.Ability("Debut", "Self", [Act("damage", 0)], chain: ["Suite"]);
        var duel = w.Duel([w.Card("A", abilities: main)], [w.Card("B")]);
        duel.Deciders[0] = probe;
        var a = duel.Players[0].Board[0];
        duel.Cast(a, a.Abilities[0]);
        Assert.True(probe.Called);
        Assert.NotNull(caught);
    }

    private sealed class ProbeDecider(Action<Duel> probe) : IDuelDecider
    {
        public bool Called { get; private set; }
        public CardState ChooseTarget(Duel duel, CardState caster, AbilityDef ability, IReadOnlyList<CardState> targets)
        {
            Called = true;
            probe(duel);
            return targets[0];
        }
        public int ChooseOption(Duel duel, CardState caster, AbilityDef ability, IReadOnlyList<AbilityDef> options) => 0;
    }

    // ================================================================ correction : morts du début de tour

    [Fact]
    public void Entre_deux_actions_rien_n_attend_dans_la_file()
    {
        // 🔒 GameLogic.Update : les effets « à ma mort » d'une carte tuée au début du tour (overtime, poison)
        // se résolvent avant que le joueur agisse. Avant la correction, ils attendaient l'action suivante.
        if (Kb.Value is not { } kb) return;
        var rng = new Rng(8004);
        for (var g = 0; g < 300; g++)
        {
            var duel = new Duel(kb, Bench.RandomDeck(kb, rng), Bench.RandomDeck(kb, rng), rng.NextULong());
            duel.Log.Enabled = false;
            var players = new IDuelPlayer[] { new RandomPlayer(rng.NextULong()), new RandomPlayer(rng.NextULong()) };
            duel.Deciders[0] = players[0];
            duel.Deciders[1] = players[1];
            duel.Start();
            for (var n = 0; n < 3000 && duel.Phase == DuelPhase.Main; n++)
            {
                duel.Clone();   // refuse si une résolution attend encore
                var c = players[duel.CurrentPlayer].Choose(duel, duel.LegalActions());
                if (c is null) duel.EndTurn(); else duel.Apply(c);
            }
        }
    }

    // ================================================================ note de position

    [Fact]
    public void La_note_est_symetrique_et_une_victoire_domine()
    {
        var w = new TestWorld();
        var duel = w.Duel([w.Card("A", hp: 500, abilities: w.Strike())], [w.Card("B", hp: 800, abilities: w.Strike("Cogner"))]);
        Assert.Equal(Evaluator.Score(duel, 0), -Evaluator.Score(duel, 1), 6);
        Assert.True(Evaluator.Score(duel, 1) > 0);                       // B a plus de PV
        var a = duel.Players[0].Board[0];
        for (var i = 0; i < 8 && duel.Phase == DuelPhase.Main; i++)
        {
            if (duel.CanCast(a, a.Abilities[0])) duel.Cast(a, a.Abilities[0], duel.Players[1].Board[0]);
            if (duel.Phase == DuelPhase.Main) duel.EndTurn();
            if (duel.Phase == DuelPhase.Main) duel.EndTurn();
        }
        Assert.Equal(0, duel.Winner);
        Assert.Equal(HeuristicWeights.Default.Win, Evaluator.Score(duel, 0));
    }

    [Fact]
    public void Abimer_une_carte_deja_blessee_vaut_plus()
    {
        // Concentration : 100 dégâts sur une carte à 200 PV valent plus que sur une carte à 1 000 PV.
        var w = new TestWorld();
        w.Card("X");
        var kb = w.Build();
        var def = kb.Cards.First();
        CardState Make(int damage) => new("1.1", def, 1, 4, []) { Damage = damage };
        var hw = HeuristicWeights.Default;
        var onHurt = Evaluator.Card(kb, Make(800), hw) - Evaluator.Card(kb, Make(900), hw);
        var onFresh = Evaluator.Card(kb, Make(0), hw) - Evaluator.Card(kb, Make(100), hw);
        Assert.True(onHurt > onFresh);
    }

    // ================================================================ joueur heuristique

    [Fact]
    public void L_heuristique_vise_l_ennemi_et_pas_sa_propre_carte()
    {
        // « Frapper » n'a pas de condition « ennemi » : le joueur au hasard peut se frapper (KI-025), pas l'heuristique.
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var w = new TestWorld();
            var duel = w.Duel([w.Card("A", abilities: w.Strike())], [w.Card("B")]);
            var choice = new HeuristicPlayer(seed).Choose(duel, duel.LegalActions());
            Assert.NotNull(choice);
            Assert.Equal(1, choice!.Cast!.Target!.Owner);
        }
    }

    [Fact]
    public void L_heuristique_finit_son_tour_plutot_que_de_se_blesser()
    {
        var w = new TestWorld();
        var hurt = w.Ability("Douleur", "Self", [Act("damage", 300, useMiss: false)]);
        var duel = w.Duel([w.Card("A", abilities: hurt)], [w.Card("B")]);
        Assert.NotEmpty(duel.LegalActions());
        Assert.Null(new HeuristicPlayer(1).Choose(duel, duel.LegalActions()));
    }

    [Fact]
    public void L_heuristique_finit_une_carte_qu_elle_peut_tuer()
    {
        var w = new TestWorld();
        var duel = w.Duel([w.Card("A", abilities: w.Strike(value: 150))],
            [w.Card("Fort", hp: 1000), w.Card("Faible", hp: 120)]);
        var choice = new HeuristicPlayer(3).Choose(duel, duel.LegalActions());
        Assert.Equal("Faible", choice!.Cast!.Target!.Def.Id);
    }

    [Fact]
    public void L_heuristique_essaie_les_options_d_un_choix_multiple()
    {
        // Les règles simples prendraient l'option « la plus forte » (300, mais sur soi-même) ;
        // en essayant chaque réponse, l'heuristique choisit de frapper l'ennemi.
        var w = new TestWorld();
        w.Ability("EchoChoix_1", "Self", [Act("damage", 300, useMiss: false)]);
        w.Ability("EchoChoix_2", "SelectTarget", [Act("damage", 100)], conditions: ["is_not_empty", "is_enemy"]);
        var choice = w.Ability("Choix", "ChoiceSelector", [Act("_NULL", 0, element: null)], chain: ["EchoChoix_1", "EchoChoix_2"]);
        var duel = w.Duel([w.Card("A", abilities: choice)], [w.Card("B")]);
        var player = new HeuristicPlayer(5);
        duel.Deciders[0] = player;
        var action = player.Choose(duel, duel.LegalActions());
        Assert.NotNull(action);
        duel.Apply(action!);
        Assert.Equal(900, duel.Players[1].Board[0].Hp);
        Assert.Equal(1000, duel.Players[0].Board[0].Hp);
    }

    [Fact]
    public void L_heuristique_ne_distingue_pas_les_cartes_cachees_par_la_fumee()
    {
        // Sans Fumée, elle tue toujours la carte à 120 PV. Si les deux cartes ennemies sont cachées,
        // elle ne sait pas laquelle est laquelle : elle vise l'une ou l'autre selon le hasard (KI-030).
        var targets = new HashSet<string>();
        for (ulong seed = 1; seed <= 30; seed++)
        {
            var w = new TestWorld();
            var duel = w.Duel([w.Card("A", abilities: w.Strike(value: 150))],
                [w.Card("Fort", hp: 1000), w.Card("Faible", hp: 120)]);
            foreach (var c in duel.Players[1].Board) c.AddStatus(StatusKind.Smoked, 0, 0);
            var choice = new HeuristicPlayer(seed).Choose(duel, duel.LegalActions());
            targets.Add(choice!.Cast!.Target!.Def.Id);
        }
        Assert.Equal(["Faible", "Fort"], targets.Order());
    }

    [Fact]
    public void L_heuristique_joue_des_parties_completes_sans_erreur()
    {
        if (Kb.Value is not { } kb) return;
        var r = Bench.Run(kb, s => new HeuristicPlayer(s), s => new RandomPlayer(s), 40, 3);
        output.WriteLine(r.Summary("heuristique", "hasard"));
        Assert.Equal(0, r.Errors);
        Assert.Equal(0, r.Unfinished);
        Assert.True(r.ScoreA > 0.6, $"score {r.ScoreA}");
    }
}
