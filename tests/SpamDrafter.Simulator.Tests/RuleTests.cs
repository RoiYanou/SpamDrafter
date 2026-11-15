using SpamDrafter.Simulator.Engine;
using static SpamDrafter.Simulator.Tests.TestWorld;

namespace SpamDrafter.Simulator.Tests;

/// <summary>
/// Un test par règle de docs/game/REGLES_DU_DUEL.md (le paragraphe est rappelé dans le nom ou le commentaire).
/// Convention : J1 = Players[0] commence (réglage du monde d'essai).
/// </summary>
public class RuleTests
{
    // ------------------------------------------------------------------ §1 mise en place

    [Fact]
    public void S1_2_La_premiere_carte_du_deck_va_en_x4()
    {
        var w = new TestWorld();
        var a = w.Card("A"); var b = w.Card("B"); var c = w.Card("C"); var d = w.Card("D");
        var duel = w.Duel([a, b, c, d], [a]);
        Assert.Equal([4, 3, 2, 1], duel.Players[0].Board.Select(x => x.Slot));
    }

    [Fact]
    public void S1_4_Depart_round_1_chance_et_Spamtons()
    {
        var w = new TestWorld();
        var a = w.Card("A", income: 10); var b = w.Card("B", income: 5);
        var duel = w.Duel([a, b], [a]);
        Assert.Equal(1, duel.Round);
        Assert.Equal(0, duel.CurrentPlayer);
        Assert.Equal(1, duel.Players[0].Luck);             // départ 0, +1 au début du tour
        Assert.Equal(1 + 10 + 5, duel.Players[0].Spamtons); // revenu = n° du round + revenus des cartes
        Assert.Equal(0, duel.Players[1].Spamtons);          // le second joueur n'a pas encore joué
        Assert.All(duel.Players[0].ElementLevels.Values, v => Assert.Equal(0, v));
    }

    [Fact]
    public void S1_5_Recharge_de_depart_et_compensation_du_second_joueur()
    {
        var w = new TestWorld();
        var ab = w.Strike(cooldown: 3);
        var a = w.Card("A", abilities: ab);
        var duel = w.Duel([a], [a]);
        Assert.Equal(2, duel.Players[0].Board[0].Abilities[0].CurrentCooldown); // recharge − 1
        Assert.Equal(1, duel.Players[1].Board[0].Abilities[0].CurrentCooldown); // − 1 de plus
    }

    [Fact]
    public void S1_5_Recharge_3_disponible_au_3e_tour_du_premier_et_au_2e_du_second()
    {
        var w = new TestWorld();
        var a = w.Card("A", hp: 100000, abilities: w.Strike(cooldown: 3));
        int TurnsUntilReady(int player)
        {
            var duel = w.Duel([a], [a]);
            for (var turn = 1; turn < 10; turn++)
            {
                while (duel.CurrentPlayer != player) duel.EndTurn();
                if (duel.CanCast(duel.Active.Board[0], duel.Active.Board[0].Abilities[0])) return turn;
                duel.EndTurn();
            }
            return -1;
        }
        Assert.Equal(3, TurnsUntilReady(0));
        Assert.Equal(2, TurnsUntilReady(1));
    }

    // ------------------------------------------------------------------ §2 et §8.1 tours

    [Fact]
    public void S2_Le_round_augmente_quand_les_deux_joueurs_ont_joue()
    {
        var w = new TestWorld();
        var a = w.Card("A");
        var duel = w.Duel([a], [a]);
        duel.EndTurn();
        Assert.Equal(1, duel.Round);
        Assert.Equal(1, duel.CurrentPlayer);
        duel.EndTurn();
        Assert.Equal(2, duel.Round);
        Assert.Equal(0, duel.CurrentPlayer);
        Assert.Equal(2, duel.Players[0].Luck);
        Assert.Equal((1 + 10) + (2 + 10), duel.Players[0].Spamtons);
    }

