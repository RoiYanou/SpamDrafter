using SpamDrafter.Core.Decks;
using SpamDrafter.Core.Knowledge;

namespace SpamDrafter.Core.Tests;

public class CoreTests
{
    /// <summary>Cherche knowledge/1.30 en remontant les dossiers (dépôt privé). Null si absent (dépôt public seul).</summary>
    private static string? FindKnowledge()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "knowledge", "1.30");
            if (File.Exists(Path.Combine(p, "manifest.json"))) return p;
        }
        return null;
    }

    private static KnowledgeBase? Kb() => FindKnowledge() is { } p ? KnowledgeBase.Load(p) : null;

    [Fact]
    public void Charge_la_base_1_30_avec_les_comptes_attendus()
    {
        if (Kb() is not { } kb) return;
        Assert.Equal("1.30", kb.Manifest.GameVersion);
        Assert.Equal(151, kb.Cards.Count);
        Assert.All(kb.Cards, c => Assert.Equal(3, c.Talents.Count));
        Assert.All(kb.Cards, c => Assert.NotEqual(kb.CardName(c), c.NameKey)); // nom français trouvé
    }

    [Fact]
    public void Les_talents_ont_un_nom_francais()
    {
        if (Kb() is not { } kb) return;
        var t = kb.Cards[0].Talents[0];
        Assert.NotEqual("Talent.Name." + t, kb.TalentName(t));
    }

    [Fact]
    public void Texte_absent_renvoie_la_cle()
    {
        if (Kb() is not { } kb) return;
        Assert.Equal("Inexistant.Cle", kb.Text("Inexistant.Cle"));
    }

    private static Deck ValidDeck(KnowledgeBase kb) => new()
    {
        Name = "Test",
        Slots = kb.Cards.Take(4).Select(c => new DeckSlot(c.Id, c.Talents[0])).ToList(),
    };

    [Fact]
    public void Deck_valide_sans_probleme()
    {
        if (Kb() is not { } kb) return;
        Assert.Empty(DeckValidator.Validate(ValidDeck(kb), kb));
    }

    [Fact]
    public void Deck_refuse_doublon_talent_etranger_et_taille()
    {
        if (Kb() is not { } kb) return;
        var d = ValidDeck(kb);
        d.Slots[1] = d.Slots[0];
        Assert.Contains(DeckValidator.Validate(d, kb), p => p.Contains("double"));

        d = ValidDeck(kb);
        var foreign = kb.Cards[5].Talents.First(t => !kb.Cards[0].Talents.Contains(t));
        d.Slots[0] = new DeckSlot(kb.Cards[0].Id, foreign);
        Assert.Contains(DeckValidator.Validate(d, kb), p => p.Contains("n'appartient pas"));

        d = ValidDeck(kb);
        d.Slots.RemoveAt(3);
        Assert.NotEmpty(DeckValidator.Validate(d, kb));
        Assert.Empty(DeckValidator.Validate(d, kb, requireComplete: false));

        d = ValidDeck(kb);
        d.Slots.Add(new DeckSlot(kb.Cards[10].Id, null));
        Assert.Contains(DeckValidator.Validate(d, kb), p => p.Contains("au plus"));
    }

    [Fact]
    public void Deck_sans_talent_est_accepte()
    {
        if (Kb() is not { } kb) return;
        var d = ValidDeck(kb);
        d.Slots[0] = new DeckSlot(d.Slots[0].CardId, null);
        Assert.Empty(DeckValidator.Validate(d, kb));
    }

    [Fact]
    public void Sauvegarde_puis_rechargement_donnent_le_meme_deck()
    {
        var dir = Path.Combine(Path.GetTempPath(), "spamdrafter-test-" + Guid.NewGuid());
        try
        {
            var store = new DeckStore(dir);
            var deck = new Deck
            {
                Name = "Mon deck", GameVersion = "1.30", Origin = DeckOrigin.Generated,
                Slots = { new DeckSlot("a", "T1"), new DeckSlot("b", null) },
            };
            store.Save(deck);
            File.WriteAllText(Path.Combine(dir, "casse.json"), "{pas du json");

            var errors = new List<string>();
            var loaded = Assert.Single(store.LoadAll(errors));
            Assert.Single(errors);
            Assert.Equal(deck.Id, loaded.Id);
            Assert.Equal("Mon deck", loaded.Name);
            Assert.Equal(DeckOrigin.Generated, loaded.Origin);
            Assert.Equal(deck.Slots, loaded.Slots);

            store.Delete(deck.Id);
            Assert.Empty(store.LoadAll());
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
