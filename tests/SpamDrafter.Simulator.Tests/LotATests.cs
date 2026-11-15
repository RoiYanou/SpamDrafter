using SpamDrafter.Simulator.Data;
using SpamDrafter.Simulator.Engine;
using static SpamDrafter.Simulator.Tests.TestWorld;

namespace SpamDrafter.Simulator.Tests;

/// <summary>
/// Phase 6, lot A : enchaînements, choix multiples, dernière cible, Écho, statistiques par capacité,
/// effets et conditions des capacités actives. J1 commence ; les cartes d'essai ont 1 000 PV.
/// </summary>
public class LotATests
{
    /// <summary>Décideur d'essai qui prend toujours l'option ou la cible demandée.</summary>
    private sealed class Pick(int option = 0, Func<IReadOnlyList<CardState>, CardState>? target = null) : IDuelDecider
    {
        public CardState ChooseTarget(Duel d, CardState c, AbilityDef a, IReadOnlyList<CardState> t) => target?.Invoke(t) ?? t[0];
        public int ChooseOption(Duel d, CardState c, AbilityDef a, IReadOnlyList<AbilityDef> o) => option;
    }

    private static (Duel duel, CardState me, CardState foe) Setup(TestWorld w, AbilityDef ability, int foeHp = 1000,
        int foeResist = 0, AbilityDef? foeAbility = null)
    {
        var me = w.Card("Moi", abilities: ability);
        var foe = foeAbility is null ? w.Card("Lui", hp: foeHp, resist: foeResist)
                                     : w.Card("Lui", hp: foeHp, resist: foeResist, abilities: foeAbility);
        var duel = w.Duel([me], [foe]);
        return (duel, duel.Players[0].Board[0], duel.Players[1].Board[0]);
    }

    // ------------------------------------------------------------------ enchaînements et choix

    [Fact]
    public void Enchainement_resolu_apres_la_capacite_principale_et_recharge_appliquee_a_la_fin()
    {
        var w = new TestWorld();
        w.Ability("EchoDouble_1", "SelectTarget", [Act("damage", 50)], conditions: ["is_not_empty", "is_enemy"]);
        var main = w.Ability("Double", "PlayerSelf", [Act("AddAllPlayerAttack", 1)], cooldown: 1, chain: ["EchoDouble_1"]);
        var (duel, me, foe) = Setup(w, main);
        duel.Cast(me, me.Abilities[0]);
        Assert.Equal(1, duel.Players[0].Level("fire"));
        Assert.Equal(1000 - 55, foe.Hp);                    // l'enchaînement profite du niveau gagné juste avant
        Assert.Equal(1, me.Abilities[0].CurrentCooldown);
    }

    [Fact]
    public void Choix_multiple_seule_l_option_choisie_est_resolue()
    {
        var w = new TestWorld();
        w.Ability("EchoChoix_1", "PlayerSelf", [Act("AddAllPlayerAttack", 3)]);
        w.Ability("EchoChoix_2", "SelectTarget", [Act("damage", 100)], conditions: ["is_not_empty", "is_enemy"]);
        var choice = w.Ability("Choix", "ChoiceSelector", [Act("_NULL", 0, element: null)], cooldown: 1,
            chain: ["EchoChoix_1", "EchoChoix_2"]);
        var (duel, me, foe) = Setup(w, choice);
        duel.Deciders[0] = new Pick(option: 1);
        duel.Cast(me, me.Abilities[0]);
        Assert.Equal(900, foe.Hp);
        Assert.Equal(0, duel.Players[0].Level("fire"));
        Assert.Equal(1, me.Abilities[0].CurrentCooldown);   // recharge de la capacité à choix
    }