    [Fact]
    public void S8_1_Recharge_remise_au_max_puis_moins_1_a_chaque_fin_de_tour_du_proprietaire()
    {
        var w = new TestWorld();
        var a = w.Card("A", hp: 100000, abilities: w.Strike(cooldown: 2));
        var duel = w.Duel([a], [a]);
        duel.EndTurn(); duel.EndTurn();                    // J1, 2ᵉ tour : recharge 2−1−1 = 0
        var me = duel.Players[0].Board[0];
        var ab = me.Abilities[0];
        Assert.Equal(0, ab.CurrentCooldown);
        duel.Cast(me, ab, duel.Players[1].Board[0]);
        Assert.Equal(2, ab.CurrentCooldown);               // remise au maximum
        Assert.False(duel.CanCast(me, ab));                // une fois par tour
        duel.EndTurn();
        Assert.Equal(1, ab.CurrentCooldown);               // −1 à la fin du tour de J1
        duel.EndTurn();
        Assert.Equal(1, ab.CurrentCooldown);               // pas de changement au tour adverse
    }

    // ------------------------------------------------------------------ §5 dégâts

    private static (Duel, CardState, CardState) Setup(TestWorld w, Data.AbilityDef ability, int foeResist = 0,
        int foeHp = 1000)
    {
        var me = w.Card("Moi", hp: 1000, abilities: ability);
        var foe = w.Card("Lui", hp: foeHp, resist: foeResist);
        var duel = w.Duel([me], [foe]);
        return (duel, duel.Players[0].Board[0], duel.Players[1].Board[0]);
    }

