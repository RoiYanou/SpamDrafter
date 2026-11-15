using SpamDrafter.Simulator.Data;
using SpamDrafter.Simulator.Engine;
using static SpamDrafter.Simulator.Tests.TestWorld;

namespace SpamDrafter.Simulator.Tests;

/// <summary>Phase 6, lot B : talents (attachement, déclencheurs, chance lissée, paliers, effets, statuts de talent).</summary>
public class LotBTests
{
    private static AbilityDef ResistTalent(TestWorld w, string id = "Peau", string trigger = "StartOfGame", double v = 10) =>
        w.Talent(id, trigger, "Self", [Act("AddCardResistance", v)]);

    // ------------------------------------------------------------------ attachement et début de partie

    [Fact]
    public void Talent_de_debut_de_partie_applique_au_premier_tour()
    {
        var w = new TestWorld();
        var t = ResistTalent(w);
        var a = w.Card("A", talents: [t]);
        var duel = w.Duel([(a, t)], [(a, (AbilityDef?)null)]);
        Assert.Equal(10, duel.Players[0].Board[0].Resistances["fire"]);
        Assert.Equal(0, duel.Players[1].Board[0].Resistances["fire"]);   // pas de talent choisi
        Assert.Single(duel.Players[0].Board[0].Talents);
    }

    [Fact]
    public void Talent_qui_n_appartient_pas_a_la_carte_est_refuse()
    {
        var w = new TestWorld();
        var t = ResistTalent(w);
        var a = w.Card("A");
        Assert.Throws<ArgumentException>(() => w.Duel([(a, t)], [(a, (AbilityDef?)null)]));
    }

    [Fact]
    public void Les_talents_de_debut_de_partie_se_resolvent_apres_le_revenu_du_premier_tour()
    {
        // 🔒 La file n'est résolue qu'à la fin du début de tour : le bonus de revenu ne compte pas au tour 1.
        var w = new TestWorld();
        var t = w.Talent("Rente", "StartOfGame", "Self", [Act("AddSpamtonGenerationCaster", 5)]);
        var a = w.Card("A", income: 10, talents: [t]);
        var duel = w.Duel([(a, t)], [(w.Card("B"), (AbilityDef?)null)]);
        Assert.Equal(1 + 10, duel.Players[0].Spamtons);
        duel.EndTurn(); duel.EndTurn();
        Assert.Equal((1 + 10) + (2 + 15), duel.Players[0].Spamtons);
    }

    [Fact]
    public void Talent_de_debut_de_tour_meme_gele()
    {
        var w = new TestWorld();
        var t = w.Talent("Paie", "StartOfTurn", "PlayerSelf", [Act("AddSpamton", 5)]);
        var a = w.Card("A", income: 0, talents: [t]);
        var duel = w.Duel([(a, t)], [(w.Card("B"), (AbilityDef?)null)]);
        Assert.Equal(1 + 5, duel.Players[0].Spamtons);
        duel.Players[0].Board[0].AddStatus(StatusKind.Frozen, 0, 3);
        duel.EndTurn(); duel.EndTurn();
        Assert.Equal(6 + 2 + 5, duel.Players[0].Spamtons);  // le talent ignore le gel
    }

    [Fact]
    public void Activations_limitees_du_talent()
    {
        var w = new TestWorld();
        var t = w.Talent("Unique", "StartOfTurn", "PlayerSelf", [Act("AddSpamton", 5)], activations: 1);
        var a = w.Card("A", income: 0, talents: [t]);
        var duel = w.Duel([(a, t)], [(w.Card("B"), (AbilityDef?)null)]);
        duel.EndTurn(); duel.EndTurn();
        Assert.Equal(6 + 2, duel.Players[0].Spamtons);
    }

