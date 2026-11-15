using SpamDrafter.Simulator.Data;
using SpamDrafter.Simulator.Engine;
using static SpamDrafter.Simulator.Tests.TestWorld;

namespace SpamDrafter.Simulator.Tests;

/// <summary>
/// Phase 6, lot D : boutique (offre, acheter, relancer, passer, bannir, Casino, Petit Prix) et derniers effets.
/// Boutique d'essai : seulement des communs (Brut 20, Bouclier 15, Ténacité 5, Brut rare 30).
/// </summary>
public class LotDTests
{
    /// <summary>Duel où J1 (une ou plusieurs cartes) arrive au round 2 avec sa boutique ouverte.</summary>
    private static Duel AtRound2(TestWorld w, CardDef[]? mine = null, int income = 50)
    {
        var duel = w.Duel(mine ?? [w.Card("Moi", income: income)], [w.Card("Lui", income: 0)]);
        duel.EndTurn(); duel.EndTurn();
        return duel;
    }

    [Fact]
    public void La_boutique_s_ouvre_seulement_les_rounds_pairs_et_se_ferme_en_fin_de_tour()
    {
        var w = new TestWorld();
        var duel = w.Duel([w.Card("Moi")], [w.Card("Lui")]);
        Assert.Empty(duel.ShopItems);                        // round 1
        duel.EndTurn();
        Assert.Empty(duel.ShopItems);
        duel.EndTurn();                                      // round 2, J1
        Assert.Equal(3, duel.ShopItems.Count);
        Assert.Equal(3, duel.ShopItems.Distinct().Count());  // 3 Tox différents
        duel.EndTurn();                                      // round 2, J2 : sa propre boutique
        Assert.Equal(3, duel.ShopItems.Count);
        duel.EndTurn();                                      // round 3
        Assert.Empty(duel.ShopItems);
    }

    [Fact]
    public void Acheter_pose_le_Tox_paie_et_ferme_la_boutique()
    {
        var w = new TestWorld();
        var duel = AtRound2(w);
        var p = duel.Players[0];
        var item = duel.ShopItems[0];
        var before = p.Spamtons;
        var card = p.Board[0];
        duel.Buy(item, card);
        Assert.Contains(card.Toxes, t => t.Def == item);
        Assert.Equal(before - item.Cost, p.Spamtons);
        Assert.Empty(duel.ShopItems);
        Assert.DoesNotContain(duel.LegalActions(), a => a.Kind is ActionKind.Reroll or ActionKind.Skip or ActionKind.Buy);
    }

    [Fact]
    public void Carte_pleine_il_faut_liberer_un_emplacement()
    {
        var w = new TestWorld();
        var full = w.Card("Pleine", income: 50, abilities: [w.Strike("A1"), w.Strike("A2"), w.Strike("A3")]);
        var duel = AtRound2(w, [full]);
        var card = duel.Players[0].Board[0];
        Assert.Throws<InvalidOperationException>(() => duel.Buy(duel.ShopItems[0], card));
        Assert.All(duel.LegalActions().Where(a => a.Kind == ActionKind.Buy), a => Assert.NotNull(a.Origin));
        duel.Buy(duel.ShopItems[0], card, "A:A2");
        Assert.Equal(["A1", "A3"], card.Abilities.Select(a => a.Def.Id));
        Assert.Single(card.Toxes);
    }

    [Fact]
    public void Achat_trop_cher_impossible()
    {
        var w = new TestWorld();
        var duel = AtRound2(w, income: 0);                   // 1 + 2 = 3 Spamtons
        Assert.DoesNotContain(duel.LegalActions(), a => a.Kind == ActionKind.Buy && a.Item!.Cost > 3);
    }

    [Fact]
    public void Relancer_coute_10_puis_15()
    {
        var w = new TestWorld();
        var duel = AtRound2(w);
        var p = duel.Players[0];
        var s0 = p.Spamtons;
        duel.Reroll();
        duel.Reroll();
        Assert.Equal(s0 - 10 - 15, p.Spamtons);
        Assert.Equal(20, duel.RerollPrice(p));
        duel.EndTurn(); duel.EndTurn(); duel.EndTurn(); duel.EndTurn();   // round 4 : surcoût remis à 0
        Assert.Equal(10, duel.RerollPrice(p));
    }

    [Fact]
    public void Casino_premiere_relance_du_tour_gratuite()
    {
        var w = new TestWorld();
        var t = w.Talent("Casino", "StartOfGame", "Self", [Act("AddSCasino", 0)]);
        var me = w.Card("Moi", income: 50, talents: [t]);
        var duel = w.Duel([(me, t)], [(w.Card("Lui"), (AbilityDef?)null)]);
        duel.EndTurn(); duel.EndTurn();
        var p = duel.Players[0];
        Assert.Equal(0, duel.RerollPrice(p));
        var s0 = p.Spamtons;
        duel.Reroll();
        Assert.Equal(s0, p.Spamtons);
        Assert.Equal(15, duel.RerollPrice(p));
    }