    [Fact]
    public void S5_1_Degats_de_base()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Strike(value: 100));
        duel.Cast(me, me.Abilities[0], foe);
        Assert.Equal(900, foe.Hp);
    }

    [Fact]
    public void S5_1_Plus_10_pourcent_par_niveau_d_element()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Strike(value: 100));
        duel.Players[0].ElementLevels["fire"] = 3;
        duel.Cast(me, me.Abilities[0], foe);
        Assert.Equal(1000 - 130, foe.Hp);
    }

    [Fact]
    public void S5_1_Un_niveau_negatif_compte_pour_zero()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Strike(value: 100));
        duel.Players[0].ElementLevels["fire"] = -4;
        duel.Cast(me, me.Abilities[0], foe);
        Assert.Equal(900, foe.Hp);
    }

    [Fact]
    public void S5_1_Critique_multiplie_les_degats()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Ability("Crit", "SelectTarget", [Act("damage", 100)], crit: 100, critRatio: 3));
        duel.Cast(me, me.Abilities[0], foe);
        Assert.Equal(700, foe.Hp);
    }

    [Theory]
    [InlineData(10, 910)]    // résistance 10 % → 90 dégâts
    [InlineData(-50, 850)]   // faiblesse 50 % → 150 dégâts
    [InlineData(50, 950)]
    public void S5_3_Resistance(int resist, int expectedHp)
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Strike(value: 100), foeResist: resist);
        duel.Cast(me, me.Abilities[0], foe);
        Assert.Equal(expectedHp, foe.Hp);
    }

    [Fact]
    public void S5_3_Bouclier_reduit_puis_disparait()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Strike(value: 100));
        foe.Toxes.Add(new ToxState(w.Tox("Shield", 50), false));   // 🔒 la réduction vient du Tox Bouclier
        foe.AddStatus(StatusKind.Shield, 50, 1);
        duel.Cast(me, me.Abilities[0], foe);
        Assert.Equal(950, foe.Hp);
        Assert.False(foe.Has(StatusKind.Shield));
    }

    [Fact]
    public void S5_3_Bouclier_100_annule_tout()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Strike(value: 100));
        foe.Toxes.Add(new ToxState(w.Tox("Shield", 100), false));
        foe.AddStatus(StatusKind.Shield, 100, 1);
        duel.Cast(me, me.Abilities[0], foe);
        Assert.Equal(1000, foe.Hp);
    }

    [Fact]
    public void S5_4_Le_soin_ne_depasse_jamais_les_PV_max()
    {
        var w = new TestWorld();
        var heal = w.Ability("Soin", "SelectTarget", [Act("healTarget", 300)]);
        var (duel, me, foe) = Setup(w, heal);
        me.Damage = 100;
        duel.Cast(me, me.Abilities[0], me);
        Assert.Equal(1000, me.Hp);
    }

    [Fact]
    public void S4_2_Rate_si_le_pourcentage_de_rate_est_atteint()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Ability("Rate", "SelectTarget", [Act("damage", 100)], miss: 100));
        duel.Cast(me, me.Abilities[0], foe);
        Assert.Equal(1000, foe.Hp);
    }

    [Fact]
    public void S4_2_Une_action_sans_jet_de_rate_touche_toujours()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Ability("Sur", "SelectTarget", [Act("damage", 100, useMiss: false)], miss: 100));
        duel.Cast(me, me.Abilities[0], foe);
        Assert.Equal(900, foe.Hp);
    }

    [Fact]
    public void S4_2_Condition_d_action_was_critick()
    {
        var w = new TestWorld();
        var ab = w.Ability("Suite", "SelectTarget",
            [Act("damage", 100), Act("damage", 50, condition: "was_critick")], crit: 0);
        var (duel, me, foe) = Setup(w, ab);
        duel.Cast(me, me.Abilities[0], foe);
        Assert.Equal(900, foe.Hp);                         // pas de critique → 2ᵉ action ignorée
    }

    // ------------------------------------------------------------------ §3.1 lancer

    [Theory]
    [InlineData(StatusKind.Frozen)]
    [InlineData(StatusKind.Silenced)]
    [InlineData(StatusKind.Paralysed)]
    public void S3_1_Gel_silence_et_paralysie_empechent_de_lancer(StatusKind kind)
    {
        var w = new TestWorld();
        var (duel, me, _) = Setup(w, w.Strike());
        me.AddStatus(kind, 0, 2);
        Assert.False(duel.CanCast(me, me.Abilities[0]));
        Assert.Empty(duel.LegalCasts());
    }

    [Fact]
    public void S3_1_Cout_en_niveaux_d_element()
    {
        var w = new TestWorld();
        var ab = w.Ability("Cher", "SelectTarget", [Act("damage", 100)], costs: [new("CostElementFire", 2)]);
        var (duel, me, foe) = Setup(w, ab);
        Assert.False(duel.CanCast(me, me.Abilities[0]));   // niveau 0
        duel.Players[0].ElementLevels["fire"] = 2;
        Assert.True(duel.CanCast(me, me.Abilities[0]));
        duel.Cast(me, me.Abilities[0], foe);
        Assert.Equal(0, duel.Players[0].Level("fire"));     // coût payé après la résolution
        Assert.Equal(1000 - 120, foe.Hp);                   // les dégâts ont utilisé le niveau 2
    }

    [Fact]
    public void S3_1_Activations_limitees()
    {
        var w = new TestWorld();
        var ab = w.Ability("Unique", "SelectTarget", [Act("damage", 10)], cooldown: 1, activations: 1);
        var (duel, me, foe) = Setup(w, ab);
        duel.Cast(me, me.Abilities[0], foe);
        duel.EndTurn(); duel.EndTurn();
        Assert.Equal(0, me.Abilities[0].CurrentCooldown);
        Assert.False(duel.CanCast(me, me.Abilities[0]));
    }

    // ------------------------------------------------------------------ §3.2 ciblage

    [Fact]
    public void S3_2_Furtivite_empeche_le_ciblage_et_se_perd_en_lancant()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Strike());
        foe.AddStatus(StatusKind.Stealth, 0, 0);
        Assert.DoesNotContain(duel.LegalCasts(), o => o.Target == foe);
        me.AddStatus(StatusKind.Stealth, 0, 0);
        foe.Remove(StatusKind.Stealth);
        duel.Cast(me, me.Abilities[0], foe);
        Assert.False(me.Has(StatusKind.Stealth));
    }

    [Fact]
    public void S3_2_Les_effets_sur_toutes_les_cartes_touchent_la_furtivite()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Ability("Vague", "AllCardsBoard", [Act("damage", 100)],
            conditions: ["is_not_empty", "is_enemy"]));
        foe.AddStatus(StatusKind.Stealth, 0, 0);
        duel.Cast(me, me.Abilities[0]);
        Assert.Equal(900, foe.Hp);
        Assert.Equal(1000, me.Hp);                          // « is_enemy » : pas sur soi
    }

    [Fact]
    public void S3_2_Camouflage_empeche_le_ciblage()
    {
        var w = new TestWorld();
        var me = w.Card("Moi", abilities: w.Strike());
        var f1 = w.Card("F1"); var f2 = w.Card("F2");
        var duel = w.Duel([me], [f1, f2]);
        duel.Players[1].Board[0].Toxes.Add(new ToxState(w.Tox("SolCamouflage"), true));
        Assert.Equal([duel.Players[1].Board[1], duel.Players[0].Board[0]],
            duel.LegalCasts().Select(o => o.Target).OrderBy(t => t!.Owner == 0));
    }

    [Fact]
    public void S6_3_Provocation_force_l_adversaire_a_la_viser()
    {
        var w = new TestWorld();
        var me = w.Card("Moi", abilities: w.Ability("Coup", "SelectTarget", [Act("damage", 10)],
            conditions: ["is_not_empty", "ai_is_enemy"]));
        var f1 = w.Card("F1"); var f2 = w.Card("F2");
        var duel = w.Duel([me], [f1, f2]);
        var taunter = duel.Players[1].Board[1];
        taunter.AddStatus(StatusKind.Taunt, 0, 2);
        Assert.True(duel.IsProtected(duel.Players[1].Board[0]));
        // Parmi les cartes adverses, seule celle qui provoque peut être visée (un humain peut aussi viser la sienne, D-053).
        Assert.Equal([taunter], duel.LegalCasts().Select(o => o.Target).Where(t => t!.Owner == 1));
    }

    [Fact]
    public void S3_2_Un_joueur_humain_peut_viser_ses_propres_cartes_avec_ai_is_enemy()
    {
        var w = new TestWorld();
        var me = w.Card("Moi", abilities: w.Ability("Coup", "SelectTarget", [Act("damage", 10)],
            conditions: ["is_not_empty", "ai_is_enemy"]));
        var duel = w.Duel([me], [w.Card("F")]);
        Assert.False(duel.Players[0].IsAi);                 // D-053 : les joueurs simulés sont des humains
        duel.Players[0].IsAi = true;
        Assert.Single(duel.LegalCasts());                   // IA du jeu : seulement l'ennemi
        duel.Players[0].IsAi = false;
        Assert.Equal(2, duel.LegalCasts().Count);           // humain : aussi sa propre carte
    }

    [Fact]
    public void S3_2_Filtre_au_hasard_garde_une_seule_cible()
    {
        var w = new TestWorld();
        var me = w.Card("Moi", abilities: w.Ability("Hasard", "AllCardsBoard", [Act("damage", 100)],
            conditions: ["is_not_empty", "is_enemy"], filters: ["filter_random_1"]));
        var f = w.Card("F");
        var duel = w.Duel([me], [f, f, f]);
        duel.Cast(duel.Players[0].Board[0], duel.Players[0].Board[0].Abilities[0]);
        Assert.Equal(1, duel.Players[1].Board.Count(c => c.Hp == 900));
    }

    // ------------------------------------------------------------------ §6 statuts

    [Fact]
    public void S6_1_Poison_a_chaque_debut_de_tour_du_proprietaire_cumulable_sans_resistance()
    {
        var w = new TestWorld();
        var me = w.Card("Moi");
        var foe = w.Card("Lui", resist: 50);
        var duel = w.Duel([me], [foe]);
        var target = duel.Players[1].Board[0];
        target.AddStatus(StatusKind.Poisoned, 30, 6);
        target.AddStatus(StatusKind.Poisoned, 20, 6);
        Assert.Equal(2, target.Statuses.Count);             // le poison s'empile
        duel.EndTurn();                                     // début du tour de J2
        Assert.Equal(1000 - 50, target.Hp);
    }

    [Fact]
    public void S6_1_Le_poison_ne_consomme_pas_le_bouclier()
    {
        var w = new TestWorld();
        var duel = w.Duel([w.Card("Moi")], [w.Card("Lui")]);
        var target = duel.Players[1].Board[0];
        target.AddStatus(StatusKind.Poisoned, 30, 6);
        target.AddStatus(StatusKind.Shield, 50, 0);
        duel.EndTurn();
        Assert.Equal(970, target.Hp);
        Assert.True(target.Has(StatusKind.Shield));
    }

    [Fact]
    public void S6_2_Les_degats_brisent_le_gel_et_infligent_les_degats_de_degel()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Strike(value: 100));
        foe.AddStatus(StatusKind.Frozen, 60, 2);
        duel.Cast(me, me.Abilities[0], foe);
        Assert.False(foe.Has(StatusKind.Frozen));
        Assert.Equal(1000 - 100 - 60, foe.Hp);
    }

    [Fact]
    public void S6_2_Le_soin_brise_le_gel()
    {
        // Le lanceur gelé ne pourrait pas lancer : on gèle donc une autre alliée.
        var w2 = new TestWorld();
        var healer = w2.Card("Soigneur", abilities: w2.Ability("Soin", "AllCardsBoard", [Act("healTarget", 10)],
            conditions: ["is_not_empty", "is_ally"]));
        var other = w2.Card("Autre");
        var d = w2.Duel([healer, other], [w2.Card("F")]);
        var frozen = d.Players[0].Board[1];
        frozen.Damage = 100;
        frozen.AddStatus(StatusKind.Frozen, 60, 2);
        d.Cast(d.Players[0].Board[0], d.Players[0].Board[0].Abilities[0]);
        Assert.False(frozen.Has(StatusKind.Frozen));
        Assert.Equal(1000 - 100 + 10 - 60, frozen.Hp);
    }

    [Fact]
    public void S6_4_Durees_moins_1_a_chaque_fin_de_tour_des_deux_joueurs()
    {
        var w = new TestWorld();
        var duel = w.Duel([w.Card("Moi")], [w.Card("Lui")]);
        var c = duel.Players[1].Board[0];
        c.AddStatus(StatusKind.Silenced, 0, 2);
        c.AddStatus(StatusKind.Taunt, 0, 0);                // durée 0 = permanent
        duel.EndTurn();                                      // fin du tour de J1
        Assert.Equal(1, c.Get(StatusKind.Silenced)!.Duration);
        duel.EndTurn();                                      // fin du tour de J2
        Assert.False(c.Has(StatusKind.Silenced));
        Assert.True(c.Has(StatusKind.Taunt));
    }

    [Fact]
    public void S6_4_Un_meme_statut_cumule_valeur_et_garde_la_plus_longue_duree()
    {
        var w = new TestWorld();
        var duel = w.Duel([w.Card("Moi")], [w.Card("Lui")]);
        var c = duel.Players[1].Board[0];
        c.AddStatus(StatusKind.Shield, 25, 1);
        c.AddStatus(StatusKind.Shield, 50, 3);
        Assert.Single(c.Statuses);
        Assert.Equal(75, c.Get(StatusKind.Shield)!.Value);
        Assert.Equal(3, c.Get(StatusKind.Shield)!.Duration);
    }

    // ------------------------------------------------------------------ effets v1

    [Fact]
    public void Effet_gain_de_niveau_d_element()
    {
        var w = new TestWorld();
        var (duel, me, _) = Setup(w, w.Ability("Flamme", "PlayerSelf", [Act("AddAllPlayerAttack", 2)]));
        duel.Cast(me, me.Abilities[0]);
        Assert.Equal(2, duel.Players[0].Level("fire"));
    }

    [Fact]
    public void Effet_ajout_de_recharge_sur_la_cible()
    {
        var w = new TestWorld();
        var me = w.Card("Moi", abilities: w.Ability("Temps", "SelectTarget", [Act("AddCooldown", 2)],
            conditions: ["is_not_empty", "is_enemy"]));
        var foe = w.Card("Lui", abilities: w.Strike(cooldown: 1));
        var duel = w.Duel([me], [foe]);
        var target = duel.Players[1].Board[0];
        Assert.Equal(0, target.Abilities[0].CurrentCooldown);
        duel.Cast(duel.Players[0].Board[0], duel.Players[0].Board[0].Abilities[0], target);
        Assert.Equal(2, target.Abilities[0].CurrentCooldown);
    }

    [Fact]
    public void Effet_degats_a_toutes_les_cartes_adverses()
    {
        var w = new TestWorld();
        var me = w.Card("Moi", abilities: w.Ability("Pluie", "PlayerOpponent", [Act("DamageAll", 100)],
            conditions: ["is_not_empty", "is_enemy"]));
        var f = w.Card("F");
        var duel = w.Duel([me], [f, f]);
        duel.Cast(duel.Players[0].Board[0], duel.Players[0].Board[0].Abilities[0]);
        Assert.All(duel.Players[1].Board, c => Assert.Equal(900, c.Hp));
        Assert.Equal(1000, duel.Players[0].Board[0].Hp);
    }

    [Fact]
    public void Effet_non_simule_ne_fait_rien_et_est_signale()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Ability("V2", "SelectTarget", [Act("effet_v2", 1)]));
        duel.Cast(me, me.Abilities[0], foe);
        Assert.Equal(1000, foe.Hp);
        Assert.Contains(duel.Log.Lines, l => l.Contains("non simulé"));
        Assert.False(duel.IsAbilityFullySupported(me.Abilities[0].Def));
    }

    // ------------------------------------------------------------------ §7 mort, §8.2 victoire

    [Fact]
    public void S7_Mort_retire_la_carte_et_donne_50_Spamtons_au_proprietaire()
    {
        var w = new TestWorld();
        var me = w.Card("Moi", abilities: w.Strike(value: 200));
        var f = w.Card("F", hp: 100);
        var g = w.Card("G");
        var duel = w.Duel([me], [f, g]);
        var victim = duel.Players[1].Board[0];
        var before = duel.Players[1].Spamtons;
        duel.Cast(duel.Players[0].Board[0], duel.Players[0].Board[0].Abilities[0], victim);
        Assert.DoesNotContain(victim, duel.Players[1].Board);
        Assert.Contains(victim, duel.Players[1].Discard);
        Assert.Equal(before + 50, duel.Players[1].Spamtons);
        Assert.Equal(1, duel.Players[0].KillCount);
        Assert.Null(duel.Winner);
    }

    [Fact]
    public void S8_2_Un_joueur_sans_carte_perd()
    {
        var w = new TestWorld();
        var me = w.Card("Moi", abilities: w.Strike(value: 200));
        var duel = w.Duel([me], [w.Card("F", hp: 100)]);
        duel.Cast(duel.LegalCasts().Single(o => o.Target!.Owner == 1));
        Assert.Equal(DuelPhase.Ended, duel.Phase);
        Assert.Equal(0, duel.Winner);
    }

    [Fact]
    public void S8_2_Egalite_si_les_deux_joueurs_n_ont_plus_de_carte()
    {
        var w = new TestWorld();
        var me = w.Card("Moi", hp: 100, abilities: w.Ability("Apocalypse", "AllCardsBoard", [Act("damage", 500)]));
        var duel = w.Duel([me], [w.Card("F", hp: 100)]);
        duel.Cast(duel.Players[0].Board[0], duel.Players[0].Board[0].Abilities[0]);
        Assert.Equal(-1, duel.Winner);
    }

    [Fact]
    public void S7_La_derniere_carte_perd_son_camouflage()
    {
        var w = new TestWorld();
        var me = w.Card("Moi", abilities: w.Strike(value: 200));
        var duel = w.Duel([me], [w.Card("F", hp: 100), w.Card("G")]);
        var hidden = duel.Players[1].Board[1];
        hidden.Toxes.Add(new ToxState(w.Tox("SolCamouflage"), true));
        Assert.True(hidden.IsCamouflaged);
        duel.Cast(duel.Players[0].Board[0], duel.Players[0].Board[0].Abilities[0], duel.Players[1].Board[0]);
        Assert.False(hidden.IsCamouflaged);
    }

    // ------------------------------------------------------------------ §8.3 overtime

    [Theory]
    [InlineData(20, 100)]
    [InlineData(21, 150)]
    [InlineData(22, 200)]
    [InlineData(24, 300)]
    [InlineData(26, 400)]
    [InlineData(30, 600)]
    public void S8_3_Perte_de_PV_max_en_overtime(int round, int expected)
    {
        var w = new TestWorld();
        var duel = w.Duel([w.Card("A", hp: 1000)], [w.Card("B")]);
        Assert.Equal(expected, duel.OvertimeLoss(duel.Players[0].Board[0], round));
    }

    [Fact]
    public void S8_3_Pas_d_overtime_avant_le_round_20_puis_perte_au_debut_du_tour()
    {
        var w = new TestWorld();
        var duel = w.Duel([w.Card("A", hp: 1000)], [w.Card("B", hp: 1000)]);
        while (duel.Round < 20) duel.EndTurn();
        Assert.Equal(900, duel.Players[0].Board[0].HpMax);  // tour de J1 au round 20
        Assert.Equal(1000, duel.Players[1].Board[0].HpMax); // J2 n'a pas encore joué
        duel.EndTurn();
        Assert.Equal(900, duel.Players[1].Board[0].HpMax);
    }

    [Fact]
    public void S8_3_L_overtime_finit_toujours_la_partie()
    {
        var w = new TestWorld();
        var duel = w.Duel([w.Card("A", hp: 1500)], [w.Card("B", hp: 350)]);
        while (duel.Phase != DuelPhase.Ended && duel.Round < 40) duel.EndTurn();
        Assert.Equal(DuelPhase.Ended, duel.Phase);
        Assert.Equal(24, duel.Round);           // 1 500 PV : 150+225+300+375+450 = 1 500 perdus au round 24
        Assert.Equal(1, duel.Winner);           // J1 perd sa carte au début de son tour du round 24
    }

    [Fact]
    public void S8_3_Arrondi_bas_une_carte_de_350_PV_survit_jusqu_au_round_25()
    {
        // 350 PV : 35 + 52 + 70 + 87 + 105 = 349 perdus de 20 à 24 (arrondis bas) → 1 PV au round 24.
        var w = new TestWorld();
        var duel = w.Duel([w.Card("A", hp: 350)], [w.Card("B", hp: 100000)]);
        var c = duel.Players[0].Board[0];
        Assert.Equal(349, Enumerable.Range(20, 5).Sum(r => duel.OvertimeLoss(c, r)));
    }

    // ------------------------------------------------------------------ §8.7 hasard reproductible

    [Fact]
    public void S8_7_Meme_graine_meme_partie()
    {
        string Run(ulong seed)
        {
            var w = new TestWorld();
            var ab = w.Ability("Aleatoire", "SelectTarget", [Act("damage", 50, 150, element: null)], miss: 25, crit: 30);
            var a = w.Card("A", abilities: ab);
            var duel = w.Duel([a, a], [a, a], seed);
            Players.DuelRunner.Play(duel, new Players.RandomPlayer(seed + 1), new Players.RandomPlayer(seed + 2));
            return duel.Log.ToString();
        }
        Assert.Equal(Run(42), Run(42));
        Assert.NotEqual(Run(42), Run(43));
    }
}
