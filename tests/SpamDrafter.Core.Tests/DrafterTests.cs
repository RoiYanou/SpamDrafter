using SpamDrafter.App.ViewModels;
using SpamDrafter.Core.Decks;
using SpamDrafter.Core.Knowledge;

namespace SpamDrafter.Core.Tests;

public class DeckSearchTests
{
    private static Deck D(string name, params string[] cards) =>
        new() { Name = name, Slots = cards.Select(c => new DeckSlot(c, null)).ToList() };

    private static readonly Deck[] Decks =
    {
        D("Zèbre", "a", "b", "c", "d"),
        D("Alpha", "a", "b", "e", "f"),
        D("Brouillon", "a", "b"),
        D("Autre", "x", "y", "z", "w"),
    };

    private static string[] Names(IEnumerable<DeckMatch> m) => m.Select(x => x.Deck.Name).ToArray();

    [Fact]
    public void Un_talent_precise_la_recherche_et_un_talent_vide_laisse_tout_passer()
    {
        var decks = new[]
        {
            new Deck { Name = "T1", Slots = new List<DeckSlot> { new("a", "t1"), new("b", null) } },
            new Deck { Name = "T2", Slots = new List<DeckSlot> { new("a", "t2"), new("b", null) } },
        };
        Assert.Equal(new[] { "T1" }, Names(DeckSearch.Find(decks, new (string, string?)[] { ("a", "t1") })));
        Assert.Equal(new[] { "T1", "T2" }, Names(DeckSearch.Find(decks, new (string, string?)[] { ("a", null) })));
        Assert.Empty(DeckSearch.Find(decks, new (string, string?)[] { ("a", "t3") }));
        Assert.Equal(new[] { "T2" }, Names(DeckSearch.Find(decks, new (string, string?)[] { ("a", "t2"), ("b", null) })));
    }

    [Fact]
    public void Une_carte_trouve_les_decks_qui_la_contiennent()
        => Assert.Equal(new[] { "Alpha", "Zèbre", "Brouillon" }, Names(DeckSearch.Find(Decks, new[] { "a" })));

    [Fact]
    public void Plusieurs_cartes_exigent_toutes_les_cartes_dans_n_importe_quel_ordre()
    {
        Assert.Equal(new[] { "Zèbre" }, Names(DeckSearch.Find(Decks, new[] { "d", "c", "a" })));
        Assert.Equal(new[] { "Alpha", "Zèbre", "Brouillon" }, Names(DeckSearch.Find(Decks, new[] { "b", "a" })));
    }

    [Fact]
    public void Quatre_cartes_trouvent_le_deck_exact()
        => Assert.Equal(new[] { "Autre" }, Names(DeckSearch.Find(Decks, new[] { "w", "x", "y", "z" })));

    [Fact]
    public void Aucun_resultat_quand_aucun_deck_ne_correspond()
        => Assert.Empty(DeckSearch.Find(Decks, new[] { "a", "x" }));

    [Fact]
    public void Decks_complets_avant_les_brouillons()
    {
        var m = DeckSearch.Find(Decks, new[] { "a" });
        Assert.Equal(new[] { true, true, false }, m.Select(x => x.IsComplete).ToArray());
    }

    [Fact]
    public void Entrees_invalides_et_liste_vide()
    {
        Assert.Empty(DeckSearch.Find(new List<Deck>(), new[] { "a" }));
        Assert.Empty(DeckSearch.Find(Decks, Array.Empty<string>()));
        Assert.Throws<ArgumentException>(() => DeckSearch.Find(Decks, new[] { "a", "a" }));
        Assert.Throws<ArgumentException>(() => DeckSearch.Find(Decks, new[] { "a", "b", "c", "d", "e" }));
    }
}

public class DrafterViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "spamdrafter-dr-" + Guid.NewGuid());
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private static KnowledgeBase? Kb()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "knowledge", "1.30");
            if (File.Exists(Path.Combine(p, "manifest.json"))) return KnowledgeBase.Load(p);
        }
        return null;
    }

    private (DrafterViewModel drafter, DeckBuilderViewModel builder, List<CardItem> items) Make(KnowledgeBase kb)
    {
        var items = kb.Cards.Select(c => new CardItem(c, kb)).ToList();
        var lib = new DeckLibrary(new DeckStore(_dir));
        return (new DrafterViewModel(new CardBrowser(items, kb), items, kb, lib),
                new DeckBuilderViewModel(new CardBrowser(items, kb), items, kb, lib) { AllowIncompleteDecks = true }, items);
    }

    [Fact]
    public void Drafter_vide_tant_que_rien_n_est_sauvegarde_puis_trouve_le_deck_sauvegarde()
    {
        if (Kb() is not { } kb) return;
        var (dr, b, items) = Make(kb);
        Assert.Contains("Aucun deck sauvegardé", dr.Message);

        for (var i = 0; i < 4; i++) { b.Browser.Selected = items[i]; b.AddCommand.Execute(null); }
        b.Name = "Mon deck";
        b.SaveCommand.Execute(null);   // le Drafter est prévenu sans relancer l'application

        dr.Browser.Selected = items[1]; dr.AddCommand.Execute(null);
        dr.Browser.Selected = items[3]; dr.AddCommand.Execute(null);
        var r = Assert.Single(dr.Results);
        Assert.Equal("Mon deck", r.Name);
        Assert.Equal("Complet · créé à la main", r.Badge);
        Assert.Equal(4, r.Lines.Count);

        dr.Browser.Selected = items[10]; dr.AddCommand.Execute(null);
        Assert.Empty(dr.Results);
        Assert.Contains("Aucun de tes decks", dr.Message);
    }

    [Fact]
    public void Recherche_limitee_a_4_cartes_sans_doublon_et_ouverture_dans_le_builder()
    {
        if (Kb() is not { } kb) return;
        var (dr, b, items) = Make(kb);
        b.Browser.Selected = items[0]; b.AddCommand.Execute(null);
        b.SaveCommand.Execute(null);

        for (var i = 0; i < 5; i++) { dr.Browser.Selected = items[i]; dr.AddCommand.Execute(null); }
        Assert.Equal(4, dr.Wanted.Count);
        dr.Browser.Selected = items[0]; dr.AddCommand.Execute(null);
        Assert.Equal(4, dr.Wanted.Count);

        dr.ClearCommand.Execute(null);
        dr.Browser.Selected = items[0]; dr.AddCommand.Execute(null);
        Deck? opened = null;
        dr.OpenRequested += d => opened = d;
        dr.OpenCommand.Execute(Assert.Single(dr.Results));
        Assert.NotNull(opened);
        b.OpenDeck(opened!);
        Assert.Single(b.Slots);
    }
}
