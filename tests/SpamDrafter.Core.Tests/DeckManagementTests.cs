using SpamDrafter.App.ViewModels;
using SpamDrafter.Core.Decks;
using SpamDrafter.Core.Knowledge;

namespace SpamDrafter.Core.Tests;

/// <summary>Phase 12, lot A : noms uniques (D-072), annuler/rétablir, alerte, dupliquer, favoris, étiquettes, tri, copier/coller.</summary>
public class DeckManagementTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "spamdrafter-mgmt-" + Guid.NewGuid());

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
        return new DeckBuilderViewModel(new CardBrowser(items, kb), items, kb, new DeckLibrary(new DeckStore(_dir))) { AllowIncompleteDecks = true };
    }

    private static void Add(DeckBuilderViewModel vm, int index)
    {
        vm.Browser.Selected = vm.Browser.Items[index];
        vm.AddCommand.Execute(null);
    }

    private static void Fill(DeckBuilderViewModel vm, string name, int from = 0, int count = 4)
    {
        for (var i = 0; i < count; i++) Add(vm, from + i);
        vm.Name = name;
        vm.SaveCommand.Execute(null);
    }

    // ----- Noms uniques (D-072) -----

    [Fact]
    public void Deux_decks_ne_peuvent_pas_avoir_le_meme_nom_meme_sans_accent_ni_casse()
    {
        if (Kb() is not { } kb) return;
        var vm = Make(kb);
        Fill(vm, "Mon Éclair");
        vm.NewCommand.Execute(null);
        Add(vm, 5);
        vm.Name = "mon eclair";
        Assert.Contains(vm.Problems, p => p.Contains("déjà ce nom"));
        vm.SaveCommand.Execute(null);
        Assert.Contains("déjà", vm.Status);
        Assert.Single(vm.SavedDecks);
    }

    [Fact]
    public void Un_deck_peut_etre_resauvegarde_sous_son_propre_nom()
    {
        if (Kb() is not { } kb) return;
        var vm = Make(kb);
        Fill(vm, "Mon deck");
        vm.Name = "Mon deck";
        vm.SaveCommand.Execute(null);
        Assert.Contains("sauvegardé", vm.Status);
        Assert.Single(vm.SavedDecks);
    }

    [Fact]
    public void Les_doublons_existants_recoivent_un_suffixe_au_chargement_sans_rien_supprimer()
    {
        if (Kb() is not { } kb) return;
        var store = new DeckStore(_dir);
        foreach (var i in new[] { 0, 1, 2 })
            store.Save(new Deck { Name = i == 2 ? "MON DÉCK" : "Mon déck", GameVersion = "1.30", Slots = { new DeckSlot(kb.Cards[i].Id, null) } });
        var lib = new DeckLibrary(store);
        Assert.Equal(3, lib.Decks.Count);
        Assert.Equal(2, lib.RenamedDuplicates);
        Assert.Equal(3, lib.Decks.Select(d => DeckNames.Key(d.Name)).Distinct().Count());
        Assert.Contains(lib.Decks, d => d.Name.EndsWith("(2)"));
        // Le renommage est enregistré : au prochain chargement, plus rien à corriger.
        Assert.Equal(0, new DeckLibrary(store).RenamedDuplicates);
    }

    [Fact]
    public void MakeUnique_trouve_un_nom_libre()
    {
        var a = new Deck { Name = "Test" };
        var b = new Deck { Name = "Test (2)" };
        Assert.Equal("test (3)", DeckNames.MakeUnique(new[] { a, b }, "test", Guid.Empty));
        Assert.Equal("Autre", DeckNames.MakeUnique(new[] { a, b }, "Autre", Guid.Empty));
        Assert.Equal("Test", DeckNames.MakeUnique(new[] { a, b }, "Test", a.Id));
    }

    // ----- Deck généré renommable -----

    [Fact]
    public void Un_deck_genere_renomme_garde_son_origine()
    {
        if (Kb() is not { } kb) return;
        new DeckStore(_dir).Save(new Deck
        {
            Name = "Généré — A, B", Origin = DeckOrigin.Generated, GameVersion = "1.30",
            Slots = { new DeckSlot(kb.Cards[0].Id, null), new DeckSlot(kb.Cards[1].Id, null) },
        });
        var vm = Make(kb);
        vm.SelectedSaved = vm.SavedDecks[0];
        vm.Name = "Mon deck préféré";
        vm.SaveCommand.Execute(null);
        var reloaded = new DeckStore(_dir).LoadAll();
        Assert.Equal("Mon deck préféré", Assert.Single(reloaded).Name);
        Assert.Equal(DeckOrigin.Generated, reloaded[0].Origin);
    }

    // ----- Version de SPAM de création conservée -----

    [Fact]
    public void La_version_de_creation_ne_change_pas_quand_on_modifie_le_deck()
    {
        if (Kb() is not { } kb) return;
        new DeckStore(_dir).Save(new Deck { Name = "Ancien", GameVersion = "1.20", Slots = { new DeckSlot(kb.Cards[0].Id, null) } });
        var vm = Make(kb);
        vm.SelectedSaved = vm.SavedDecks[0];
        Add(vm, 3);
        vm.SaveCommand.Execute(null);
        Assert.Equal("1.20", Assert.Single(new DeckStore(_dir).LoadAll()).GameVersion);
    }

    // ----- Annuler / rétablir -----

    [Fact]
    public void Annuler_et_retablir_rejouent_les_modifications()
    {
        if (Kb() is not { } kb) return;
        var vm = Make(kb);
        Add(vm, 0); Add(vm, 1); Add(vm, 2);
        Assert.Equal(3, vm.Slots.Count);
        vm.UndoCommand.Execute(null);
        Assert.Equal(2, vm.Slots.Count);
        vm.UndoCommand.Execute(null);
        Assert.Single(vm.Slots);
        vm.RedoCommand.Execute(null);
        Assert.Equal(2, vm.Slots.Count);
        // Une nouvelle modification efface ce qui pouvait être rétabli.
        Add(vm, 7);
        Assert.False(vm.RedoCommand.CanExecute(null));
    }

    [Fact]
    public void Annuler_restaure_aussi_un_talent_et_regroupe_la_saisie_du_nom()
    {
        if (Kb() is not { } kb) return;
        var vm = Make(kb);
        Add(vm, 0);
        var before = vm.Slots[0].Talent.Id;
        vm.Slots[0].Talent = vm.Slots[0].TalentOptions[2];
        vm.UndoCommand.Execute(null);
        Assert.Equal(before, vm.Slots[0].Talent.Id);

        vm.Name = "A"; vm.Name = "AB"; vm.Name = "ABC";   // trois frappes = une seule étape
        vm.UndoCommand.Execute(null);
        Assert.Equal("Nouveau deck", vm.Name);
    }

    [Fact]
    public void Le_bouton_annuler_n_est_actif_que_s_il_y_a_quelque_chose_a_annuler()
    {
        if (Kb() is not { } kb) return;
        var vm = Make(kb);
        Assert.False(vm.UndoCommand.CanExecute(null));
        Add(vm, 0);
        Assert.True(vm.UndoCommand.CanExecute(null));
        vm.SaveCommand.Execute(null);
        vm.NewCommand.Execute(null);   // un deck neuf repart d'un historique vide
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    // ----- Modifications non sauvegardées -----

    [Fact]
    public void Un_deck_neuf_ou_sauvegarde_n_a_pas_de_modification_en_attente()
    {
        if (Kb() is not { } kb) return;
        var vm = Make(kb);
        Assert.False(vm.IsDirty);
        Add(vm, 0);
        Assert.True(vm.IsDirty);
        vm.SaveCommand.Execute(null);
        Assert.False(vm.IsDirty);
        vm.Slots[0].Talent = vm.Slots[0].TalentOptions[2];
        Assert.True(vm.IsDirty);
        vm.UndoCommand.Execute(null);
        Assert.False(vm.IsDirty);   // revenu à l'état sauvegardé
    }

    [Fact]
    public void Changer_de_deck_avec_des_modifications_demande_confirmation_et_peut_etre_annule()
    {
        if (Kb() is not { } kb) return;
        var vm = Make(kb);
        Fill(vm, "Premier");
        vm.NewCommand.Execute(null);
        Fill(vm, "Second", from: 10);
        var first = vm.SavedDecks.First(d => d.Name == "Premier");
        Add(vm, 20);   // modification non sauvegardée sur « Second »... mais le deck est déjà complet
        Assert.Equal(4, vm.Slots.Count);
        vm.Name = "Second modifié";
        Assert.True(vm.IsDirty);

        vm.SelectedSaved = first;
        Assert.True(vm.ConfirmVisible);
        Assert.Equal("Second modifié", vm.Name);    // rien n'a changé

        vm.ConfirmCancelCommand.Execute(null);
        Assert.False(vm.ConfirmVisible);
        Assert.Equal("Second modifié", vm.Name);

        vm.SelectedSaved = first;
        vm.ConfirmDiscardCommand.Execute(null);      // abandonner les modifications
        Assert.Equal("Premier", vm.Name);
        Assert.False(vm.IsDirty);
        Assert.Contains(vm.SavedDecks, d => d.Name == "Second");   // l'ancien nom est toujours enregistré
    }

    [Fact]
    public void La_confirmation_peut_sauvegarder_puis_continuer()
    {
        if (Kb() is not { } kb) return;
        var vm = Make(kb);
        Fill(vm, "Premier");
        vm.NewCommand.Execute(null);
        Fill(vm, "Second", from: 10);
        vm.Name = "Second v2";
        vm.NewCommand.Execute(null);
        Assert.True(vm.ConfirmVisible);
        Assert.True(vm.ConfirmCanSave);
        vm.ConfirmSaveCommand.Execute(null);
        Assert.False(vm.ConfirmVisible);
        Assert.Equal("Nouveau deck", vm.Name);
        Assert.Contains(vm.SavedDecks, d => d.Name == "Second v2");
    }

    [Fact]
    public void La_modification_en_cours_ne_touche_pas_au_deck_de_la_liste_avant_la_sauvegarde()
    {
        if (Kb() is not { } kb) return;
        var vm = Make(kb);
        Fill(vm, "Deck");
        var inList = vm.SavedDecks[0];
        vm.Slots.RemoveAt(0);
        vm.Name = "Autre nom";
        Assert.Equal("Deck", inList.Name);
        Assert.Equal(4, inList.Slots.Count);
    }

    [Fact]
    public void Ouvrir_un_deck_depuis_le_drafter_demande_confirmation_si_modifications()
    {
        if (Kb() is not { } kb) return;
        var vm = Make(kb);
        Fill(vm, "Un");
        vm.NewCommand.Execute(null);
        Add(vm, 30);
        var other = vm.SavedDecks[0];
        vm.RequestOpen(other);
        Assert.True(vm.ConfirmVisible);
    }

    // ----- Dupliquer -----

    [Fact]
    public void Dupliquer_cree_une_copie_ouverte_sans_toucher_a_l_original()
    {
        if (Kb() is not { } kb) return;
        var vm = Make(kb);
        Fill(vm, "Original");
        vm.DuplicateCommand.Execute(null);
        Assert.Equal(2, vm.SavedDecks.Count);
        Assert.Equal("Original (copie)", vm.Name);
        Assert.Equal(4, vm.Slots.Count);
        vm.DuplicateCommand.Execute(null);
        Assert.Contains(vm.SavedDecks, d => d.Name == "Original (copie) (copie)");
        Assert.Equal(3, new DeckStore(_dir).LoadAll().Count);
    }

    // ----- Favoris et étiquettes -----

    [Fact]
    public void Le_favori_d_un_deck_sauvegarde_s_enregistre_tout_de_suite()
    {
        if (Kb() is not { } kb) return;
        var vm = Make(kb);
        Fill(vm, "Deck");
        vm.ToggleFavoriteCommand.Execute(null);
        Assert.True(Assert.Single(new DeckStore(_dir).LoadAll()).Favorite);
        Assert.False(vm.IsDirty);
        vm.ToggleFavoriteCommand.Execute(null);
        Assert.False(Assert.Single(new DeckStore(_dir).LoadAll()).Favorite);
    }

    [Fact]
    public void Les_etiquettes_se_sauvegardent_et_servent_de_filtre()
    {
        if (Kb() is not { } kb) return;
        var vm = Make(kb);
        Add(vm, 0); vm.Name = "A"; vm.TagsText = "ranked, essai , ranked";
        vm.SaveCommand.Execute(null);
        vm.NewCommand.Execute(null);
        Add(vm, 5); vm.Name = "B"; vm.TagsText = "essai";
        vm.SaveCommand.Execute(null);
        vm.NewCommand.Execute(null);
        Add(vm, 9); vm.Name = "C";
        vm.SaveCommand.Execute(null);

        Assert.Equal(new[] { "ranked", "essai" }, new DeckStore(_dir).LoadAll().Single(d => d.Name == "A").Tags);
        Assert.Equal(new[] { "", "essai", "ranked" }, vm.TagOptions.Select(o => o.Id));
        vm.TagFilter = vm.TagOptions.First(o => o.Id == "essai");
        Assert.Equal(new[] { "A", "B" }, vm.SavedDecks.Select(d => d.Name));
        vm.TagFilter = vm.TagOptions.First(o => o.Id == "ranked");
        Assert.Equal(new[] { "A" }, vm.SavedDecks.Select(d => d.Name));
    }

    // ----- Liste : recherche et tri -----

    [Fact]
    public void La_liste_se_filtre_par_nom_sans_accent_par_favori_et_par_origine()
    {
        if (Kb() is not { } kb) return;
        var store = new DeckStore(_dir);
        store.Save(new Deck { Name = "Éclair rouge", Favorite = true, GameVersion = "1.30", Slots = { new DeckSlot(kb.Cards[0].Id, null) } });
        store.Save(new Deck { Name = "Brume", Origin = DeckOrigin.Generated, GameVersion = "1.30", Slots = { new DeckSlot(kb.Cards[1].Id, null) } });
        var vm = Make(kb);
        Assert.Equal(2, vm.SavedDecks.Count);
        vm.DeckFilter = "eclair";
        Assert.Equal("Éclair rouge", Assert.Single(vm.SavedDecks).Name);
        vm.DeckFilter = "";
        vm.FavoritesOnly = true;
        Assert.Equal("Éclair rouge", Assert.Single(vm.SavedDecks).Name);
        vm.FavoritesOnly = false;
        vm.OriginFilter = vm.OriginOptions.First(o => o.Id == "generated");
        Assert.Equal("Brume", Assert.Single(vm.SavedDecks).Name);
        vm.ClearDeckFiltersCommand.Execute(null);
        Assert.Equal(2, vm.SavedDecks.Count);
        // Recherche par le nom d'une carte du deck.
        vm.DeckFilter = kb.CardName(kb.Cards[1]);
        Assert.Equal("Brume", Assert.Single(vm.SavedDecks).Name);
    }

    [Fact]
    public void Tri_par_favoris_et_par_modification_recente()
    {
        if (Kb() is not { } kb) return;
        var store = new DeckStore(_dir);
        store.Save(new Deck { Name = "A", GameVersion = "1.30", ModifiedUtc = new DateTime(2026, 1, 1), Slots = { new DeckSlot(kb.Cards[0].Id, null) } });
        store.Save(new Deck { Name = "B", Favorite = true, GameVersion = "1.30", ModifiedUtc = new DateTime(2026, 1, 3), Slots = { new DeckSlot(kb.Cards[1].Id, null) } });
        store.Save(new Deck { Name = "C", GameVersion = "1.30", ModifiedUtc = new DateTime(2026, 1, 2), Slots = { new DeckSlot(kb.Cards[2].Id, null) } });
        var vm = Make(kb);
        Assert.Equal(new[] { "A", "B", "C" }, vm.SavedDecks.Select(d => d.Name));
        vm.Sort = vm.SortOptions.First(o => o.Id == "favorites");
        Assert.Equal("B", vm.SavedDecks[0].Name);
        vm.Sort = vm.SortOptions.First(o => o.Id == "recent");
        Assert.Equal(new[] { "B", "C", "A" }, vm.SavedDecks.Select(d => d.Name));
    }

    // ----- Copier / coller -----

    [Fact]
    public void Un_deck_copie_puis_colle_donne_le_meme_deck()
    {
        if (Kb() is not { } kb) return;
        var vm = Make(kb);
        for (var i = 0; i < 4; i++) Add(vm, i * 7);
        vm.Slots[1].Talent = vm.Slots[1].TalentOptions[3];
        vm.Slots[2].Talent = vm.Slots[2].TalentOptions[0];   // aucun talent
        vm.Name = "Mon échange";
        string? copied = null;
        vm.ClipboardWriteRequested += t => copied = t;
        vm.CopyTextCommand.Execute(null);
        Assert.NotNull(copied);
        Assert.StartsWith("SpamDrafter · Mon échange", copied);

        var other = Make(kb);
        other.ImportText(copied!);
        Assert.Equal("Mon échange", other.Name);
        Assert.Equal(vm.Slots.Select(s => (s.Card.Card.Id, s.Talent.Id)), other.Slots.Select(s => (s.Card.Card.Id, s.Talent.Id)));
        Assert.True(other.IsDirty);   // collé mais pas encore sauvegardé
        other.SaveCommand.Execute(null);
        Assert.Single(other.SavedDecks);
    }

    [Fact]
    public void Le_collage_tolere_accents_casse_et_refuse_les_cartes_inconnues()
    {
        if (Kb() is not { } kb) return;
        var card = kb.Cards[0];
        var name = kb.CardName(card).ToUpperInvariant();
        var ok = DeckText.Parse($"{name} — aucun talent", kb);
        Assert.True(ok.Ok);
        Assert.Equal(card.Id, ok.Deck!.Slots[0].CardId);
        Assert.Null(ok.Deck.Slots[0].TalentId);

        var bad = DeckText.Parse("Carte qui n'existe pas — Rien", kb);
        Assert.False(bad.Ok);
        Assert.Contains(bad.Problems, p => p.Contains("Carte inconnue"));
        Assert.False(DeckText.Parse("", kb).Ok);

        var vm = Make(kb);
        vm.ImportText("Carte qui n'existe pas");
        Assert.Contains("Collage impossible", vm.Status);
        Assert.Empty(vm.Slots);
    }

    [Fact]
    public void Le_collage_refuse_plus_de_4_cartes_et_les_doublons()
    {
        if (Kb() is not { } kb) return;
        var five = string.Join("\n", kb.Cards.Take(5).Select(c => kb.CardName(c)));
        Assert.Contains(DeckText.Parse(five, kb).Problems, p => p.Contains("au plus"));
        var twice = kb.CardName(kb.Cards[0]) + "\n" + kb.CardName(kb.Cards[0]);
        Assert.Contains(DeckText.Parse(twice, kb).Problems, p => p.Contains("double"));
    }

    // ----- Compatibilité avec les anciens fichiers -----

    [Fact]
    public void Un_ancien_fichier_sans_favori_ni_etiquette_se_charge()
    {
        if (Kb() is not { } kb) return;
        Directory.CreateDirectory(_dir);
        var id = Guid.NewGuid();
        File.WriteAllText(Path.Combine(_dir, id + ".json"),
            $"{{\"Id\":\"{id}\",\"Name\":\"Ancien\",\"Origin\":\"Manual\",\"GameVersion\":\"1.30\",\"Slots\":[{{\"CardId\":\"{kb.Cards[0].Id}\",\"TalentId\":null}}]}}");
        var deck = Assert.Single(new DeckStore(_dir).LoadAll());
        Assert.False(deck.Favorite);
        Assert.Empty(deck.Tags);
        Assert.Null(deck.ModifiedUtc);
        Assert.Single(Make(kb).SavedDecks);
    }

    [Fact]
    public void La_sauvegarde_enregistre_la_date_de_modification()
    {
        if (Kb() is not { } kb) return;
        var vm = Make(kb);
        Fill(vm, "Daté");
        Assert.NotNull(new DeckStore(_dir).LoadAll()[0].ModifiedUtc);
    }

    // ----- Deck généré : composition verrouillée -----

    private DeckBuilderViewModel MakeWithGenerated(KnowledgeBase kb)
    {
        new DeckStore(_dir).Save(new Deck
        {
            Name = "Généré — test", Origin = DeckOrigin.Generated, GameVersion = "1.30",
            Slots = { new DeckSlot(kb.Cards[0].Id, kb.Cards[0].Talents[0]), new DeckSlot(kb.Cards[1].Id, kb.Cards[1].Talents[0]) },
        });
        var vm = Make(kb);
        vm.SelectedSaved = vm.SavedDecks[0];
        return vm;
    }

    [Fact]
    public void Un_deck_genere_ne_peut_pas_changer_de_cartes_ni_de_talents()
    {
        if (Kb() is not { } kb) return;
        var vm = MakeWithGenerated(kb);
        Assert.True(vm.IsLocked);
        Assert.False(vm.CanEditCards);
        Assert.All(vm.Slots, s => Assert.False(s.IsEditable));

        Add(vm, 5);
        Assert.Equal(2, vm.Slots.Count);
        Assert.Contains("générateur", vm.Status);

        vm.RemoveCommand.Execute(vm.Slots[0]);
        Assert.Equal(2, vm.Slots.Count);

        var before = vm.Slots[0].Talent.Id;
        vm.Slots[0].Talent = vm.Slots[0].TalentOptions[2];
        Assert.Equal(before, vm.Slots[0].Talent.Id);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Un_deck_genere_reste_renommable_etiquetable_et_favori()
    {
        if (Kb() is not { } kb) return;
        var vm = MakeWithGenerated(kb);
        vm.Name = "Mon deck";
        vm.TagsText = "ranked";
        vm.SaveCommand.Execute(null);
        vm.ToggleFavoriteCommand.Execute(null);
        var saved = Assert.Single(new DeckStore(_dir).LoadAll());
        Assert.Equal("Mon deck", saved.Name);
        Assert.Equal(new[] { "ranked" }, saved.Tags);
        Assert.True(saved.Favorite);
        Assert.Equal(DeckOrigin.Generated, saved.Origin);
        Assert.Equal(2, saved.Slots.Count);
    }

    [Fact]
    public void La_copie_d_un_deck_genere_est_un_deck_a_soi_modifiable()
    {
        if (Kb() is not { } kb) return;
        var vm = MakeWithGenerated(kb);
        vm.DuplicateCommand.Execute(null);
        Assert.False(vm.IsLocked);
        Assert.All(vm.Slots, s => Assert.True(s.IsEditable));
        Add(vm, 9);
        Assert.Equal(3, vm.Slots.Count);
        // L'original reste verrouillé.
        vm.SelectedSaved = vm.SavedDecks.First(d => d.Origin == DeckOrigin.Generated);
        vm.ConfirmDiscardCommand.Execute(null);   // la copie a une modification non sauvegardée : on l'abandonne
        Assert.True(vm.IsLocked);
    }

    [Fact]
    public void Un_nouveau_deck_ou_un_deck_colle_n_est_pas_verrouille()
    {
        if (Kb() is not { } kb) return;
        var vm = MakeWithGenerated(kb);
        vm.NewCommand.Execute(null);
        Assert.False(vm.IsLocked);
        Add(vm, 3);
        Assert.Single(vm.Slots);
    }
}
