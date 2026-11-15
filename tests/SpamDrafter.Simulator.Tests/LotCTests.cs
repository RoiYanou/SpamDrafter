using SpamDrafter.Simulator.Data;
using SpamDrafter.Simulator.Engine;
using static SpamDrafter.Simulator.Tests.TestWorld;

namespace SpamDrafter.Simulator.Tests;

/// <summary>Phase 6, lot C : Tox (pose, emplacements, passifs innés) et statuts. Cartes d'essai à 1 000 PV ; J1 commence.</summary>
public class LotCTests
{
    private static (Duel duel, CardState me, CardState foe) Setup(TestWorld w, AbilityDef? ability = null,
        ToxDef[]? myTox = null, ToxDef[]? foeTox = null, int foeResist = 0, AbilityDef? foeAbility = null,
        CardDef[]? extraFoes = null)
    {
        var me = ability is null ? w.Card("Moi", innate: myTox) : w.Card("Moi", innate: myTox, abilities: ability);
        var foe = foeAbility is null ? w.Card("Lui", resist: foeResist, innate: foeTox)
                                     : w.Card("Lui", resist: foeResist, innate: foeTox, abilities: foeAbility);
        var duel = w.Duel([me], [foe, .. extraFoes ?? []]);
        return (duel, duel.Players[0].Board[0], duel.Players[1].Board[0]);
    }

    private static void Hit(Duel d, CardState me, CardState target, int ability = 0) => d.Cast(me, me.Abilities[ability], target);

    // ------------------------------------------------------------------ passifs innés et emplacements

    [Fact]
    public void Passif_inne_pose_des_le_depart()
    {
        var w = new TestWorld();
        var (_, me, _) = Setup(w, w.Strike(), myTox: [w.Tox("Brut")]);
        Assert.True(me.HasTox("Brut"));
        Assert.True(me.Toxes[0].IsOriginal);
    }

    [Fact]
    public void Mystere_devient_un_Tox_tire_au_hasard()
    {
        var w = new TestWorld();
        var (_, me, _) = Setup(w, w.Strike(), myTox: [w.Tox("Mystere")]);
        Assert.Equal("Berserk", me.Toxes.Single().Def.StatusType);   // seul Tox du pool d'essai
    }

    // ------------------------------------------------------------------ calcul des dégâts