    [Fact]
    public void Chance_de_25_pourcent_lissee()
    {
        // 25 % lissé à 0,5 : réussite → −37,5 ; échec → +12,5.
        var w = new TestWorld();
        var t = w.Talent("Focus", "StartOfTurn", "PlayerSelf", [Act("AddSpamton", 1)], chanceOfMiss: 75);
        var a = w.Card("A", income: 0, talents: [t]);
        var duel = w.Duel([(a, t)], [(w.Card("B", hp: 100000), (AbilityDef?)null)]);
        var state = duel.Players[0].Board[0].Talents[0];
        var procs = 0;
        var before = duel.Players[0].Spamtons - 1;           // revenu du round 1 = 1
        for (var r = 0; r < 18; r++) { duel.EndTurn(); duel.EndTurn(); }
        procs = duel.Players[0].Spamtons - before - Enumerable.Range(1, 19).Sum();
        Assert.InRange(procs, 3, 6);                          // 18 tours à environ 25 %, très régulier une fois lissé
        Assert.True(state.ChanceModifier % 12.5 == 0);
    }

    [Fact]
    public void Talent_non_lisse_garde_toujours_la_meme_chance()
    {
        var w = new TestWorld();
        var t = w.Talent("Brut", "StartOfTurn", "PlayerSelf", [Act("AddSpamton", 1)], chanceOfMiss: 75, smooth: false);
        var a = w.Card("A", income: 0, talents: [t]);
        var duel = w.Duel([(a, t)], [(w.Card("B"), (AbilityDef?)null)]);
        for (var r = 0; r < 5; r++) { duel.EndTurn(); duel.EndTurn(); }
        Assert.Equal(0, duel.Players[0].Board[0].Talents[0].ChanceModifier);
    }

    [Fact]
    public void Condition_tour_tire_au_sort_un_seul_declenchement_entre_1_et_20()
    {
        var w = new TestWorld();
        var t = w.Talent("Revitalisation", "StartOfTurn", "PlayerSelf", [Act("AddSpamton", 1000)],
            triggerConditions: ["ConditionDrawnTurn1-20"]);
        var a = w.Card("A", income: 0, hp: 100000, talents: [t]);
        var duel = w.Duel([(a, t)], [(w.Card("B", hp: 100000), (AbilityDef?)null)]);
        var drawn = duel.Players[0].Board[0].Talents[0].DrawnTurn;
        Assert.InRange(drawn, 1, 20);
        while (duel.Round < 20) duel.EndTurn();
        Assert.Equal(1000 + Enumerable.Range(1, 20).Sum(), duel.Players[0].Spamtons);
    }

    // ------------------------------------------------------------------ déclencheurs d'événements

    [Fact]
    public void Degats_subis_bonus_sur_la_derniere_carte_qui_a_lance()
    {
        var w = new TestWorld();
        var t = w.Talent("SacDeCrit", "OnDamageTaken", "LastPlayed", [Act("AddCardCriticChance", 3, element: null)],
            conditions: ["is_enemy"]);
        // 🔒 « Sac de crit » : la condition porte sur la dernière carte qui a lancé (un ennemi),
        // mais l'effet « carte » (targetToModify = joueur) donne le bonus à la carte qui porte le talent.
        var victim = w.Card("Victime", talents: [t], abilities: w.Strike("Riposte"));
        var attacker = w.Card("Attaquant", abilities: w.Strike());
        var duel = w.Duel([(attacker, (AbilityDef?)null)], [(victim, t)]);
        var me = duel.Players[0].Board[0];
        duel.Cast(me, me.Abilities[0], duel.Players[1].Board[0]);
        Assert.Equal(3, duel.Players[1].Board[0].Abilities[0].BonusCrit);
        Assert.Equal(0, me.Abilities[0].BonusCrit);
    }

    [Fact]
    public void Degats_infliges_vol_de_Spamtons()
    {
        var w = new TestWorld();
        var t = w.Talent("Racket", "OnDamageDone", "PlayerOpponent", [Act("StealSpamtonTaken", 5)]);
        var me = w.Card("Moi", talents: [t], abilities: w.Strike());
        var duel = w.Duel([(me, t)], [(w.Card("Lui", income: 0), (AbilityDef?)null)]);
        duel.EndTurn();                                       // J2 touche 1 Spamton
        duel.EndTurn();
        var c = duel.Players[0].Board[0];
        var mine = duel.Players[0].Spamtons;
        duel.Cast(c, c.Abilities[0], duel.Players[1].Board[0]);
        Assert.Equal(0, duel.Players[1].Spamtons);            // au plus ce que la cible possède
        Assert.Equal(mine + 1, duel.Players[0].Spamtons);
    }

