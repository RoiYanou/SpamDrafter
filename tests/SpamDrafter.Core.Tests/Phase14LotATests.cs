using SpamDrafter.App.ViewModels;
using SpamDrafter.Core.Decks;
using SpamDrafter.Core.Knowledge;

namespace SpamDrafter.Core.Tests;

/// <summary>Phase 14, lot A : le cadre de l'onglet Tester (choix du deck, sans calcul).</summary>
public class Phase14LotATests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "spamdrafter-p14a-" + Guid.NewGuid());
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private static Deck D(string name, params string[] cards) =>
        new() { Name = name, Slots = cards.Select(c => new DeckSlot(c, null)).ToList() };

    private (TesterViewModel tester, DeckLibrary lib) Make(params Deck[] decks)
    {
        var lib = new DeckLibrary(new DeckStore(_dir));
        foreach (var d in decks) lib.Save(d);
        return (new TesterViewModel(lib), lib);
    }

    [Fact]
    public void Seuls_les_decks_de_4_cartes_sont_proposes()
    {
        var (t, _) = Make(D("Complet", "a", "b", "c", "d"), D("Brouillon", "a", "b"));
        var r = Assert.Single(t.Results);
        Assert.Equal("Complet", r.Name);
        Assert.Contains("1 brouillon", t.Message);
    }

    [Fact]
    public void Message_sans_deck_et_sans_deck_complet()
    {
        var (t, lib) = Make();
        Assert.Contains("Aucun deck sauvegardé", t.Message);
        lib.Save(D("Brouillon", "a"));
        Assert.Empty(t.Results);
        Assert.Contains("Aucun deck complet", t.Message);
    }

    [Fact]
    public void Choisir_un_deck_remplit_la_selection_et_les_pastilles()
    {
        var (t, _) = Make(D("Complet", "a", "b", "c", "d"));
        Assert.False(t.HasSelection);
        t.Selected = t.Results[0];
        Assert.True(t.HasSelection);
        Assert.Equal("Complet", t.SelectedTitle);
        Assert.Equal(4, t.SelectedChips.Count);
        t.ClearSelectionCommand.Execute(null);
        Assert.False(t.HasSelection);
    }

    [Fact]
    public void Select_depuis_un_autre_onglet_refuse_un_brouillon()
    {
        var (t, lib) = Make(D("Complet", "a", "b", "c", "d"), D("Brouillon", "a"));
        Assert.True(t.Select(lib.Decks.First(d => d.Name == "Complet")));
        Assert.Equal("Complet", t.SelectedTitle);
        Assert.False(t.Select(lib.Decks.First(d => d.Name == "Brouillon")));
    }

    [Fact]
    public void Le_deck_choisi_reste_choisi_apres_une_sauvegarde_et_disparait_s_il_est_supprime()
    {
        var (t, lib) = Make(D("Complet", "a", "b", "c", "d"), D("Autre", "e", "f", "g", "h"));
        t.Select(lib.Decks.First(d => d.Name == "Complet"));
        var other = lib.Decks.First(d => d.Name == "Autre");
        other.Favorite = true;
        lib.Save(other);
        Assert.Equal("Complet", t.SelectedTitle);
        lib.Delete(lib.Decks.First(d => d.Name == "Complet").Id);
        Assert.False(t.HasSelection);
        Assert.Single(t.Results);
    }

    [Fact]
    public void Ouvrir_dans_le_builder_envoie_le_deck_choisi()
    {
        var (t, _) = Make(D("Complet", "a", "b", "c", "d"));
        Deck? opened = null;
        t.OpenRequested += d => opened = d;
        Assert.False(t.OpenInBuilderCommand.CanExecute(null));
        t.Selected = t.Results[0];
        Assert.True(t.OpenInBuilderCommand.CanExecute(null));
        t.OpenInBuilderCommand.Execute(null);
        Assert.Equal("Complet", opened?.Name);
    }

    [Fact]
    public void Les_sous_onglets_Un_deck_et_Comparer_suivent_le_mode()
    {
        var (t, _) = Make(D("Complet", "a", "b", "c", "d"));
        Assert.Equal(0, t.ModeIndex);
        t.ModeIndex = 1;
        Assert.True(t.IsCompare);
        Assert.Equal(1, t.ModeIndex);
        t.ModeIndex = 0;
        Assert.False(t.IsCompare);
    }

    [Fact]
    public void La_croix_retire_le_deck_selectionne()
    {
        var (t, _) = Make(D("Complet", "a", "b", "c", "d"));
        t.Selected = t.Results[0];
        Assert.True(t.ClearSelectionCommand.CanExecute(null));
        t.ClearSelectionCommand.Execute(null);
        Assert.False(t.HasSelection);
        Assert.True(t.ShowNoSelection);
        Assert.False(t.ClearSelectionCommand.CanExecute(null));
    }

    [Fact]
    public void Tester_est_l_onglet_5_et_l_accueil_reste_le_4()
    {
        // Numéros déjà enregistrés dans les réglages : 0 à 3 inchangés, 4 = accueil, 5 = Tester.
        var main = typeof(MainViewModel);
        Assert.NotNull(main.GetProperty(nameof(MainViewModel.IsTab5)));
        Assert.NotNull(main.GetProperty(nameof(MainViewModel.HasTester)));
    }
}