    [Fact]
    public void Brut_ignore_resistance_et_bouclier_et_retire_la_provocation()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Strike(), myTox: [w.Tox("Brut")], foeTox: [w.Tox("Shield", 50)], foeResist: 50);
        foe.AddStatus(StatusKind.Shield, 50, 1);
        foe.AddStatus(StatusKind.Taunt, 1, 2);
        Hit(duel, me, foe);
        Assert.Equal(900, foe.Hp);
        Assert.False(foe.Has(StatusKind.Shield));
        Assert.False(foe.Has(StatusKind.Taunt));
    }

    [Fact]
    public void Brut_peut_viser_une_carte_protegee()
    {
        var w = new TestWorld();
        var me = w.Card("Moi", innate: [w.Tox("Brut")], abilities: w.Strike());
        var duel = w.Duel([me], [w.Card("A"), w.Card("B")]);
        duel.Players[1].Board[1].AddStatus(StatusKind.Taunt, 1, 2);
        Assert.Equal(3, duel.LegalCasts().Count);           // A protégée reste visable
    }

    [Fact]
    public void Bouclier_recharge_au_debut_du_tour_adverse()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Strike(), foeTox: [w.Tox("Shield", 50)]);
        // 🔒 Au début du tour de J1, les cartes de J2 (l'adversaire) chargent leur bouclier : dès le round 1.
        Assert.True(foe.Has(StatusKind.Shield));
        Hit(duel, me, foe);
        Assert.Equal(950, foe.Hp);
        Assert.False(foe.Has(StatusKind.Shield));
        duel.EndTurn(); duel.EndTurn();
        Assert.True(foe.Has(StatusKind.Shield));             // rechargé au tour suivant de J1
    }

    [Fact]
    public void Multiplicateur_multiplie_le_niveau_d_element()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Strike(), myTox: [w.Tox("Multiplicator", 2)]);
        duel.Players[0].ElementLevels["fire"] = 3;
        Hit(duel, me, foe);
        Assert.Equal(1000 - 160, foe.Hp);                   // niveau 3 × 2 = 6 → +60 %
    }

    [Fact]
    public void Optimisation_toujours_la_valeur_max()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Ability("Large", "SelectTarget", [Act("damage", 10, 200)]), myTox: [w.Tox("Optimisation")]);
        Hit(duel, me, foe);
        Assert.Equal(800, foe.Hp);
    }

    [Fact]
    public void Berserk_de_toutes_les_cartes_du_joueur()
    {
        var w = new TestWorld();
        var me = w.Card("Moi", innate: [w.Tox("Berserk", 25)], abilities: w.Strike());
        var ally = w.Card("Allie", innate: [w.Tox("Berserk", 25)]);
        var duel = w.Duel([me, ally], [w.Card("Lui")]);
        Hit(duel, duel.Players[0].Board[0], duel.Players[1].Board[0]);
        Assert.Equal(850, duel.Players[1].Board[0].Hp);     // +50 %
    }

    [Fact]
    public void Erosion_une_part_des_degats_retire_des_PV_max()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Strike(), myTox: [w.Tox("Erosion", 50)]);
        Hit(duel, me, foe);
        Assert.Equal(950, foe.HpMax);
        Assert.Equal(900, foe.Hp);
    }

    [Fact]
    public void Tenacite_reste_a_1_PV_une_fois()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Strike(value: 5000), foeTox: [w.Tox("Tenacite")]);
        Hit(duel, me, foe);
        Assert.Equal(1, foe.Hp);
        duel.EndTurn(); duel.EndTurn();
        Hit(duel, me, foe);
        Assert.Contains(foe, duel.Players[1].Discard);      // déjà à 1 PV : la Ténacité ne sauve plus
    }

    [Fact]
    public void AoE_touche_les_voisines()
    {
        var w = new TestWorld();
        var me = w.Card("Moi", innate: [w.Tox("Aoe", 20)], abilities: w.Strike());
        var duel = w.Duel([me], [w.Card("A"), w.Card("B"), w.Card("C")]);   // x = 4, 3, 2
        var middle = duel.Players[1].Board[1];
        Hit(duel, duel.Players[0].Board[0], middle);
        Assert.Equal(900, middle.Hp);
        Assert.Equal(980, duel.Players[1].Board[0].Hp);
        Assert.Equal(980, duel.Players[1].Board[2].Hp);
    }

    [Fact]
    public void Inversement_les_degats_deviennent_un_soin()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Strike(), myTox: [w.Tox("Inversement")]);
        foe.Damage = 300;
        Hit(duel, me, foe);
        Assert.Equal(800, foe.Hp);
    }

    // ------------------------------------------------------------------ statuts posés à l'impact

    [Fact]
    public void Poison_pose_X_pourcent_des_PV_max_pendant_6_tours()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Strike(), myTox: [w.Tox("Poisoned", 3)]);
        Hit(duel, me, foe);
        var p = foe.Get(StatusKind.Poisoned)!;
        Assert.Equal(30, p.Value);
        Assert.Equal(6, p.Duration);
        duel.EndTurn();                                      // début du tour de J2 : tick
        Assert.Equal(1000 - 100 - 30, foe.Hp);
    }

    [Fact]
    public void Gel_pose_jusqu_a_la_fin_du_prochain_tour_adverse()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Strike(), myTox: [w.Tox("Frozen", 60)], foeAbility: w.Strike("Riposte"));
        Hit(duel, me, foe);
        Assert.True(foe.Has(StatusKind.Frozen));
        duel.EndTurn();                                      // tour de J2 : gelée, ne peut pas lancer
        Assert.False(duel.CanCast(foe, foe.Abilities[0]));
        duel.EndTurn();
        Assert.False(foe.Has(StatusKind.Frozen));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(100, true)]
    public void Gel_de_la_capacite_seulement_sur_critique(double crit, bool frozen)
    {
        var w = new TestWorld();
        var ab = w.Ability("GrainDeSable", "SelectTarget", [Act("damage", 10)], crit: crit,
            passives: [w.Tox("Frozen", 60)], freezeOnlyOnCrit: true);
        var (duel, me, foe) = Setup(w, ab);
        Hit(duel, me, foe);
        Assert.Equal(frozen, foe.Has(StatusKind.Frozen));
    }

    [Fact]
    public void Provocation_de_la_capacite_posee_sur_la_cible_pour_sa_duree()
    {
        var w = new TestWorld();
        var ab = w.Ability("Couvee", "SelectTarget", [Act("_NULL", 0, element: null)], passives: [w.Tox("Protection")], duration: 2);
        var (duel, me, _) = Setup(w, ab);
        duel.Cast(me, me.Abilities[0], me);
        Assert.Equal(2, me.Get(StatusKind.Taunt)!.Duration);
    }

    [Fact]
    public void Silence_de_l_attaquant()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Strike(), myTox: [w.Tox("Silenced")], foeAbility: w.Strike("Riposte"));
        Hit(duel, me, foe);
        Assert.Equal(2, foe.Get(StatusKind.Silenced)!.Duration);
    }

    [Fact]
    public void Provocation_posee_sur_l_attaquant()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Strike(), myTox: [w.Tox("Protection")]);
        Hit(duel, me, foe);
        Assert.True(me.Has(StatusKind.Taunt));
    }

    [Fact]
    public void Eternite_et_Temporalite()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Strike(), myTox: [w.Tox("Eternity")], foeAbility: w.Strike("Riposte", cooldown: 3));
        var before = foe.Abilities[0].CurrentCooldown;
        Hit(duel, me, foe);
        Assert.Equal(before + 1, foe.Abilities[0].CurrentCooldown);
    }

    [Fact]
    public void Disparition_retire_le_dernier_Tox_de_la_cible()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Strike(), myTox: [w.Tox("Disparition")], foeTox: [w.Tox("Brut"), w.Tox("Tenacite")]);
        Hit(duel, me, foe);
        Assert.Equal(["Brut"], foe.Toxes.Select(t => t.Def.StatusType));
    }

    [Fact]
    public void Immunite_bloque_les_statuts_adverses_et_efface_les_statuts_a_la_pose()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Strike(), myTox: [w.Tox("Poisoned", 3), w.Tox("Disparition")],
            foeTox: [w.Tox("ImunityStatus"), w.Tox("Brut")]);
        Hit(duel, me, foe);
        Assert.False(foe.Has(StatusKind.Poisoned));
        Assert.Equal(2, foe.Toxes.Count);
    }

    [Fact]
    public void Sac_de_frappe_vol_de_vie_renvoi_contrecoup()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Strike(), myTox: [w.Tox("LifeSteal", 50), w.Tox("ContreCoup", 10)],
            foeTox: [w.Tox("SacDeFrappe", 2), w.Tox("Renvoie", 25)]);
        me.Damage = 500;
        Hit(duel, me, foe);
        Assert.Equal(2, duel.Players[1].Level("fire"));     // Sac de frappe : élément de la carte touchée
        Assert.Equal(500 + 50 - 25 - 10, me.Hp);             // vol de vie 50, renvoi 25, contrecoup 10
    }

    [Fact]
    public void Fumee_melange_les_places_de_l_attaquant()
    {
        var w = new TestWorld();
        var me = w.Card("Moi", innate: [w.Tox("Smoked")], abilities: w.Strike());
        var duel = w.Duel([me, w.Card("B")], [w.Card("Lui")]);
        Hit(duel, duel.Players[0].Board[0], duel.Players[1].Board[0]);
        Assert.All(duel.Players[0].Board, c => Assert.True(c.Has(StatusKind.Smoked)));
        Assert.Equal([1, 2], duel.Players[0].Board.Select(c => c.Slot).Order());   // 🔒 regroupées sur x = 1, 2
    }

    // ------------------------------------------------------------------ début de tour

    [Fact]
    public void Coeurs_SpamLand_Soin_primitif_et_Chance_au_debut_du_tour()
    {
        var w = new TestWorld();
        var me = w.Card("Moi", income: 0, innate: [w.Tox("CoeurElement", 2, element: "water"), w.Tox("CoeurVital", 50),
            w.Tox("SpamLand", 8)]);
        var ally = w.Card("Allie", income: 0, innate: [w.Tox("CoeurResistance", 3), w.Tox("Chance", 2), w.Tox("SoinPrimitif", 10)]);
        var duel = w.Duel([me, ally], [w.Card("Lui")]);
        var p = duel.Players[0];
        Assert.Equal(2, p.Level("water"));
        Assert.Equal(1050, p.Board[0].HpMax);
        Assert.Equal(1 + 8, p.Spamtons);
        Assert.Equal(3, p.Board[0].Resistances["fire"]);
        Assert.Equal(1 + 2, p.Luck);
    }

    // ------------------------------------------------------------------ mort

    [Fact]
    public void Phenix_revient_avec_X_pourcent_et_perd_son_Tox()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Strike(value: 2000), foeTox: [w.Tox("Phoenix", 50)], extraFoes: [w.Card("Autre")]);
        Hit(duel, me, foe);
        Assert.Contains(foe, duel.Players[1].Board);
        Assert.Equal(500, foe.Hp);
        Assert.False(foe.HasTox("Phoenix"));
    }

    [Fact]
    public void Bomba_frappe_une_carte_adverse_a_la_mort()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Strike(value: 2000), foeTox: [w.Tox("Bomba", 500)], extraFoes: [w.Card("Autre")]);
        Hit(duel, me, foe);
        Assert.Equal(500, me.Hp);
    }

    [Fact]
    public void Cimetiere_et_Vengeur()
    {
        var w = new TestWorld();
        var me = w.Card("Moi", abilities: w.Strike(value: 2000));
        var grave = w.Card("Fossoyeur", innate: [w.Tox("Cimetierre", 1)]);
        var avenger = w.Card("Vengeur", innate: [w.Tox("Vengeur")], abilities: w.Strike("Lente", cooldown: 5));
        var duel = w.Duel([me], [w.Card("Victime"), grave, avenger]);
        Hit(duel, duel.Players[0].Board[0], duel.Players[1].Board[0]);
        Assert.All(duel.Players[1].ElementLevels.Values, v => Assert.Equal(1, v));
        Assert.Equal(0, duel.Players[1].Board.Single(c => c.Def.Id == "Vengeur").Abilities[0].CurrentCooldown);
    }

    [Fact]
    public void Reset_la_capacite_qui_tue_reste_prete()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Strike(value: 2000, cooldown: 3), myTox: [w.Tox("Reset")], extraFoes: [w.Card("Autre")]);
        duel.EndTurn(); duel.EndTurn(); duel.EndTurn(); duel.EndTurn();
        Hit(duel, me, foe);
        Assert.Equal(0, me.Abilities[0].CurrentCooldown);
    }

    [Fact]
    public void Revanche_Tox_la_carte_tuee_riposte()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Strike(value: 2000), foeTox: [w.Tox("Revanche")],
            foeAbility: w.Strike("Riposte", 300, cooldown: 4), extraFoes: [w.Card("Autre")]);
        Hit(duel, me, foe);
        Assert.Equal(700, me.Hp);
    }

    // ------------------------------------------------------------------ Camouflage

    [Fact]
    public void Camouflage_revele_apres_avoir_agi_puis_recache()
    {
        var w = new TestWorld();
        var me0 = w.Card("Moi", innate: [w.Tox("SolCamouflage")], abilities: w.Strike());
        var duel = w.Duel([me0, w.Card("Allie")], [w.Card("Lui")]);   // pas seule : sinon elle perd son camouflage
        var me = duel.Players[0].Board[0];
        Assert.True(me.IsCamouflaged);
        Hit(duel, me, duel.Players[1].Board[0]);
        Assert.False(me.IsCamouflaged);                     // révélée
        duel.EndTurn(); duel.EndTurn();
        Assert.True(me.IsCamouflaged);                      // recachée au début de son tour
    }

    // ------------------------------------------------------------------ Tox spéciaux

    [Fact]
    public void Coeur_corrompu_et_Vol_de_Tox()
    {
        var w = new TestWorld();
        var (duel, me, foe) = Setup(w, w.Strike(), myTox: [w.Tox("StealTox")], foeTox: [w.Tox("Brut")]);
        Assert.True(me.HasTox("Brut") || foe.HasTox("Brut"));
        Assert.False(me.HasTox("StealTox"));
    }

    [Fact]
    public void Metamorphose_simple_change_la_carte_et_efface_les_Tox()
    {
        var w = new TestWorld();
        w.Tox("MetamorphSimple");
        var epic = w.Card("Epique", hp: 700, rarity: "rarity-epic", abilities: w.Strike("Coup epique"));
        var ability = w.Ability("Transforme", "SelectTarget", [Act("AddSMeta", 100)]);
        var me = w.Card("Moi", rarity: "rarity-epic", innate: [w.Tox("Brut")], abilities: ability);
        var duel = w.Duel([me], [w.Card("Lui")]);
        var c = duel.Players[0].Board[0];
        duel.Cast(c, c.Abilities[0], c);
        Assert.Equal("Epique", c.Def.Id);                    // seule autre carte épique
        Assert.Equal(700, c.Hp);
        Assert.Equal(["Coup epique"], c.Abilities.Select(a => a.Def.Id));
        Assert.Empty(c.Toxes);
        Assert.NotNull(epic);
    }
}
