using SpamDrafter.Engines.Cooking;
using SpamDrafter.Engines.Meta;
using SpamDrafter.Simulator.Data;
using SpamDrafter.Simulator.Engine;
using SpamDrafter.Simulator.Players;

namespace SpamDrafter.Engines.Tests;

/// <summary>Phase 9, lot C : pré-calcul des générations à une carte contre la méta synthétique.</summary>
public class Phase9LotCTests
{
    private static SimKnowledge? Kb => Phase8LotATests.Kb.Value;

    private static readonly CookSettings Tiny = new()
    {
        Population = 6, Elites = 2, MutantsPerElite = 2, Generations = 2, ChampionEvery = 0, ScreenOpponents = 4,
        ConfirmPlayer = "rapide", ConfirmCandidates = 3, ConfirmOpponents = 4, Proposals = 2,
    };

    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "spamdrafter-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    /// <summary>Petite méta factice (3 decks au hasard) enregistrée au format de la méta synthétique.</summary>
    private static string FakeMeta(SimKnowledge kb, string dir, ulong seed = 7)
    {
        var rng = new Rng(seed);
        var decks = Enumerable.Range(0, 3).Select(_ => new MetaDeck(Bench.RandomDeck(kb, rng), 0.5, "x")).ToList();
        var path = Path.Combine(dir, "synthetique.json");
        SyntheticMeta.Save(path, kb, decks, seed, 0, false, TimeSpan.Zero);
        return path;
    }

    private static List<string> Cards(SimKnowledge kb, int n) =>
        kb.Cards.Select(c => c.Id).Order(StringComparer.Ordinal).Take(n).ToList();

    [Trait("Categorie", "Lent")]
    [Fact]
    public void Chaque_carte_a_ses_propositions_et_le_fichier_se_relit()
    {
        if (Kb is not { } kb) return;
        var dir = TempDir();
        var meta = FakeMeta(kb, dir);
        var path = Path.Combine(dir, "gen.json");
        var (content, computed) = OneCardGenerations.Build(kb, meta, path, cards: Cards(kb, 2), cookOverride: Tiny);
        Assert.Equal(2, computed);
        Assert.Equal(2, content.Cards.Count);
        Assert.Equal(OneCardGenerations.FileSha256(meta), content.MetaSha256);

        var loaded = OneCardGenerations.Load(path, kb);
        foreach (var id in Cards(kb, 2))
        {
            var props = loaded[id];
            Assert.InRange(props.Count, 1, Tiny.Proposals);
            Assert.All(props, p =>
            {
                Assert.Contains(p.Deck, s => s.CardId == id);                                // la carte imposée est là
                Assert.Equal(kb.Settings.CardsPerDeck, p.Deck.Select(s => s.CardId).Distinct().Count());   // D-020
                Assert.InRange(p.Score, 0, 1);
                Assert.True(p.UsesMeta);
            });
        }
    }

    [Trait("Categorie", "Lent")]
    [Fact]
    public void Un_calcul_interrompu_reprend_et_une_nouvelle_meta_refait_tout()
    {
        if (Kb is not { } kb) return;
        var dir = TempDir();
        var meta = FakeMeta(kb, dir);
        var path = Path.Combine(dir, "gen.json");
        OneCardGenerations.Build(kb, meta, path, cards: Cards(kb, 2), cookOverride: Tiny);
        Assert.Equal(1, OneCardGenerations.Build(kb, meta, path, cards: Cards(kb, 3), cookOverride: Tiny).Computed);
        Assert.Equal(0, OneCardGenerations.Build(kb, meta, path, cards: Cards(kb, 3), cookOverride: Tiny).Computed);

        FakeMeta(kb, dir, seed: 8);   // autre méta (même fichier) : les résultats ne valent plus
        Assert.Equal(3, OneCardGenerations.Build(kb, meta, path, cards: Cards(kb, 3), cookOverride: Tiny).Computed);
    }

    [Trait("Categorie", "Lent")]
    [Fact]
    public void Meme_meta_et_memes_reglages_donnent_les_memes_propositions()
    {
        if (Kb is not { } kb) return;
        var dir = TempDir();
        var meta = FakeMeta(kb, dir);
        var a = OneCardGenerations.Build(kb, meta, Path.Combine(dir, "a.json"), cards: Cards(kb, 1), cookOverride: Tiny).Content;
        var b = OneCardGenerations.Build(kb, meta, Path.Combine(dir, "b.json"), cards: Cards(kb, 1), cookOverride: Tiny).Content;
        Assert.Equal(
            a.Cards.SelectMany(c => c.Proposals).Select(p => string.Join("|", p.Cards.Select(s => s.Card + "/" + s.Talent))),
            b.Cards.SelectMany(c => c.Proposals).Select(p => string.Join("|", p.Cards.Select(s => s.Card + "/" + s.Talent))));
    }

    [Trait("Categorie", "Lent")]
    [Fact]
    public void Un_fichier_d_une_autre_version_est_refuse()
    {
        if (Kb is not { } kb) return;
        var dir = TempDir();
        var path = Path.Combine(dir, "gen.json");
        OneCardGenerations.Build(kb, FakeMeta(kb, dir), path, cards: Cards(kb, 1), cookOverride: Tiny);
        var c = OneCardGenerations.Read(path);
        OneCardGenerations.Save(path, c with { GameVersion = "0.99" });
        Assert.Throws<InvalidDataException>(() => OneCardGenerations.Load(path, kb));
    }
}
