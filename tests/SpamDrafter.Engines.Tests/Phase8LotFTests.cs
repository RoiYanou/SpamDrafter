using SpamDrafter.Engines.Analysis;
using SpamDrafter.Engines.Cooking;
using SpamDrafter.Simulator.Data;
using SpamDrafter.Simulator.Engine;
using SpamDrafter.Simulator.Players;

namespace SpamDrafter.Engines.Tests;

/// <summary>Phase 8, lot F (D-050) : decks de référence (« méta ») comme adversaires.</summary>
public class Phase8LotFTests
{
    private static SimKnowledge? Kb => Phase8LotATests.Kb.Value;

    private static List<NamedDeck> Meta(SimKnowledge kb, int count, ulong seed)
    {
        var rng = new Rng(seed);
        return Enumerable.Range(1, count).Select(i => new NamedDeck($"Réf {i}", Bench.RandomDeck(kb, rng))).ToList();
    }

    private static readonly CookSettings TinyCook = new()
    {
        Population = 6, Elites = 2, MutantsPerElite = 2, Generations = 2, ChampionEvery = 0,
        ConfirmPlayer = "rapide", ConfirmCandidates = 3, Proposals = 2,
    };

    private static readonly AnalysisSettings TinySheet = AnalysisSettings.Sheet with
    {
        Player = "rapide", Pairs = 1, TalentPairs = 1, RoleVariants = 2, MetaPairs = 2, MetaRatingPairs = 1, ToxMinGames = 1,
    };

    [Trait("Categorie", "Lent")]
    [Fact]
    public void Le_Cuisinier_affronte_les_decks_de_reference()
    {
        if (Kb is not { } kb) return;
        var meta = Meta(kb, 5, 1);
        var card = kb.Cards.OrderBy(c => c.Id, StringComparer.Ordinal).First().Id;
        var withMeta = GrandCuisinier.Cook(kb, [new FixedCard(card)], TinyCook with { Meta = meta.Select(m => m.Deck).ToList() });
        Assert.True(withMeta.UsesMeta);
        Assert.Equal(5, withMeta.RandomOpponents);
        Assert.All(withMeta.Proposals, p => Assert.Contains(p.Deck, x => x.CardId == card));
        // Confirmation : les 5 decks de référence + les candidats.
        Assert.All(withMeta.Proposals, p => Assert.Equal((5 + TinyCook.ConfirmCandidates) * 2 * TinyCook.ConfirmPairs, p.Confirmation.Games.Count));
        var without = GrandCuisinier.Cook(kb, [new FixedCard(card)], TinyCook);
        Assert.False(without.UsesMeta);
    }

    [Trait("Categorie", "Lent")]
    [Fact]
    public void L_Analyseur_mesure_contre_chaque_deck_de_reference()
    {
        if (Kb is not { } kb) return;
        var meta = Meta(kb, 4, 2);
        var deck = Bench.RandomDeck(kb, new Rng(77));
        var r = GrandAnalyseur.Analyse(kb, deck, TinySheet with { Meta = meta });
        Assert.True(r.UsesMeta);
        Assert.Equal(4, r.Matchups.Count);
        Assert.Equal(meta.Select(m => m.Name).Order(), r.Matchups.Select(m => m.Name).Order());
        Assert.Equal(r.Matchups.Select(m => m.Score.Value).Order(), r.Matchups.Select(m => m.Score.Value));   // le plus dur d'abord
        Assert.All(r.Matchups, m => Assert.Equal(TinySheet.MetaPairs * 2, m.Score.Games));
        Assert.InRange(r.Percentile, 0, 1);
        Assert.Empty(r.Dangers);
        Assert.Equal(4 * TinySheet.Pairs * 2, r.Strength.Games);
        var sheet = SheetWriter.ToText(kb, r);
        Assert.Contains("decks de référence", sheet);
        Assert.DoesNotContain("À CRAINDRE", sheet);                                  // plus de decks de la méta dans l'analyse
        foreach (var m in meta) Assert.DoesNotContain(m.Name, sheet);
        var md = ReportWriter.ToMarkdown(kb, r);
        Assert.DoesNotContain("Adversaires difficiles", md);                          // ni section, ni tableau des decks de la méta
        Assert.DoesNotContain("| Deck de référence | Score | Marge |", md);
        foreach (var m in meta) Assert.DoesNotContain(m.Name, md);
    }

    [Trait("Categorie", "Lent")]
    [Fact]
    public void Un_seul_deck_de_reference_ne_suffit_pas()
    {
        if (Kb is not { } kb) return;
        var r = GrandAnalyseur.Analyse(kb, Bench.RandomDeck(kb, new Rng(78)),
            TinySheet with { Meta = Meta(kb, 1, 3), PoolSize = 20, CalibrationOpponents = 4, Opponents = 4, FieldSeed = 94 });
        Assert.False(r.UsesMeta);
        Assert.Empty(r.Matchups);
    }

    [Fact]
    public void Qualificatif_contre_la_meta()
    {
        // Phase 12, lot B : la note sur 100 remplace les anciens qualificatifs.
        Assert.Equal("Parfait", DeckNote.LabelFor(100));
        Assert.Equal("Excellent", DeckNote.LabelFor(99));
        Assert.Equal("Excellent", DeckNote.LabelFor(75));
        Assert.Equal("Bon", DeckNote.LabelFor(74));
        Assert.Equal("Bon", DeckNote.LabelFor(55));
        Assert.Equal("Passable", DeckNote.LabelFor(54));
        Assert.Equal("Passable", DeckNote.LabelFor(35));
        Assert.Equal("Faible", DeckNote.LabelFor(34));
        Assert.Equal("Faible", DeckNote.LabelFor(1));
        Assert.Equal("Intankable", DeckNote.LabelFor(0));
    }
}
