using SpamDrafter.Engines.Cooking;
using SpamDrafter.Simulator.Data;

namespace SpamDrafter.Engines.Tests;

/// <summary>Phase 15 : le Grand Cuisinier limité aux cartes et talents possédés par le joueur.</summary>
public class Phase15OwnedCookTests
{
    private static SimKnowledge? Kb => Phase8LotATests.Kb.Value;

    private static readonly CookSettings Tiny = new()
    {
        ScreenOpponents = 4, Population = 8, Elites = 3, MutantsPerElite = 2, Generations = 4, ChampionEvery = 2, Champions = 2,
        ConfirmPlayer = "rapide", ConfirmCandidates = 4, ConfirmOpponents = 4, Proposals = 3,
    };

    /// <summary>12 cartes possédées : les niveaux de talent vont de 0 à 3 en tournant.</summary>
    private static Dictionary<string, int> Owned(SimKnowledge kb) =>
        kb.Cards.Select(c => c.Id).Order(StringComparer.Ordinal).Take(12).Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i % 4);

    [Fact]
    public void Les_decks_generes_n_utilisent_que_des_cartes_et_talents_possedes()
    {
        if (Kb is not { } kb) return;
        var owned = Owned(kb);
        var start = owned.Keys.Order(StringComparer.Ordinal).First();
        var res = GrandCuisinier.Cook(kb, [new FixedCard(start)], Tiny with { Owned = owned });
        Assert.NotEmpty(res.Proposals);
        foreach (var p in res.Proposals)
            foreach (var slot in p.Deck)
            {
                Assert.True(owned.ContainsKey(slot.CardId), $"{slot.CardId} n'est pas possédée");
                if (slot.TalentId is not null)
                {
                    var rank = kb.Card(slot.CardId).Talents.ToList().IndexOf(slot.TalentId) + 1;
                    Assert.InRange(rank, 1, owned[slot.CardId]);   // « Talent n » est possédé seulement si n <= niveau
                }
            }
    }

    [Fact]
    public void Sans_collection_rien_ne_change()
    {
        if (Kb is not { } kb) return;
        var id = kb.Cards.Select(c => c.Id).Order(StringComparer.Ordinal).First();
        var a = GrandCuisinier.Cook(kb, [new FixedCard(id)], Tiny);
        var b = GrandCuisinier.Cook(kb, [new FixedCard(id)], Tiny with { Owned = null });
        Assert.Equal(a.Proposals.Select(p => GrandCuisinier.Key(p.Deck)), b.Proposals.Select(p => GrandCuisinier.Key(p.Deck)));
    }

    [Fact]
    public void Une_carte_ou_un_talent_non_possede_est_refuse()
    {
        if (Kb is not { } kb) return;
        var owned = Owned(kb);
        var notOwned = kb.Cards.Select(c => c.Id).First(id => !owned.ContainsKey(id));
        Assert.Throws<ArgumentException>(() => GrandCuisinier.Cook(kb, [new FixedCard(notOwned)], Tiny with { Owned = owned }));
        // Carte possédée sans aucun talent (niveau 0) : imposer son talent 1 est refusé.
        var level0 = owned.First(x => x.Value == 0).Key;
        if (kb.Card(level0).Talents.Count > 0)
            Assert.Throws<ArgumentException>(() => GrandCuisinier.Cook(kb, [new FixedCard(level0, kb.Card(level0).Talents[0])], Tiny with { Owned = owned }));
    }

    [Fact]
    public void Moins_de_quatre_cartes_possedees_est_refuse_clairement()
    {
        if (Kb is not { } kb) return;
        var few = kb.Cards.Select(c => c.Id).Order(StringComparer.Ordinal).Take(3).ToDictionary(id => id, _ => 3);
        var ex = Assert.Throws<ArgumentException>(() => GrandCuisinier.Cook(kb, [new FixedCard(few.Keys.First())], Tiny with { Owned = few }));
        Assert.Contains("au moins 4", ex.Message);
    }

    [Fact]
    public void La_cle_de_la_memoire_depend_de_la_collection()
    {
        var sel = new List<SpamDrafter.Simulator.Engine.DuelSlot> { new("a", null) };
        Assert.Equal(History.GenerationHistory.GenerationKey(sel, Effort.Normal), History.GenerationHistory.GenerationKey(sel, Effort.Normal, ""));
        Assert.NotEqual(History.GenerationHistory.GenerationKey(sel, Effort.Normal), History.GenerationHistory.GenerationKey(sel, Effort.Normal, "abc"));
        Assert.NotEqual(History.GenerationHistory.GenerationKey(sel, Effort.Normal, "abc"), History.GenerationHistory.GenerationKey(sel, Effort.Normal, "abd"));
    }
}
