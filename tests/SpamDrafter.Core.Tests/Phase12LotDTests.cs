using SpamDrafter.App.ViewModels;
using SpamDrafter.Core.Decks;
using SpamDrafter.Core.Knowledge;

namespace SpamDrafter.Core.Tests;

/// <summary>Phase 12, lot D : compteurs du deck en direct.</summary>
public class Phase12LotDTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "spamdrafter-lotd-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static string? FindKnowledge()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "knowledge", "1.30");
            if (File.Exists(Path.Combine(p, "manifest.json"))) return p;
        }
        return null;
    }

    private (DeckBuilderViewModel Vm, KnowledgeBase Kb, List<CardItem> Items)? Make()
    {
        if (FindKnowledge() is not { } p) return null;
        var kb = KnowledgeBase.Load(p);
        var items = kb.Cards.Select(c => new CardItem(c, kb)).ToList();
        var vm = new DeckBuilderViewModel(new CardBrowser(items, kb), items, kb, new DeckLibrary(new DeckStore(Path.Combine(_dir, "decks")))) { AllowIncompleteDecks = true };
        return (vm, kb, items);
    }

    private static void Add(DeckBuilderViewModel vm, CardItem c) { vm.Browser.Selected = c; vm.AddCommand.Execute(null); }

    // ----- Compteurs -----

    [Fact]
    public void Les_compteurs_sont_lus_dans_les_donnees_des_cartes()
    {
        if (FindKnowledge() is not { } p) return;
        var kb = KnowledgeBase.Load(p);
        var cards = kb.Cards.Take(4).ToList();
        var stats = DeckStats.Compute(cards, kb);
        Assert.Equal(cards.Sum(c => c.Hp), stats.TotalHp);
        Assert.Equal(cards.Sum(c => c.GoldIncome), stats.TotalIncome);
        Assert.Equal(4, stats.Elements.Sum(e => e.Count));
        Assert.All(stats.Elements, e => Assert.Equal(cards.Count(c => c.Element == e.ElementId), e.Count));
        Assert.Equal(6, stats.Resistances.Count);                  // les 6 éléments de combat, pas l'éther
        Assert.DoesNotContain(stats.Resistances, r => r.ElementId == "divine");
        foreach (var r in stats.Resistances)
            Assert.Equal(cards.Average(c => c.Resistances.GetValueOrDefault(r.ElementId)), r.Average, 6);
        Assert.All(stats.Weaknesses, w => Assert.True(w.Value < 0));
    }

    [Fact]
    public void Les_points_faibles_sont_les_resistances_negatives_les_plus_basses_d_abord()
    {
        if (FindKnowledge() is not { } p) return;
        var kb = KnowledgeBase.Load(p);
        // Cartes réelles avec des résistances négatives.
        var fragile = kb.Cards.Where(c => c.Resistances.Values.Any(v => v < 0)).Take(3).ToList();
        Assert.NotEmpty(fragile);
        var stats = DeckStats.Compute(fragile, kb);
        Assert.NotEmpty(stats.Weaknesses);
        Assert.Equal(stats.Weaknesses.Select(w => w.Value).Order(), stats.Weaknesses.Select(w => w.Value));
        Assert.Equal(fragile.SelectMany(c => c.Resistances.Values).Count(v => v < 0), stats.Weaknesses.Count);
        Assert.Empty(DeckStats.Compute(Array.Empty<Card>(), kb).Weaknesses);
    }

    [Fact]
    public void Le_deck_builder_affiche_les_compteurs_en_direct()
    {
        if (Make() is not { } w) return;
        Assert.False(w.Vm.HasStats);
        Add(w.Vm, w.Items[0]);
        Assert.True(w.Vm.HasStats);
        Assert.StartsWith("Éléments : ", w.Vm.ElementsText);
        Assert.Contains("×1", w.Vm.ElementsText);
        var one = w.Vm.TotalsText;
        Assert.Contains("PV totaux", one);
        Assert.EndsWith(" PV", w.Vm.HpTotalShort);
        Assert.NotEmpty(w.Vm.IncomeTotalShort);
        Add(w.Vm, w.Items[1]);
        Assert.NotEqual(one, w.Vm.TotalsText);                 // se met à jour à chaque carte
        Assert.StartsWith("Résistances moyennes : ", w.Vm.ResistancesText);
        Assert.Contains(" %", w.Vm.ResistancesText);
        w.Vm.RemoveCommand.Execute(w.Vm.Slots[0]);
        w.Vm.RemoveCommand.Execute(w.Vm.Slots[0]);
        Assert.False(w.Vm.HasStats);
    }

    [Fact]
    public void Les_compteurs_suivent_l_ouverture_d_un_deck_et_les_resistances_ont_une_icone_et_une_valeur()
    {
        if (Make() is not { } w) return;
        var fragile = w.Items.First(i => i.Card.Resistances.Values.Any(v => v < 0));
        Add(w.Vm, fragile);
        Assert.NotEmpty(w.Vm.ResistanceItems);                  // comme sur la fiche d'une carte : icône + valeur
        Assert.All(w.Vm.ResistanceItems, r => Assert.EndsWith("%", r.Percent));
        Assert.Contains(w.Vm.ResistanceItems, r => r.Percent.StartsWith("-"));
        w.Vm.Name = "Fragile";
        w.Vm.SaveCommand.Execute(null);
        w.Vm.NewCommand.Execute(null);
        Assert.False(w.Vm.HasStats);
        w.Vm.SelectedSaved = w.Vm.SavedDecks[0];
        Assert.True(w.Vm.HasStats);
        Assert.NotEmpty(w.Vm.ResistanceItems);
    }
}