    [Fact]
    public void A_ma_mort_et_a_la_mort_d_une_autre_carte()
    {
        var w = new TestWorld();
        var heritage = w.Talent("Heritage", "OnDeath", "PlayerSelf", [Act("AddSpamton", 50)]);
        var vitalite = w.Talent("Vitalite", "OnDeathOther", "Self", [Act("AddCardHPMax", 200, element: null)]);
        var weak = w.Card("Faible", hp: 100, income: 0, talents: [heritage]);
        var ally = w.Card("Allie", income: 0, talents: [vitalite]);
        var me = w.Card("Moi", income: 0, abilities: w.Strike(value: 500));
        var duel = w.Duel([(me, (AbilityDef?)null)], [(weak, heritage), (ally, vitalite)]);
        var c = duel.Players[0].Board[0];
        duel.Cast(c, c.Abilities[0], duel.Players[1].Board[0]);
        Assert.Equal(50 + 50, duel.Players[1].Spamtons);      // 50 Spamtons de mort + talent Héritage
        Assert.Equal(1200, duel.Players[1].Board[0].HpMax);   // Vitalité
    }

    [Fact]
    public void Ennemi_tue_soin_complet()
    {
        var w = new TestWorld();
        var t = w.Talent("ResetUltime", "OnKill", "Self", [Act("SetHPInPercent", 100)]);
        var me = w.Card("Moi", talents: [t], abilities: w.Strike(value: 500));
        var duel = w.Duel([(me, t)], [(w.Card("A", hp: 100), (AbilityDef?)null), (w.Card("B"), (AbilityDef?)null)]);
        var c = duel.Players[0].Board[0];
        c.Damage = 600;
        duel.Cast(c, c.Abilities[0], duel.Players[1].Board[0]);
        Assert.Equal(1000, c.Hp);
    }

    [Fact]
    public void Soin_recu_declenche_le_talent()
    {
        var w = new TestWorld();
        var t = w.Talent("Convalescence", "OnHealTaken", "Self", [Act("AddCardCriticChance", 3, element: null)]);
        var me = w.Card("Moi", talents: [t], abilities: w.Ability("Soin", "Self", [Act("healTarget", 10)]));
        var duel = w.Duel([(me, t)], [(w.Card("Lui"), (AbilityDef?)null)]);
        var c = duel.Players[0].Board[0];
        c.Damage = 100;
        duel.Cast(c, c.Abilities[0]);
        Assert.Equal(3, c.Abilities[0].BonusCrit);
    }

    [Fact]
    public void Changement_de_palier_ligne_sous_50_pourcent_puis_annulee()
    {
        var w = new TestWorld();
        var t = w.Talent("Resilience", "OnHPPalierChange", "Self", [Line("AddCardResistance", 25, "fire", 50)]);
        var me = w.Card("Moi", talents: [t]);
        var hitter = w.Card("Frappeur", abilities: [w.Strike("Gros", 600), w.Ability("Soin", "SelectTarget", [Act("healTarget", 600)])]);
        var duel = w.Duel([(hitter, (AbilityDef?)null)], [(me, t)]);
        var h = duel.Players[0].Board[0];
        var target = duel.Players[1].Board[0];
        duel.Cast(h, h.Abilities[0], target);                 // 1 000 → 400 PV : sous 50 %
        Assert.Equal(25, target.Resistances["fire"]);
        duel.Cast(h, h.Abilities[1], target);                 // soin → 1 000 PV : au-dessus de 50 %
        Assert.Equal(0, target.Resistances["fire"]);
    }

