using SpamDrafter.Simulator.Data;
using SpamDrafter.Simulator.Engine;
using SpamDrafter.Simulator.Players;
using static SpamDrafter.Simulator.Tests.TestWorld;

namespace SpamDrafter.Simulator.Tests;

/// <summary>Phase 8, lot H : renforts d'abord, note des combos, planificateur de tour.</summary>
public class Phase8LotHTests
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
    public void Le_joueur_rapide_lance_d_abord_ses_renforts()
    {
        var w = new TestWorld();
        var buff = w.Ability("Elan", "PlayerSelf", [Act("AddAllPlayerAttack", 2)]);
        // Le premier lancer proposé (dans l'ordre des capacités) est l'attaque ; le renfort doit passer devant.
        var duel = w.Duel([w.Card("A", abilities: w.Strike()), w.Card("A2", abilities: buff)], [w.Card("B")]);
        Assert.True(FastPlayer.BuffValue(duel.Knowledge, buff) > 0);
        var choice = new FastPlayer(1).Choose(duel, duel.LegalActions());
        Assert.Equal("Elan", choice!.Cast!.Ability.Def.Id);
        var old = new FastPlayer(1) { BuffFirst = false }.Choose(duel, duel.LegalActions());
        Assert.Equal("Frapper", old!.Cast!.Ability.Def.Id);
    }

    [Fact]
    public void Un_bonus_permanent_de_degats_de_base_a_une_valeur()
    {
        // Audit (lot H) : l'IA ignorait les « investissements » comme Explosion (+10 dégâts de base après chaque usage).
        var w = new TestWorld();
        var duel = w.Duel([w.Card("A", abilities: w.Strike())], [w.Card("B")]);
        var before = Evaluator.Score(duel, 0);
        var beforeV1 = Evaluator.Score(duel, 0, HeuristicWeights.V1);
        duel.Players[0].Board[0].Abilities[0].BaseDamageBonus = 10;
        Assert.True(Evaluator.Score(duel, 0) > before);
        Assert.Equal(beforeV1, Evaluator.Score(duel, 0, HeuristicWeights.V1));   // l'ancienne note l'ignorait
    }

    [Fact]
    public void Un_joueur_humain_peut_viser_sa_propre_carte_avec_une_condition_reservee_a_l_IA()
    {
        // 🔒 ConditionOwnerAI : « ai_is_enemy » ne restreint que l'IA du jeu (D-053). Ex. réel : Corne 75 sur sa
        // Prisonnière de la Machine presque morte pour la remonter à 75 % de ses PV.
        if (Kb.Value is not { } kb) return;
        string Id(string n) => kb.Cards.First(c => kb.CardName(c) == n).Id;
        var deck = new[] { "Prisonnière de la Machine", "Dragon de la Cité Infernale", "Cornu mythologique", "Roi Feu" }.Select(n => new DuelSlot(Id(n))).ToList();
        var opp = new[] { "Hibernatus", "L'Inventeur", "Crabe Joyau-Monde", "Aidan" }.Select(n => new DuelSlot(Id(n))).ToList();
        var duel = new Duel(kb, deck, opp, 5) { Log = { Enabled = false } };
        duel.Start();
        if (duel.CurrentPlayer != 0) duel.EndTurn();
        var cornu = duel.Players[0].Board.First(c => kb.CardName(c.Def) == "Cornu mythologique");
        var pris = duel.Players[0].Board.First(c => kb.CardName(c.Def) == "Prisonnière de la Machine");
        foreach (var a in cornu.Abilities) a.CurrentCooldown = 0;
        pris.Damage = pris.HpMax - 80;
        Assert.False(duel.Players[0].IsAi);
        var corne75 = duel.LegalActions().Where(o => o.Cast?.Card == cornu && o.Cast.Ability.Def.Id == "Corne 75").ToList();
        Assert.Contains(corne75, o => o.Cast!.Target == pris);
        // L'heuristique y voit un bon coup : remonter sa Prisonnière vaut mieux que la Corne 75 sur un adversaire en pleine forme.
        var score = (DuelAction o) =>
        {
            var c = duel.Clone(reseed: 3);
            c.Deciders[0] = new FastPlayer(1); c.Deciders[1] = new FastPlayer(2);
            c.Apply(c.Resolve(duel.RefOf(o)));
            return Evaluator.Score(c, 0);
        };
        Assert.True(score(corne75.First(o => o.Cast!.Target == pris)) > corne75.Where(o => o.Cast!.Target!.Owner == 1).Max(score));
        // Joueur marqué « IA du jeu » : alliées interdites.
        duel.Players[0].IsAi = true;
        Assert.DoesNotContain(duel.LegalActions(), o => o.Cast?.Card == cornu && o.Cast.Target?.Owner == 0);
    }

    [Fact]
    public void Les_anciennes_versions_restent_disponibles_pour_comparer()
    {
        foreach (var n in new[] { "heuristique-v1", "rapide-v1", "planificateur", "heuristique-combo:5" })
            Assert.NotNull(PlayerCatalog.Factory(n));
        Assert.Null(PlayerCatalog.Factory("heuristique-combo:abc"));
        Assert.Equal(0, HeuristicWeights.V1.ElementUse);
        Assert.True(HeuristicWeights.Default.ElementUse > 0);
    }

    [Fact]
    public void Le_planificateur_joue_des_parties_completes_sans_toucher_a_la_vraie_partie()
    {
        if (Kb.Value is not { } kb) return;
        var rng = new Rng(77);
        for (var g = 0; g < 6; g++)
        {
            var duel = new Duel(kb, Bench.RandomDeck(kb, rng), Bench.RandomDeck(kb, rng), rng.NextULong()) { Log = { Enabled = false } };
            duel.Start();
            var before = duel.StateKey();
            var planner = new TurnPlanner(rng.NextULong(), PlannerSettings.Default with { MaxDepth = 3 });
            planner.Choose(duel, duel.LegalActions());
            Assert.Equal(before, duel.StateKey());
            var fresh = new Duel(kb, Bench.RandomDeck(kb, rng), Bench.RandomDeck(kb, rng), rng.NextULong()) { Log = { Enabled = false } };
            var r = DuelRunner.Play(fresh, new TurnPlanner(rng.NextULong(), PlannerSettings.Default with { MaxDepth = 3 }), new HeuristicPlayer(rng.NextULong()));
            Assert.True(r.Finished);
        }
    }
}