    [Fact]
    public void Petit_Prix_premier_achat_gratuit_puis_consomme()
    {
        var w = new TestWorld();
        var t = w.Talent("PetitPrix", "StartOfGame", "Self", [Act("AddSPetitPrix", 0)]);
        var me = w.Card("Moi", income: 50, talents: [t]);
        var duel = w.Duel([(me, t)], [(w.Card("Lui"), (AbilityDef?)null)]);
        duel.EndTurn(); duel.EndTurn();
        var p = duel.Players[0];
        var s0 = p.Spamtons;
        duel.Buy(duel.ShopItems[0], p.Board[0]);
        Assert.Equal(s0, p.Spamtons);
        Assert.False(p.Board[0].HasTalentStatus("PetitPrix"));
    }

    [Fact]
    public void Passer_donne_10_Spamtons_et_ferme_la_boutique()
    {
        var w = new TestWorld();
        var duel = AtRound2(w);
        var p = duel.Players[0];
        var s0 = p.Spamtons;
        duel.SkipShop();
        Assert.Equal(s0 + 10, p.Spamtons);
        Assert.Empty(duel.ShopItems);
    }

    [Fact]
    public void Bannir_un_Tox_bannit_tout_son_type_pour_les_deux_joueurs_une_fois()
    {
        var w = new TestWorld();
        var duel = AtRound2(w);
        var brut = duel.ShopItems.FirstOrDefault(t => t.StatusType == "Brut");
        while (brut is null) { duel.Reroll(); brut = duel.ShopItems.FirstOrDefault(t => t.StatusType == "Brut"); }
        duel.Ban(brut);
        Assert.Contains("shop_brut", duel.Banned);
        Assert.Contains("shop_brut2", duel.Banned);           // même type et même élément
        Assert.Contains(brut, duel.ShopItems);               // reste affiché…
        Assert.DoesNotContain(duel.LegalActions(), a => a.Kind == ActionKind.Buy && a.Item == brut);   // … mais pas achetable
        Assert.DoesNotContain(duel.LegalActions(), a => a.Kind == ActionKind.Ban);   // un seul bannissement
        duel.SkipShop();
        duel.EndTurn();                                      // boutique de J2 : plus de Brut
        Assert.DoesNotContain(duel.ShopItems, t => t.StatusType == "Brut");
    }

    [Fact]
    public void Acheter_revele_une_carte_camouflee_et_brise_le_gel()
    {
        var w = new TestWorld();
        var me = w.Card("Moi", income: 50, innate: [w.Tox("SolCamouflage")]);
        var duel = AtRound2(w, [me, w.Card("Allie")]);
        var card = duel.Players[0].Board[0];
        card.AddStatus(StatusKind.Frozen, 60, 2);
        Assert.True(card.IsCamouflaged);
        duel.Buy(duel.ShopItems.First(t => t.StatusType != "SolCamouflage"), card);
        Assert.False(card.IsCamouflaged);
        Assert.False(card.Has(StatusKind.Frozen));
        Assert.Equal(940, card.Hp);                          // dégâts de dégel
    }

    // ------------------------------------------------------------------ derniers effets (talents à Tox)

    [Fact]
    public void Remplacer_par_un_Tox_libere_un_emplacement()
    {
        var w = new TestWorld();
        var t = w.Talent("Bomba", "StartOfGame", "Self", [Act("ReplaceLastWithTenacite", 0)]);
        var me = w.Card("Moi", talents: [t], abilities: [w.Strike("A1"), w.Strike("A2"), w.Strike("A3")]);
        var duel = w.Duel([(me, t)], [(w.Card("Lui"), (AbilityDef?)null)]);
        var c = duel.Players[0].Board[0];
        Assert.Equal(["A1", "A2"], c.Abilities.Select(a => a.Def.Id));
        Assert.True(c.HasTox("Tenacite"));
    }

    [Fact]
    public void Evolution_change_la_rarete_des_Tox_d_origine()
    {
        var w = new TestWorld();
        var t = w.Talent("Evolution", "OnHPPalierChange", "Self", [Act("EffectEvolution", 100, element: null)]);
        var common = w.Tox("Brut", rarity: "rarity-common", uuid: "brut_c");
        w.Tox("Brut", rarity: "rarity-rare", uuid: "brut_r");
        var me = w.Card("Moi", talents: [t], innate: [common]);
        var hitter = w.Card("Frappeur", abilities: w.Strike("Gros", 300));
        var duel = w.Duel([(hitter, (AbilityDef?)null)], [(me, t)]);
        var c = duel.Players[1].Board[0];
        duel.Cast(duel.Players[0].Board[0], duel.Players[0].Board[0].Abilities[0], c);   // 70 % des PV → rare
        var evolved = c.Toxes.Single().Def;                  // 🔒 le premier Tox « Brut » rare de la liste
        Assert.Equal(("Brut", "rarity-rare"), (evolved.StatusType, evolved.Rarity));
        Assert.True(c.Toxes.Single().IsOriginal);
    }
}