    [Fact]
    public void Spamtons_modifies_avec_condition_une_seule_fois()
    {
        // 🔒 Une condition de déclenchement est aussi vérifiée « comme condition de cible » :
        // « 13ᵉ mois » attend donc vraiment 100 Spamtons.
        var w = new TestWorld();
        var t = w.Talent("TreiziemeMois", "OnSpamtonChange", "Self", [Act("AddSpamton", 50)], activations: 1,
            triggerConditions: ["HasSpamton100"]);
        var me = w.Card("Moi", income: 60, talents: [t]);
        var duel = w.Duel([(me, t)], [(w.Card("Lui"), (AbilityDef?)null)]);
        Assert.Equal(61, duel.Players[0].Spamtons);
        duel.EndTurn(); duel.EndTurn();                       // 61 + 62 = 123 ≥ 100 → +50
        Assert.Equal(61 + 62 + 50, duel.Players[0].Spamtons);
        duel.EndTurn(); duel.EndTurn();
        Assert.Equal(61 + 62 + 50 + 63, duel.Players[0].Spamtons);   // une seule activation
    }

    [Fact]
    public void Condition_de_declenchement_PV_du_lanceur_verifiee()
    {
        var w = new TestWorld();
        var contre = w.Talent("Contre", "OnDamageTaken", "LastPlayed", [Act("ApplyRevanche", 0)],
            triggerConditions: ["isCasterHPBelow25Percent"]);
        var victim = w.Card("Victime", talents: [contre], abilities: w.Strike("Riposte", 70, cooldown: 5));
        var attacker = w.Card("Attaquant", abilities: w.Strike("Petit", 100));
        var duel = w.Duel([(attacker, (AbilityDef?)null)], [(victim, contre)]);
        var a = duel.Players[0].Board[0];
        duel.Cast(a, a.Abilities[0], duel.Players[1].Board[0]);   // 900 PV sur 1 000 : pas de Revanche
        Assert.Equal(1000, a.Hp);
    }

    [Fact]
    public void Echo_reussi_declenche_le_talent()
    {
        var w = new TestWorld();
        var t = w.Talent("EchoTalent", "OnEcho", "Self", [Act("AddCardHPMax", 10, element: null)]);
        var me = w.Card("Moi", talents: [t], abilities: w.Ability("Retour", "SelectTarget", [Act("damage", 1)], echo: 100));
        var duel = w.Duel([(me, t)], [(w.Card("Lui"), (AbilityDef?)null)]);
        var c = duel.Players[0].Board[0];
        duel.Cast(c, c.Abilities[0], duel.Players[1].Board[0]);
        Assert.Equal(1010, c.HpMax);
    }

    // ------------------------------------------------------------------ effets propres aux talents

    [Fact]
    public void Revanche_la_carte_applique_ses_capacites_sur_l_attaquant()
    {
        var w = new TestWorld();
        var contre = w.Talent("Contre", "OnDamageTaken", "LastPlayed", [Act("ApplyRevanche", 0)],
            triggerConditions: ["isCasterHPBelow25Percent"]);
        var victim = w.Card("Victime", talents: [contre], abilities: w.Strike("Riposte", 70, cooldown: 5));
        var attacker = w.Card("Attaquant", abilities: w.Strike("Gros", 800));
        var duel = w.Duel([(attacker, (AbilityDef?)null)], [(victim, contre)]);
        var a = duel.Players[0].Board[0];
        duel.Cast(a, a.Abilities[0], duel.Players[1].Board[0]);
        Assert.Equal(930, a.Hp);                              // riposte sans attendre la recharge
        Assert.True(duel.Players[1].Board[0].Abilities[0].CurrentCooldown > 0);  // recharge non touchée
    }

    [Fact]
    public void Remplacement_de_la_derniere_capacite()
    {
        var w = new TestWorld();
        w.Ability("Souffle", "SelectTarget", [Act("damage", 10)]);
        var t = w.Talent("Souffleur", "StartOfGame", "Self", [Act("ReplaceLastByBreath", 0)]);
        var me = w.Card("Moi", talents: [t], abilities: [w.Strike("A1"), w.Strike("A2"), w.Strike("A3")]);
        var duel = w.Duel([(me, t)], [(w.Card("Lui"), (AbilityDef?)null)]);
        Assert.Equal(["A1", "A2", "Souffle"], duel.Players[0].Board[0].Abilities.Select(a => a.Def.Id));
    }

