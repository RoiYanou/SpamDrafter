using SpamDrafter.App.ViewModels;
using SpamDrafter.Core.Decks;
using SpamDrafter.Core.Knowledge;

namespace SpamDrafter.Core.Tests;

/// <summary>Logique du Deck builder, sans ouvrir de fenêtre.</summary>
public class DeckBuilderViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "spamdrafter-vm-" + Guid.NewGuid());

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

    private DeckBuilderViewModel Make(KnowledgeBase kb)
    {
        var items = kb.Cards.Select(c => new CardItem(c, kb)).ToList();
        return new DeckBuilderViewModel(new CardBrowser(items, kb), items, kb, new DeckLibrary(new DeckStore(_dir)));
    }

    private static void Add(DeckBuilderViewModel vm, CardItem c) { vm.Browser.Selected = c; vm.AddCommand.Execute(null); }

    [Fact]
    public void Ajout_limite_a_4_cartes_sans_doublon()
    {
        if (Kb() is not { } kb) return;
        var vm = Make(kb);
        var items = vm.Browser.Items.ToList();
        Add(vm, items[0]); Add(vm, items[0]);
        Assert.Single(vm.Slots);
        Add(vm, items[1]); Add(vm, items[2]); Add(vm, items[3]); Add(vm, items[4]);
        Assert.Equal(4, vm.Slots.Count);
        Assert.Contains("déjà", vm.Status);
    }

    [Fact]
    public void Sauvegarde_et_rechargement_dans_un_nouveau_builder()
    {
        if (Kb() is not { } kb) return;
        var vm = Make(kb);
        var items = vm.Browser.Items.ToList();
        for (var i = 0; i < 4; i++) Add(vm, items[i]);
        vm.Slots[2].Talent = vm.Slots[2].TalentOptions[3]; // 3e talent
        vm.Name = "Mon deck";
        vm.SaveCommand.Execute(null);
        Assert.Empty(vm.Problems);

        var vm2 = Make(kb);
        var saved = Assert.Single(vm2.SavedDecks);
        vm2.SelectedSaved = saved;
        Assert.Equal("Mon deck", vm2.Name);
        Assert.Equal(4, vm2.Slots.Count);
        Assert.Equal(items[2].Card.Talents[2], vm2.Slots[2].Talent.Id);
        Assert.Equal(items.Take(4).Select(i => i.Card.Id), vm2.Slots.Select(s => s.Card.Card.Id));
    }

    [Fact]
    public void Brouillon_incomplet_sauvegarde_mais_signale_le_probleme()
    {
        if (Kb() is not { } kb) return;
        var vm = Make(kb);
        Add(vm, vm.Browser.Items[0]);
        vm.SaveCommand.Execute(null);
        Assert.Contains("incomplet", vm.Status);
        Assert.NotEmpty(vm.Problems);
        Assert.Single(vm.SavedDecks);
    }

    [Fact]
    public void Suppression_retire_le_deck()
    {
        if (Kb() is not { } kb) return;
        var vm = Make(kb);
        Add(vm, vm.Browser.Items[0]);
        vm.SaveCommand.Execute(null);
        vm.DeleteCommand.Execute(null);
        // Une fenêtre de confirmation nomme le deck ; rien n'est supprimé avant la confirmation.
        Assert.True(vm.DeleteConfirmVisible);
        Assert.Contains(vm.SavedDecks[0].Name, vm.DeleteConfirmText);
        vm.DeleteCancelCommand.Execute(null);
        Assert.False(vm.DeleteConfirmVisible);
        Assert.Single(vm.SavedDecks);
        vm.DeleteCommand.Execute(null);
        vm.DeleteConfirmCommand.Execute(null);
        Assert.Empty(vm.SavedDecks);
        Assert.Empty(new DeckStore(_dir).LoadAll());
    }

    [Fact]
    public void Filtres_de_la_liste()
    {
        if (Kb() is not { } kb) return;
        var vm = Make(kb);
        var b = vm.Browser;
        b.Element = b.ElementOptions.First(o => o.Id == "fire");
        Assert.NotEmpty(b.Items);
        Assert.All(b.Items, i => Assert.Equal("fire", i.Card.Element));
        b.Search = "Esprit";
        // La recherche porte aussi sur les capacités et les talents (phase 12) : le nom seul ne suffit plus à expliquer un résultat.
        Assert.NotEmpty(b.Items);
        Assert.Contains(b.Items, i => i.Name.Contains("Esprit"));
        Assert.All(b.Items, i => Assert.True(i.Matches("Esprit", "fire", null)));
    }
}