    [Fact]
    public void Choix_multiple_sans_option_possible_n_est_pas_lancable()
    {
        var w = new TestWorld();
        w.Ability("EchoChoix_1", "SelectTarget", [Act("damage", 100)], conditions: ["is_not_empty", "is_enemy"]);
        var choice = w.Ability("Choix", "ChoiceSelector", [Act("_NULL", 0, element: null)], chain: ["EchoChoix_1"]);
        var (duel, me, foe) = Setup(w, choice);
        foe.AddStatus(StatusKind.Stealth, 0, 0);
        Assert.False(duel.CanCast(me, me.Abilities[0]));
    }

    [Fact]
    public void Une_capacite_Echo_X_partage_les_statistiques_de_X()
    {
        var w = new TestWorld();
        w.Ability("EchoFrappe_1", "SelectTarget", [Act("damage", 100)], conditions: ["is_not_empty", "is_enemy"]);
        var main = w.Ability("Frappe", "PlayerSelf", [Act("_NULL", 0, element: null)], chain: ["EchoFrappe_1"]);
        var (duel, me, foe) = Setup(w, main);
        me.Abilities[0].BaseDamageBonus = 20;               // bonus posé sur « Frappe »
        duel.Cast(me, me.Abilities[0]);
        Assert.Equal(1000 - 120, foe.Hp);
    }

    [Fact]
    public void Derniere_cible_vise_la_derniere_carte_touchee()
    {
        var w = new TestWorld();
        w.Ability("EchoSuite_1", "LastTargeted", [Act("damage", 30)], conditions: ["is_not_empty"]);
        var main = w.Ability("Suite", "SelectTarget", [Act("damage", 100)], chain: ["EchoSuite_1"],
            conditions: ["is_not_empty", "is_enemy"]);
        var (duel, me, foe) = Setup(w, main);
        duel.Cast(me, me.Abilities[0], foe);
        Assert.Equal(1000 - 130, foe.Hp);
    }

    // ------------------------------------------------------------------ Écho et hasard lissé (§8.6)

    [Fact]
    public void Echo_a_100_pourcent_recharge_a_zero_et_pas_d_activation_consommee()
    {
        var w = new TestWorld();
        var ab = w.Ability("Retour100", "SelectTarget", [Act("damage", 10)], cooldown: 3, echo: 100, activations: 2);
        var (duel, me, foe) = Setup(w, ab);
        duel.EndTurn(); duel.EndTurn(); duel.EndTurn(); duel.EndTurn();
        duel.Cast(me, me.Abilities[0], foe);
        Assert.Equal(0, me.Abilities[0].CurrentCooldown);
        Assert.Equal(2, me.Abilities[0].ActivationsLeft);
        Assert.True(duel.CanCast(me, me.Abilities[0]));     // relançable dans le même tour
    }

