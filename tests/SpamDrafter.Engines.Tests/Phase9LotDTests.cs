using SpamDrafter.Engines.Analysis;
using SpamDrafter.Engines.Cooking;
using SpamDrafter.Engines.Meta;
using SpamDrafter.Simulator.Data;
using SpamDrafter.Simulator.Engine;
using SpamDrafter.Simulator.Players;

namespace SpamDrafter.Engines.Tests;

/// <summary>Phase 9, lot D : l'application utilise la méta synthétique et le pré-calcul livrés avec les données.</summary>
public class Phase9LotDTests
{
    private static SimKnowledge? Kb => Phase8LotATests.Kb.Value;

    private static readonly CookSettings Tiny = new()
    {
        Population = 6, Elites = 2, MutantsPerElite = 2, Generations = 2, ChampionEvery = 0, ScreenOpponents = 4,
        ConfirmPlayer = "rapide", ConfirmCandidates = 3, ConfirmOpponents = 4, Proposals = 2,
    };

    /// <summary>Dossier de données factice : meta/synthetique.json (3 decks au hasard) et un petit pré-calcul.</summary>
    private static (string Dir, List<string> Cards) FakeData(SimKnowledge kb, ulong seed = 7)
    {
        var dir = Path.Combine(Path.GetTempPath(), "spamdrafter-tests", Guid.NewGuid().ToString("N"));
        var rng = new Rng(seed);
        var decks = Enumerable.Range(0, 3).Select(_ => new MetaDeck(Bench.RandomDeck(kb, rng), 0.5, "x")).ToList();
        SyntheticMeta.Save(SyntheticMeta.DefaultPath(dir), kb, decks, seed, 0, false, TimeSpan.Zero);
        var cards = kb.Cards.Select(c => c.Id).Order(StringComparer.Ordinal).Take(2).ToList();
        OneCardGenerations.Build(kb, SyntheticMeta.DefaultPath(dir), OneCardGenerations.DefaultPath(dir), cards: cards, cookOverride: Tiny);
        return (dir, cards);
    }

    [Trait("Categorie", "Lent")]
    [Fact]
    public async Task Sans_deck_de_reference_la_meta_synthetique_sert_d_adversaire_et_une_carte_est_immediate()
    {
        if (Kb is not { } kb) return;
        var (dir, cards) = FakeData(kb);
        var data = SyntheticData.Load(dir, kb);
        Assert.Null(data.Warning);
        Assert.Equal(3, data.Meta.Count);
        var c = new Coordinator(kb, data);
        Assert.Equal(OpponentSource.Synthetic, c.Opponents);
        Assert.True(c.UsesMeta);

        var task = c.GenerateAsync([new FixedCard(cards[0])], Effort.Normal);
        Assert.True(task.IsCompleted);                                   // aucun calcul : réponse immédiate
        var decks = await task;
        Assert.All(decks, d => { Assert.True(d.Precomputed); Assert.Contains(d.Deck, s => s.CardId == cards[0]); });
        Assert.Equal(OneCardGenerations.Load(OneCardGenerations.DefaultPath(dir), kb)[cards[0]].Select(d => d.Deck.Count), decks.Select(d => d.Deck.Count));
        Assert.NotNull(c.Precomputed([new FixedCard(cards[0])], Effort.Rapide, Coordinator.AppSeed));
    }

    [Trait("Categorie", "Lent")]
    [Fact]
    public void Le_precalcul_ne_sert_que_dans_son_cas_exact()
    {
        if (Kb is not { } kb) return;
        var (dir, cards) = FakeData(kb);
        var c = new Coordinator(kb, SyntheticData.Load(dir, kb));
        var talent = kb.Card(cards[0]).Talents[0];
        Assert.Null(c.Precomputed([new FixedCard(cards[0], talent)], Effort.Normal, Coordinator.AppSeed));      // talent imposé
        Assert.Null(c.Precomputed([new FixedCard(cards[0]), new FixedCard(cards[1])], Effort.Normal, Coordinator.AppSeed));
        Assert.Null(c.Precomputed([new FixedCard(cards[0])], Effort.Precis, Coordinator.AppSeed));            // plus juste : on calcule
        Assert.Null(c.Precomputed([new FixedCard(cards[0])], Effort.Normal, 2));
        var other = kb.Cards.Select(x => x.Id).Order(StringComparer.Ordinal).Skip(5).First();
        Assert.Null(c.Precomputed([new FixedCard(other)], Effort.Normal, Coordinator.AppSeed));               // carte non pré-calculée
    }

    [Trait("Categorie", "Lent")]
    [Fact]
    public void Un_precalcul_fait_contre_une_autre_meta_est_ignore()
    {
        if (Kb is not { } kb) return;
        var (dir, _) = FakeData(kb);
        var rng = new Rng(99);
        var decks = Enumerable.Range(0, 3).Select(_ => new MetaDeck(Bench.RandomDeck(kb, rng), 0.5, "y")).ToList();
        SyntheticMeta.Save(SyntheticMeta.DefaultPath(dir), kb, decks, 99, 0, false, TimeSpan.Zero);
        var data = SyntheticData.Load(dir, kb);
        Assert.Equal(3, data.Meta.Count);          // la méta reste utilisée
        Assert.Null(data.OneCard);                 // mais pas le pré-calcul
        Assert.NotNull(data.Warning);
    }

    [Fact]
    public void Sans_fichier_de_meta_les_adversaires_sont_au_hasard()
    {
        if (Kb is not { } kb) return;
        var dir = Path.Combine(Path.GetTempPath(), "spamdrafter-tests", Guid.NewGuid().ToString("N"));
        var c = new Coordinator(kb, SyntheticData.Load(dir, kb));
        Assert.Equal(OpponentSource.Random, c.Opponents);
        Assert.False(c.UsesMeta);
    }

    /// <summary>Les données livrées (knowledge/1.30) : méta et pré-calcul cohérents, toutes les cartes couvertes.</summary>
    [Fact]
    public void Les_donnees_1_30_livrent_une_meta_et_un_precalcul_complet()
    {
        if (Kb is not { } kb) return;
        string? dir = null;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null && dir is null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "knowledge", "1.30");
            if (File.Exists(OneCardGenerations.DefaultPath(p))) dir = p;
        }
        if (dir is null) return;   // dépôt public seul : données absentes
        var data = SyntheticData.Load(dir, kb);
        Assert.Null(data.Warning);
        Assert.Equal(20, data.Meta.Count);
        Assert.NotNull(data.OneCard);
        Assert.All(kb.Cards, card => Assert.NotEmpty(data.OneCard![card.Id]));
    }
}
