using SpamDrafter.Simulator.Data;
using SpamDrafter.Simulator.Engine;
using SpamDrafter.Simulator.Players;
using Xunit.Abstractions;
using static SpamDrafter.Simulator.Tests.TestWorld;

namespace SpamDrafter.Simulator.Tests;

/// <summary>
/// Phase 7, lot A : fondations du Grand Joueur. Copie d'une partie par rejeu, hasard de réflexion séparé,
/// choix d'emplacement (KI-029), banc d'essai.
/// </summary>
public class Phase7LotATests(ITestOutputHelper output)
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

    /// <summary>Joue au plus <paramref name="steps"/> décisions (actions ou fins de tour).</summary>
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

    // ================================================================ copie par rejeu (vraies cartes)

    [Trait("Categorie", "Lent")]
    [Fact]
    public void Une_copie_est_identique_et_continue_exactement_comme_l_original()
    {
        if (Kb.Value is not { } kb) return;
        var rng = new Rng(7001);
        var copies = 0;
        for (var g = 0; g < 150; g++)
        {
            var duel = new Duel(kb, Bench.RandomDeck(kb, rng), Bench.RandomDeck(kb, rng), rng.NextULong());
            duel.Log.Enabled = false;
            PlayFor(duel, new RandomPlayer(rng.NextULong()), new RandomPlayer(rng.NextULong()), rng.Next(0, 120));
            var copy = duel.Copy();
            Assert.Equal(duel.StateKey(), copy.StateKey());
            copies++;

            // Même suite de décisions → même partie, jusqu'au bout (le hasard caché est copié lui aussi).
            var s1 = rng.NextULong();
            var s2 = rng.NextULong();
            var r1 = DuelRunner.Play(duel, new RandomPlayer(s1), new RandomPlayer(s2));
            var r2 = DuelRunner.Play(copy, new RandomPlayer(s1), new RandomPlayer(s2));
            Assert.Equal(r1, r2);
            Assert.Equal(duel.StateKey(), copy.StateKey());
        }
        output.WriteLine($"{copies} copies vérifiées");
    }

    [Fact]
    public void La_copie_d_une_copie_est_identique()
    {
        if (Kb.Value is not { } kb) return;
        var rng = new Rng(7002);
        for (var g = 0; g < 30; g++)
        {
            var duel = new Duel(kb, Bench.RandomDeck(kb, rng), Bench.RandomDeck(kb, rng), rng.NextULong());
            PlayFor(duel, new RandomPlayer(rng.NextULong()), new RandomPlayer(rng.NextULong()), 40);
            var c1 = duel.Copy(reseed: rng.NextULong());
            PlayFor(c1, new RandomPlayer(rng.NextULong()), new RandomPlayer(rng.NextULong()), 30);
            var c2 = c1.Copy();
            Assert.Equal(c1.StateKey(), c2.StateKey());
        }
    }

    [Trait("Categorie", "Lent")]
    [Fact]
    public void Un_nouveau_hasard_garde_le_present_change_l_avenir_et_ne_touche_pas_l_original()
    {
        if (Kb.Value is not { } kb) return;
        var rng = new Rng(7003);
        var different = 0;
        for (var g = 0; g < 40; g++)
        {
            var duel = new Duel(kb, Bench.RandomDeck(kb, rng), Bench.RandomDeck(kb, rng), rng.NextULong());
            duel.Log.Enabled = false;
            PlayFor(duel, new RandomPlayer(rng.NextULong()), new RandomPlayer(rng.NextULong()), 10);
            var before = duel.StateKey();
            var same = duel.Copy();
            var other = duel.Copy(reseed: rng.NextULong());
            Assert.Equal(before, other.StateKey());              // le présent est le même

            var s1 = rng.NextULong();
            var s2 = rng.NextULong();
            DuelRunner.Play(same, new RandomPlayer(s1), new RandomPlayer(s2));
            DuelRunner.Play(other, new RandomPlayer(s1), new RandomPlayer(s2));
            if (same.StateKey() != other.StateKey()) different++;
            Assert.Equal(before, duel.StateKey());               // jouer les copies ne change pas l'original
        }
        Assert.True(different >= 35, $"seulement {different} / 40 avenirs différents");
    }

    [Trait("Categorie", "Lent")]
    [Fact]
    public void Les_choix_pendant_un_lancer_sont_rejoues()
    {
        // Le joueur au hasard choisit les cibles des enchaînements et les options : la copie doit reprendre ses choix.
        if (Kb.Value is not { } kb) return;
        var rng = new Rng(7004);
        var withChoices = 0;
        for (var g = 0; g < 60; g++)
        {
            var duel = new Duel(kb, Bench.RandomDeck(kb, rng), Bench.RandomDeck(kb, rng), rng.NextULong());
            duel.Log.Enabled = false;
            DuelRunner.Play(duel, new RandomPlayer(rng.NextULong()), new RandomPlayer(rng.NextULong()));
            if (duel.Choices.Count > 0) withChoices++;
            Assert.Equal(duel.StateKey(), duel.Copy().StateKey());
        }
        Assert.True(withChoices > 0);
    }

    [Trait("Categorie", "Lent")]
    [Fact]
    public void Copier_une_partie_coute_peu()
    {
        if (Kb.Value is not { } kb) return;
        var rng = new Rng(7005);
        var duels = Enumerable.Range(0, 20).Select(_ =>
        {
            var d = new Duel(kb, Bench.RandomDeck(kb, rng), Bench.RandomDeck(kb, rng), rng.NextULong());
            d.Log.Enabled = false;
            PlayFor(d, new RandomPlayer(rng.NextULong()), new RandomPlayer(rng.NextULong()), 60);
            return d;
        }).ToList();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var n = 0;
        for (var k = 0; k < 10; k++) foreach (var d in duels) { d.Copy(); n++; }
        watch.Stop();
        var avg = watch.Elapsed.TotalMilliseconds / n;
        output.WriteLine($"Copie d'une partie (~60 décisions) : {avg:0.000} ms en moyenne");
        Assert.True(avg < 50, $"copie trop lente : {avg} ms");
    }

    // ================================================================ copie (monde d'essai)

    [Fact]
    public void Une_partie_pas_encore_lancee_se_copie()
    {
        var w = new TestWorld();
        var duel = w.Duel([w.Card("A", abilities: w.Strike())], [w.Card("B", abilities: w.Strike("Cogner"))], start: false);
        var copy = duel.Copy();
        Assert.Equal(DuelPhase.NotStarted, copy.Phase);
        duel.Start();
        copy.Start();
        Assert.Equal(duel.StateKey(), copy.StateKey());
    }

    [Fact]
    public void Une_action_prise_sur_une_copie_se_rejoue_sur_l_original()
    {
        var w = new TestWorld();
        var duel = w.Duel([w.Card("A", abilities: w.Strike())], [w.Card("B", abilities: w.Strike("Cogner"))]);
        var copy = duel.Copy();
        var action = copy.LegalActions().First(a => a.Cast?.Target?.Owner == 1);
        var r = copy.RefOf(action);
        copy.Apply(action);
        duel.Apply(duel.Resolve(r));
        Assert.Equal(copy.StateKey(), duel.StateKey());
        Assert.Equal(900, duel.Players[1].Board[0].Hp);
    }

    // ================================================================ emplacement choisi (KI-029)

    private static (Duel duel, CardState me, CardState foe, AbilityState lullaby) SlotSetup(TestWorld w)
    {
        var lullaby = w.Ability("Berceuse", "SelectTarget", [Act("AddSBouclier", 100, useMiss: false)], choosesSlot: true);
        var me = w.Card("Moi", abilities: lullaby);
        var foe = w.Card("Lui", abilities: [w.Strike("Un"), w.Strike("Deux"), w.Strike("Trois")]);
        var duel = w.Duel([me], [foe]);
        var m = duel.Players[0].Board[0];
        return (duel, m, duel.Players[1].Board[0], m.Abilities[0]);
    }

    [Fact]
    public void Carte_pleine_le_joueur_choisit_la_capacite_remplacee()
    {
        var w = new TestWorld();
        var (duel, me, foe, lullaby) = SlotSetup(w);
        var origins = duel.LegalCasts().Where(c => c.Ability == lullaby && c.Target == foe).Select(c => c.Origin).ToList();
        Assert.Equal(["A:Un", "A:Deux", "A:Trois"], origins);     // carte pleine : pas d'emplacement vide

        duel.Cast(me, lullaby, foe, "A:Deux");
        Assert.Equal(["Un", "Trois"], foe.Abilities.Select(a => a.Def.Id));
        Assert.True(foe.HasTox("Shield"));
    }

    [Fact]
    public void Sans_choix_le_jeu_remplace_la_derniere_capacite()
    {
        var w = new TestWorld();
        var (duel, me, foe, lullaby) = SlotSetup(w);
        duel.Cast(me, lullaby, foe);
        Assert.Equal(["Un", "Deux"], foe.Abilities.Select(a => a.Def.Id));
        Assert.True(foe.HasTox("Shield"));
    }

    [Fact]
    public void Carte_non_pleine_l_emplacement_vide_est_propose_et_ne_retire_rien()
    {
        var w = new TestWorld();
        var lullaby = w.Ability("Berceuse", "SelectTarget", [Act("AddSBouclier", 100, useMiss: false)], choosesSlot: true);
        var duel = w.Duel([w.Card("Moi", abilities: lullaby)], [w.Card("Lui", abilities: w.Strike("Un"))]);
        var me = duel.Players[0].Board[0];
        var foe = duel.Players[1].Board[0];
        var origins = duel.LegalCasts().Where(c => c.Target == foe).Select(c => c.Origin).ToList();
        Assert.Equal([null, "A:Un"], origins);
        duel.Cast(me, me.Abilities[0], foe, null);
        Assert.Equal(["Un"], foe.Abilities.Select(a => a.Def.Id));
        Assert.True(foe.HasTox("Shield"));
    }

    [Fact]
    public void Un_emplacement_invalide_est_refuse()
    {
        var w = new TestWorld();
        var (duel, me, foe, lullaby) = SlotSetup(w);
        Assert.Throws<InvalidOperationException>(() => duel.Cast(me, lullaby, foe, "A:Inconnue"));
        Assert.Throws<InvalidOperationException>(() => duel.Cast(me, lullaby, foe, "T:0"));
        // Une capacité sans « choosesSlot » n'accepte pas d'emplacement.
        var w2 = new TestWorld();
        var d2 = w2.Duel([w2.Card("A", abilities: w2.Strike())], [w2.Card("B")]);
        var a = d2.Players[0].Board[0];
        Assert.Throws<InvalidOperationException>(() => d2.Cast(a, a.Abilities[0], d2.Players[1].Board[0], "A:Frapper"));
    }

    [Fact]
    public void Le_choix_d_emplacement_est_rejoue_par_la_copie()
    {
        var w = new TestWorld();
        var (duel, me, foe, lullaby) = SlotSetup(w);
        duel.Cast(me, lullaby, foe, "A:Un");
        var copy = duel.Copy();
        Assert.Equal(duel.StateKey(), copy.StateKey());
        Assert.Equal(["Deux", "Trois"], copy.Players[1].Board[0].Abilities.Select(a => a.Def.Id));
    }

    // ================================================================ banc d'essai

    [Trait("Categorie", "Lent")]
    [Fact]
    public void Le_banc_d_essai_est_reproductible_et_sans_erreur()
    {
        if (Kb.Value is not { } kb) return;
        IDuelPlayer R(ulong s) => new RandomPlayer(s);
        var r1 = Bench.Run(kb, R, R, 200, 42);
        var r2 = Bench.Run(kb, R, R, 200, 42, parallel: false);
        output.WriteLine(r1.Summary("hasard A", "hasard B"));
        Assert.Equal(200, r1.Games);
        Assert.Equal(0, r1.Errors);
        Assert.Equal(0, r1.Unfinished);
        Assert.Equal((r1.WinsA, r1.WinsB, r1.Draws), (r2.WinsA, r2.WinsB, r2.Draws));
        Assert.Equal(200, r1.WinsA + r1.WinsB + r1.Draws);
    }

    [Fact]
    public void Le_banc_d_essai_echange_les_places()
    {
        // Un joueur qui ne fait rien contre un joueur au hasard qui ne peut viser que l'ennemi :
        // le joueur au hasard gagne toutes les parties, qu'il commence ou non. Les places alternent bien.
        var w = new TestWorld();
        AbilityDef Hit(string id) => w.Ability(id, "SelectTarget", [Act("damage", 100)], conditions: ["is_not_empty", "is_enemy"]);
        w.Card("A", hp: 300, abilities: Hit("Frapper"));
        w.Card("B", hp: 300, abilities: Hit("Cogner"));
        var kb = w.Build();
        IReadOnlyList<DuelSlot> deckA = [new DuelSlot("A")], deckB = [new DuelSlot("B")];
        var r = Bench.Run(kb, s => new RandomPlayer(s), _ => new PassivePlayer(), 20, 1, _ => (deckA, deckB));
        Assert.Equal(20, r.WinsA);
        var r2 = Bench.Run(kb, _ => new PassivePlayer(), s => new RandomPlayer(s), 20, 1, _ => (deckA, deckB));
        Assert.Equal(20, r2.WinsB);
        Assert.Equal(0.0, r2.ScoreA);
    }

    private sealed class PassivePlayer : IDuelPlayer
    {
        public DuelAction? Choose(Duel duel, IReadOnlyList<DuelAction> options) => null;
        public CardState ChooseTarget(Duel duel, CardState caster, AbilityDef ability, IReadOnlyList<CardState> targets) => targets[0];
        public int ChooseOption(Duel duel, CardState caster, AbilityDef ability, IReadOnlyList<AbilityDef> options) => 0;
    }
}