    [Fact]
    public void Echo_lisse_la_chance_apres_chaque_jet()
    {
        // Écho 50 %, lissage 0,5 : échec → +25 ; réussite → −25 (chance suivante 75 % ou 25 %).
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var w = new TestWorld();
            var ab = w.Ability("Retour50", "SelectTarget", [Act("damage", 10)], cooldown: 2, echo: 50);
            var me = w.Card("Moi", hp: 100000, abilities: ab);
            var duel = w.Duel([me], [w.Card("Lui", hp: 100000)], seed);
            duel.EndTurn(); duel.EndTurn();
            var c = duel.Players[0].Board[0];
            duel.Cast(c, c.Abilities[0], duel.Players[1].Board[0]);
            var rt = c.Abilities[0];
            Assert.True(rt.EchoModifier is 25 or -25, duel.Log.ToString());
            Assert.True((rt.EchoModifier < 0 ? 0 : 2) == rt.CurrentCooldown, duel.Log.ToString() + $" mod={rt.EchoModifier} cd={rt.CurrentCooldown}");
        }
    }

    // ------------------------------------------------------------------ statistiques

    [Fact]
    public void Bonus_de_critique_de_la_carte()
    {
        var w = new TestWorld();
        w.Ability("EchoBoost_1", "SelectTarget", [Act("damage", 100)], conditions: ["is_not_empty", "is_enemy"]);
        var main = w.Ability("Boost", "Self", [Act("AddCardCriticChance", 100, element: null)], chain: ["EchoBoost_1"]);
        var (duel, me, foe) = Setup(w, main);
        duel.Cast(me, me.Abilities[0]);
        Assert.Equal(100, me.Abilities[0].BonusCrit);       // borné à 100 − 0
        Assert.Equal(800, foe.Hp);                           // critique × 2
    }

    [Fact]
    public void Bonus_de_degats_de_base_sur_la_capacite_lancee()
    {
        var w = new TestWorld();
        var ab = w.Ability("Charge", "SelectTarget", [Act("damage", 100), Act("AddAbilityBaseDamage", 50)],
            conditions: ["is_not_empty", "is_enemy"]);
        var (duel, me, foe) = Setup(w, ab);
        duel.Cast(me, me.Abilities[0], foe);
        Assert.Equal(900, foe.Hp);                           // le bonus vient après les dégâts
        Assert.Equal(50, me.Abilities[0].BaseDamageBonus);
    }

    [Fact]
    public void Bonus_par_element_seules_les_capacites_du_meme_element()
    {
        var w = new TestWorld();
        var boost = w.Ability("Feu+", "PlayerSelf", [Act("AddAllPlayerCritick", 10)]);   // élément de l'action : feu
        var water = w.Ability("Eau", "SelectTarget", [Act("damage", 10, element: "water")], element: "water");
        var me = w.Card("Moi", abilities: [boost, water]);
        var duel = w.Duel([me], [w.Card("Lui")]);
        var c = duel.Players[0].Board[0];
        duel.Cast(c, c.Abilities[0]);
        Assert.Equal(10, c.Abilities[0].BonusCrit);
        Assert.Equal(0, c.Abilities[1].BonusCrit);
    }

    [Fact]
    public void Rate_ajoute_a_l_adversaire_borne_a_99()
    {
        var w = new TestWorld();
        var ab = w.Ability("Brouillard", "SelectTarget", [Act("AddOppCardMiss", 500, element: null)],
            conditions: ["is_not_empty", "is_enemy"]);
        var (duel, me, foe) = Setup(w, ab, foeAbility: w.Strike());
        duel.Cast(me, me.Abilities[0], foe);
        Assert.Equal(99, foe.Abilities[0].BonusMiss);
    }

    [Fact]
    public void Resistance_bornee_entre_moins_50_et_50()
    {
        var w = new TestWorld();
        var ab = w.Ability("Faiblesse", "SelectTarget", [Act("AddOppCardResistance", -80)],
            conditions: ["is_not_empty", "is_enemy"]);
        var (duel, me, foe) = Setup(w, ab, foeResist: 10);
        duel.Cast(me, me.Abilities[0], foe);
        Assert.Equal(-50, foe.Resistances["fire"]);
        Assert.Equal(10, foe.Resistances["water"]);
    }

    [Fact]
    public void PV_max_de_la_carte_augmentent_de_10_pourcent_par_niveau()
    {
        var w = new TestWorld();
        var ab = w.Ability("Robuste", "Self", [Act("AddCardHPMax", 100)]);
        var (duel, me, _) = Setup(w, ab);
        duel.Players[0].ElementLevels["fire"] = 5;
        me.Damage = 100;
        duel.Cast(me, me.Abilities[0]);
        Assert.Equal(1150, me.HpMax);
        Assert.Equal(1050, me.Hp);                           // les dégâts subis restent
    }

    [Fact]
    public void Vol_de_PV_max_transfere_au_lanceur()
    {
        var w = new TestWorld();
        var ab = w.Ability("Vol", "SelectTarget", [Act("StealOppHPMax", -100, element: null)],
            conditions: ["is_not_empty", "is_enemy"]);
        var (duel, me, foe) = Setup(w, ab);
        duel.Cast(me, me.Abilities[0], foe);
        Assert.Equal(900, foe.HpMax);
        Assert.Equal(1100, me.HpMax);
    }

    [Fact]
    public void Gain_par_nombre_de_lancers()
    {
        var w = new TestWorld();
        var ab = w.Ability("Montee", "PlayerSelf", [Act("AddAllPlayerAttackCountActivation1", 1)]);
        var (duel, me, _) = Setup(w, ab);
        duel.Cast(me, me.Abilities[0]);                      // 1 + 1 × 0 lancer
        duel.EndTurn(); duel.EndTurn();
        duel.Cast(me, me.Abilities[0]);                      // 1 + 1 × 1 lancer
        Assert.Equal(3, duel.Players[0].Level("fire"));
    }

    [Fact]
    public void Recharge_qui_baisse_avec_les_cartes_disparues()
    {
        var w = new TestWorld();
        var slow = w.Ability("Lente", "SelectTarget", [Act("damage", 10)], cooldown: 5, dropsPerCardGone: true);
        var kill = w.Ability("Tuer", "SelectTarget", [Act("damage", 500)], conditions: ["is_not_empty", "is_enemy"]);
        var me = w.Card("Moi", abilities: [slow, kill]);
        var duel = w.Duel([me], [w.Card("A", hp: 100), w.Card("B")]);
        var c = duel.Players[0].Board[0];
        Assert.Equal(4, c.Abilities[0].CurrentCooldown);
        duel.Cast(c, c.Abilities[1], duel.Players[1].Board[0]);
        Assert.Equal(4, c.Abilities[0].MaxCooldown);         // 5 − 1 carte disparue
        Assert.Equal(3, c.Abilities[0].CurrentCooldown);
    }

    // ------------------------------------------------------------------ autres effets

    [Fact]
    public void Fixer_les_PV_a_un_pourcentage()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Ability("Corne", "SelectTarget", [Act("SetHPInPercent", 25)]));
        duel.Cast(me, me.Abilities[0], foe);
        Assert.Equal(250, foe.Hp);
    }

    [Fact]
    public void Soin_en_pourcentage_des_PV_max_du_lanceur()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Ability("Soin%", "SelectTarget", [Act("healPVmaxTarget", 10)]));
        foe.Damage = 500;
        duel.Cast(me, me.Abilities[0], foe);
        Assert.Equal(600, foe.Hp);
    }

    [Fact]
    public void Avalement_le_lanceur_gagne_les_PV_max_puis_la_cible_est_detruite()
    {
        var w = new TestWorld();
        var ab = w.Ability("Avale", "SelectTarget", [Act("HealCasterWithOppHPMAX", 0), Act("destroy", 0)],
            conditions: ["is_not_empty", "is_enemy"]);
        var me = w.Card("Moi", abilities: ab);
        var duel = w.Duel([me], [w.Card("A", hp: 400), w.Card("B")]);
        var c = duel.Players[0].Board[0];
        var victim = duel.Players[1].Board[0];
        duel.Cast(c, c.Abilities[0], victim);
        Assert.Equal(1400, c.HpMax);
        Assert.Equal(1000, c.Hp);
        Assert.Contains(victim, duel.Players[1].Discard);
    }

    [Fact]
    public void Spamtons_revenu_et_remise_a_zero()
    {
        var w = new TestWorld();
        var gen = w.Ability("Gen", "Self", [Act("AddSpamtonGenerationCaster", 4)]);
        var reset = w.Ability("Jugement", "PlayerOpponent", [Act("ResetSpamton", 0)], conditions: ["is_not_empty"]);
        var me = w.Card("Moi", income: 10, abilities: [gen, reset]);
        var duel = w.Duel([me], [w.Card("Lui")]);
        var c = duel.Players[0].Board[0];
        duel.Cast(c, c.Abilities[0]);
        Assert.Equal(14, c.Income);
        duel.EndTurn();                                      // J2 touche son revenu
        Assert.True(duel.Players[1].Spamtons > 0);
        duel.EndTurn();
        duel.Cast(c, c.Abilities[1]);
        Assert.Equal(0, duel.Players[1].Spamtons);
        Assert.Equal((1 + 10) + (2 + 14), duel.Players[0].Spamtons);
    }

    [Fact]
    public void Degats_plus_la_moitie_des_PV_manquants()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Ability("Rage", "SelectTarget", [Act("DamageCasterHP", 100)],
            conditions: ["is_not_empty", "is_enemy"]));
        me.Damage = 400;
        duel.Cast(me, me.Abilities[0], foe);
        Assert.Equal(1000 - 300, foe.Hp);
    }

    [Fact]
    public void Degats_avec_Spamtons_du_lanceur_sans_element()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Ability("Jackpot", "SelectTarget", [Act("DamageCasterSpamton", 0)],
            conditions: ["is_not_empty", "is_enemy"]), foeResist: 50);
        duel.Players[0].ElementLevels["fire"] = 10;
        duel.Cast(me, me.Abilities[0], foe);
        Assert.Equal(1000 - 11, foe.Hp);                     // 11 Spamtons, ni niveau ni résistance
    }

    [Fact]
    public void Degats_de_l_element_de_la_carte()
    {
        var w = new TestWorld();
        var ab = w.Ability("Nature", "SelectTarget", [Act("DamageWithCardElement", 100)], conditions: ["is_not_empty", "is_enemy"]);
        var me = w.Card("Moi", element: "nature", abilities: ab);
        var duel = w.Duel([me], [w.Card("Lui")]);
        duel.Players[0].ElementLevels["nature"] = 2;
        duel.Cast(duel.Players[0].Board[0], duel.Players[0].Board[0].Abilities[0], duel.Players[1].Board[0]);
        Assert.Equal(880, duel.Players[1].Board[0].Hp);
    }

    [Fact]
    public void Degats_en_pourcentage_des_PV_max_sans_critique()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Ability("Pourcent", "SelectTarget", [Act("DamageOpponentHPMAX", 10)],
            crit: 100, conditions: ["is_not_empty", "is_enemy"]), foeHp: 1500);
        duel.Cast(me, me.Abilities[0], foe);
        Assert.Equal(1350, foe.Hp);
    }

    [Fact]
    public void Degats_doubles_sur_une_carte_qui_provoque()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Ability("Brise", "SelectTarget", [Act("DamageOnShield2", 100)],
            conditions: ["is_not_empty", "is_enemy"]));
        foe.AddStatus(StatusKind.Taunt, 0, 0);
        duel.Cast(me, me.Abilities[0], foe);
        Assert.Equal(800, foe.Hp);
    }

    [Fact]
    public void Vol_de_PV_max_avec_les_degats()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Ability("Draine", "SelectTarget", [Act("StealMaxHPWithDamageTarget", 100)],
            conditions: ["is_not_empty", "is_enemy"]));
        duel.Cast(me, me.Abilities[0], foe);
        Assert.Equal(900, foe.Hp);
        Assert.Equal(1100, me.HpMax);
    }

    [Fact]
    public void Changement_d_element_vers_un_autre_element()
    {
        var w = new TestWorld();
        var (duel, me, _) = Setup(w, w.Ability("Mythique", "Self", [Act("ChangeElementTargetRandom", 1)]));
        duel.Cast(me, me.Abilities[0]);
        Assert.NotEqual("fire", me.Element);
        Assert.Contains(me.Element, w.Settings.RandomElements);
    }

    [Fact]
    public void Vol_d_element()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Ability("Vole", "SelectTarget", [Act("StealElement", 2)],
            conditions: ["is_not_empty", "is_enemy"]));
        duel.Players[1].ElementLevels["fire"] = 5;
        duel.Cast(me, me.Abilities[0], foe);
        Assert.Equal(3, duel.Players[1].Level("fire"));
        Assert.Equal(2, duel.Players[0].Level("fire"));
    }

    [Fact]
    public void Retirer_la_provocation()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Ability("Calme", "SelectTarget", [Act("RemoveTauntRunning", 0)],
            conditions: ["is_not_empty", "HasTaunt"]));
        Assert.False(duel.CanCast(me, me.Abilities[0]));     // condition : la cible doit provoquer
        foe.AddStatus(StatusKind.Taunt, 0, 0);
        duel.Cast(me, me.Abilities[0], foe);
        Assert.False(foe.Has(StatusKind.Taunt));
    }

    [Fact]
    public void Resurrection_de_la_derniere_carte_morte()
    {
        var w = new TestWorld();
        var kill = w.Ability("Sacrifice", "SelectTarget", [Act("damage", 500)], conditions: ["is_not_empty", "is_ally"]);
        var rez = w.Ability("Rez", "PlayerSelf", [Act("Resurection", 50)]);
        var me = w.Card("Moi", abilities: [kill, rez]);
        var duel = w.Duel([me, w.Card("Faible", hp: 400)], [w.Card("Lui")]);
        var c = duel.Players[0].Board[0];
        var weak = duel.Players[0].Board[1];
        duel.Cast(c, c.Abilities[0], weak);
        Assert.Contains(weak, duel.Players[0].Discard);
        duel.Cast(c, c.Abilities[1]);
        Assert.Contains(weak, duel.Players[0].Board);
        Assert.Equal(200, weak.Hp);
        Assert.Equal(3, weak.Slot);
    }

    [Fact]
    public void Remise_des_recharges_au_maximum()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Ability("Lenteur", "SelectTarget", [Act("ResetCooldownTarget", 0)],
            conditions: ["is_not_empty", "is_enemy"]), foeAbility: w.Strike(cooldown: 4));
        duel.Cast(me, me.Abilities[0], foe);
        Assert.Equal(4, foe.Abilities[0].CurrentCooldown);
    }

    // ------------------------------------------------------------------ conditions

    [Fact]
    public void Condition_PV_du_lanceur_sous_50_pourcent()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Ability("Desespoir", "SelectTarget",
            [Act("damage", 100, condition: "isCasterHPBelow50Percent")], conditions: ["is_not_empty", "is_enemy"]));
        duel.Cast(me, me.Abilities[0], foe);
        Assert.Equal(1000, foe.Hp);
        duel.EndTurn(); duel.EndTurn();
        me.Damage = 600;
        duel.Cast(me, me.Abilities[0], foe);
        Assert.Equal(900, foe.Hp);
    }

    [Fact]
    public void Condition_de_declenchement_numero_du_round()
    {
        var w = new TestWorld();
        var (duel, me, _) = Setup(w, w.Ability("Tour6", "PlayerSelf", [Act("AddAllPlayerAttack", 1)],
            triggerConditions: ["ConditionTurn=6"]));
        Assert.False(duel.CanCast(me, me.Abilities[0]));
        while (duel.Round < 6) duel.EndTurn();
        Assert.True(duel.CanCast(me, me.Abilities[0]));
    }

    [Fact]
    public void Condition_Spamtons_du_lanceur()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Ability("Riche", "SelectTarget", [Act("damage", 100, condition: "HasSpamton100")],
            conditions: ["is_not_empty", "is_enemy"]));
        duel.Cast(me, me.Abilities[0], foe);
        Assert.Equal(1000, foe.Hp);
        duel.Players[0].AddSpamtons(200);
        duel.EndTurn(); duel.EndTurn();
        duel.Cast(me, me.Abilities[0], foe);
        Assert.Equal(900, foe.Hp);
    }
}