    [Fact]
    public void PV_multiplies_par_deux()
    {
        var w = new TestWorld();
        var t = w.Talent("Double", "StartOfGame", "Self", [Act("MultiplyAllHP_x2", 2)]);
        var me = w.Card("Moi", hp: 500, talents: [t]);
        var duel = w.Duel([(me, t)], [(w.Card("Lui"), (AbilityDef?)null)]);
        Assert.Equal(1000, duel.Players[0].Board[0].HpMax);
        Assert.Equal(1000, duel.Players[0].Board[0].Hp);
    }

    [Fact]
    public void Chance_et_Spamtons_fixes()
    {
        var w = new TestWorld();
        var t = w.Talent("Pari", "StartOfGame", "PlayerSelf", [Act("AddLuck", 3), Act("SetSpamton", 7)]);
        var me = w.Card("Moi", talents: [t]);
        var duel = w.Duel([(me, t)], [(w.Card("Lui"), (AbilityDef?)null)]);
        Assert.Equal(1 + 3, duel.Players[0].Luck);
        Assert.Equal(7, duel.Players[0].Spamtons);            // fixé après le revenu du premier tour
    }

    // ------------------------------------------------------------------ statuts de talent

    [Fact]
    public void Statut_Resonance_ajoute_de_l_Echo_a_toutes_les_capacites()
    {
        var w = new TestWorld();
        var t = w.Talent("Res", "StartOfGame", "Self", [Act("AddSResonance", 0)]);
        var me = w.Card("Moi", talents: [t], abilities: w.Strike());
        var duel = w.Duel([(me, t)], [(w.Card("Lui", hp: 100000), (AbilityDef?)null)]);
        var c = duel.Players[0].Board[0];
        Assert.Equal(5, c.TalentStatusValue("Resonance"));
        duel.Cast(c, c.Abilities[0], duel.Players[1].Board[0]);
        Assert.True(c.Abilities[0].EchoModifier is 2.5 or -47.5);   // un jet d'Écho à 5 % a eu lieu
    }

    [Fact]
    public void Statut_Chance_vitale_change_les_degats_en_soin()
    {
        var w = new TestWorld();
        var (duel, me, foe) = (w.Duel([w.Card("Moi", abilities: w.Strike())], [w.Card("Lui")]), (CardState?)null, (CardState?)null);
        me = duel.Players[0].Board[0];
        foe = duel.Players[1].Board[0];
        foe.TalentStatuses.Add(new TalentStatus("ChanceVitale", 100));
        foe.Damage = 300;
        duel.Cast(me, me.Abilities[0], foe);
        Assert.Equal(800, foe.Hp);
    }

    [Fact]
    public void Statut_Paradoxe_inverse_la_resistance()
    {
        var w = new TestWorld();
        var duel = w.Duel([w.Card("Moi", abilities: w.Strike())], [w.Card("Lui", resist: 50)]);
        var foe = duel.Players[1].Board[0];
        foe.TalentStatuses.Add(new TalentStatus("Paradoxe", 0));
        duel.Cast(duel.Players[0].Board[0], duel.Players[0].Board[0].Abilities[0], foe);
        Assert.Equal(850, foe.Hp);
    }

    [Fact]
    public void Statuts_Coeur_d_acier_et_Attaque_multicolore()
    {
        var w = new TestWorld();
        var duel = w.Duel([w.Card("Moi", abilities: w.Strike())], [w.Card("Lui")]);
        var me = duel.Players[0].Board[0];
        var foe = duel.Players[1].Board[0];
        me.TalentStatuses.Add(new TalentStatus("AttackMultiColor", 1));
        foe.TalentStatuses.Add(new TalentStatus("CoeurAcier", 1));
        duel.Cast(me, me.Abilities[0], foe);
        Assert.All(duel.Players[0].ElementLevels.Values, v => Assert.Equal(1, v));
        Assert.All(foe.Resistances.Values, v => Assert.Equal(1, v));
    }
}
