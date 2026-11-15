using SpamDrafter.Engines.Cooking;
using SpamDrafter.Engines.Meta;
using SpamDrafter.Simulator.Data;

namespace SpamDrafter.Engines.Tests;

/// <summary>Phase 8, lot I (D-052) : méta synthétique calculée par le Cuisinier.</summary>
public class Phase8LotITests
{
    private static SimKnowledge? Kb => Phase8LotATests.Kb.Value;

    private static MetaSettings Tiny(SimKnowledge kb, int cards = 5) => new()
    {
        Size = 3, MaxRounds = 1, FinalistsFactor = 2, RankPairs = 1, FinalPlayer = "rapide",
        Cards = kb.Cards.Select(c => c.Id).Order(StringComparer.Ordinal).Take(cards).ToList(),
        Cook = new CookSettings
        {
            Population = 6, Elites = 2, MutantsPerElite = 2, Generations = 2, ChampionEvery = 0, ScreenOpponents = 4,
            ConfirmPlayer = "rapide", ConfirmCandidates = 2, ConfirmOpponents = 4, Proposals = 1,
        },
    };

    [Fact]
    public void La_meta_est_variee_reproductible_et_sans_erreur()
    {
        if (Kb is not { } kb) return;
        var s = Tiny(kb);
        var rounds = new List<MetaRound>();
        var a = SyntheticMeta.Build(kb, s, roundDone: rounds.Add);
        Assert.Equal(0, a.Errors);
        Assert.InRange(a.Rounds.Count, 1, 2);
        Assert.Equal(a.Rounds, rounds);
        Assert.Equal(0, a.Rounds[0].Round);
        Assert.InRange(a.Meta.Count, 2, s.Size);
        Assert.All(a.Meta, m =>
        {
            Assert.Equal(kb.Settings.CardsPerDeck, m.Deck.Count);
            Assert.Equal(m.Deck.Count, m.Deck.Select(x => x.CardId).Distinct().Count());   // pas de doublon (D-020)
            Assert.Contains(m.Deck, x => x.CardId == m.FromCard);
            Assert.InRange(m.Score, 0, 1);
        });
        // Variée : deux decks de la méta diffèrent d'au moins MinDifference cartes.
        for (var i = 0; i < a.Meta.Count; i++)
            for (var j = i + 1; j < a.Meta.Count; j++)
                Assert.True(a.Meta[i].Deck.Select(x => x.CardId).Except(a.Meta[j].Deck.Select(x => x.CardId)).Count() >= s.MinDifference);
        // Même base + mêmes réglages = même méta.
        var b = SyntheticMeta.Build(kb, s);
        Assert.Equal(a.Meta.Select(m => GrandCuisinier.Key(m.Deck)), b.Meta.Select(m => GrandCuisinier.Key(m.Deck)));
    }

    [Fact]
    public void Un_tour_suivant_affronte_la_meta_en_place_et_peut_la_garder()
    {
        if (Kb is not { } kb) return;
        var s = Tiny(kb, 4);
        var first = SyntheticMeta.Build(kb, s with { MaxRounds = 0 });
        Assert.Single(first.Rounds);
        Assert.Equal(0, first.Rounds[0].Kept);
        // Reprise : la méta enregistrée sert de départ, le tour 1 la remet en jeu avec les nouveaux decks.
        var next = SyntheticMeta.Build(kb, s, start: first.Meta);
        Assert.Equal(1, next.Rounds[0].Round);
        // Candidats : un deck par carte + la méta en place (un deck identique n'est compté qu'une fois).
        Assert.InRange(next.Rounds[0].Candidates, first.Meta.Count, 4 + first.Meta.Count);
        Assert.InRange(next.Rounds[0].Kept, 0, next.Meta.Count);
        Assert.Equal(next.Stable, next.Rounds[0].Kept >= Math.Ceiling(s.StableShare * next.Meta.Count));
    }

    [Fact]
    public void Le_fichier_de_meta_se_relit_et_refuse_une_autre_version()
    {
        if (Kb is not { } kb) return;
        var meta = SyntheticMeta.Build(kb, Tiny(kb, 3) with { MaxRounds = 0 }).Meta;
        var dir = Path.Combine(Path.GetTempPath(), "spamdrafter-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "meta", "synthetique.json");
        try
        {
            SyntheticMeta.Save(path, kb, meta, 1, 0, false, TimeSpan.FromMinutes(2));
            var back = SyntheticMeta.Load(path, kb);
            Assert.Equal(meta.Select(m => GrandCuisinier.Key(m.Deck)), back.Select(m => GrandCuisinier.Key(m.Deck)));
            Assert.Equal(meta.Select(m => m.FromCard), back.Select(m => m.FromCard));
            File.WriteAllText(path, File.ReadAllText(path).Replace($"\"gameVersion\": \"{kb.GameVersion}\"", "\"gameVersion\": \"0.01\""));
            Assert.Throws<InvalidDataException>(() => SyntheticMeta.Load(path, kb));   // règle n° 7
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Le_Cuisinier_accepte_la_meta_synthetique_comme_adversaires()
    {
        if (Kb is not { } kb) return;
        var s = Tiny(kb, 3);
        var meta = SyntheticMeta.Build(kb, s with { MaxRounds = 0 }).Meta;
        var card = kb.Cards.Select(c => c.Id).Order(StringComparer.Ordinal).Last();
        var res = GrandCuisinier.Cook(kb, [new FixedCard(card)], s.Cook with { Meta = meta.Select(m => m.Deck).ToList() });
        Assert.True(res.UsesMeta);
        Assert.Equal(meta.Count, res.RandomOpponents);
        Assert.Contains(res.Proposals[0].Deck, x => x.CardId == card);
    }
}
