using SpamDrafter.App.ViewModels;
using SpamDrafter.Core.Collection;
using SpamDrafter.Core.Decks;
using SpamDrafter.Core.Knowledge;
using SpamDrafter.Core.Settings;

namespace SpamDrafter.Core.Tests;

/// <summary>Phase 15 : collection du joueur, interrupteur « possédés / toutes », importation complète et précise.</summary>
public class Phase15CollectionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "spamdrafter-p15-" + Guid.NewGuid());
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

    private static readonly string[] Talents = { "t1", "t2", "t3" };

    // ----- Règles de possession (sans données du jeu) -----

    [Fact]
    public void Talent_2_signifie_que_le_talent_1_est_possede_aussi()
    {
        var c = new PlayerCollection();
        c.SetLevel("a", 2);
        Assert.Equal(new[] { "t1", "t2" }, c.OwnedTalents("a", Talents));
        Assert.True(c.OwnsSlot("a", "t1", Talents));
        Assert.True(c.OwnsSlot("a", "t2", Talents));
        Assert.False(c.OwnsSlot("a", "t3", Talents));
        c.SetLevel("a", 3);
        Assert.True(c.OwnsSlot("a", "t3", Talents));
    }

    [Fact]
    public void Une_carte_sans_talent_ou_non_possedee()
    {
        var c = new PlayerCollection();
        c.SetLevel("a", 0);
        Assert.True(c.Owns("a"));
        Assert.Empty(c.OwnedTalents("a", Talents));
        Assert.True(c.OwnsSlot("a", null, Talents));      // « aucun talent » est toujours possédé
        Assert.False(c.OwnsSlot("a", "t1", Talents));
        Assert.False(c.Owns("b"));
        Assert.Equal(PlayerCollection.NotOwned, c.LevelOf("b"));
        Assert.False(c.OwnsSlot("b", null, Talents));
        c.SetLevel("a", PlayerCollection.NotOwned);
        Assert.False(c.Owns("a"));
    }

    [Fact]
    public void Un_deck_est_possede_si_ses_cartes_et_ses_talents_le_sont()
    {
        var c = new PlayerCollection();
        c.SetLevel("a", 1); c.SetLevel("b", 3); c.SetLevel("c", 0); c.SetLevel("d", 2);
        IReadOnlyList<string>? TalentsOf(string id) => id == "z" ? null : Talents;
        Deck Make(params DeckSlot[] s) => new Deck { Slots = s.ToList() };
        Assert.True(c.OwnsDeck(Make(new DeckSlot("a", "t1"), new DeckSlot("b", "t3"), new DeckSlot("c", null), new DeckSlot("d", "t2")), TalentsOf));
        Assert.False(c.OwnsDeck(Make(new DeckSlot("a", "t2"), new DeckSlot("b", "t3"), new DeckSlot("c", null), new DeckSlot("d", "t2")), TalentsOf));   // talent 2 de a non possédé
        Assert.False(c.OwnsDeck(Make(new DeckSlot("a", "t1"), new DeckSlot("e", null)), TalentsOf));                                   // carte e non possédée
        Assert.False(c.OwnsDeck(Make(new DeckSlot("z", null)), TalentsOf));                                                   // carte inconnue
        Assert.False(c.OwnsDeck(Make(), TalentsOf));
    }

    [Fact]
    public void La_collection_fait_l_aller_retour_et_un_fichier_abime_donne_une_collection_vide()
    {
        var path = Path.Combine(_dir, "collection.json");
        var c = new PlayerCollection { GameVersion = "1.30" };
        c.SetLevel("a", 2); c.SetLevel("b", 0);
        c.Draft = new ImportDraft { Position = 5, Answers = { ["a"] = 2, ["x"] = -1 } };
        Assert.True(c.TrySave(path));
        var back = PlayerCollection.Load(path);
        Assert.Equal(2, back.LevelOf("a")); Assert.Equal(0, back.LevelOf("b"));
        Assert.Equal(5, back.Draft!.Position); Assert.Equal(-1, back.Draft.Answers["x"]);
        File.WriteAllText(path, "{ pas du json");
        Assert.True(PlayerCollection.Load(path).IsEmpty);
        Assert.True(PlayerCollection.Load(Path.Combine(_dir, "absent.json")).IsEmpty);
    }

    [Fact]
    public void Un_niveau_trop_haut_est_ramene_a_trois_et_l_empreinte_suit_la_collection()
    {
        var c = new PlayerCollection();
        c.SetLevel("a", 9);
        Assert.Equal(3, c.LevelOf("a"));
        var f1 = c.Fingerprint();
        c.SetLevel("a", 2);
        Assert.NotEqual(f1, c.Fingerprint());
    }

    // ----- Interface (avec les vraies données du jeu) -----

    /// <summary>Importation précise : vérifier la liste des changements puis les enregistrer.</summary>
    private static void Commit(CollectionViewModel c) { c.ReviewCommand.Execute(null); c.ConfirmPreciseCommand.Execute(null); }

    private sealed record World(KnowledgeBase Kb, List<CardItem> Items, CollectionViewModel Col, AppSettings Settings, string Path);

    private World? Make()
    {
        if (Kb() is not { } kb) return null;
        var items = kb.Cards.Select(c => new CardItem(c, kb)).ToList();
        var settings = new AppSettings { OwnedOnly = false };   // le défaut de l'application est « possédés » ; les tests partent de « toutes »
        var path = Path.Combine(_dir, "collection.json");
        var col = new CollectionViewModel(kb, items, path, settings, () => { });
        col.Start();   // comme l'application au démarrage : les étoiles reflètent l'état enregistré
        return new World(kb, items, col, settings, path);
    }

    [Fact]
    public void L_interrupteur_filtre_la_liste_des_cartes()
    {
        if (Make() is not { } w) return;
        var browser = new CardBrowser(w.Items, w.Kb) { Collection = w.Col };
        var all = browser.Items.Count;
        Assert.True(all > 10);
        w.Col.OwnedOnly = true;
        Assert.Empty(browser.Items);                       // collection vide : rien
        w.Col.OpenCommand.Execute(null); w.Col.PreciseCommand.Execute(null);
        w.Col.PreciseSelected = w.Col.Rows[0];
        w.Col.SetPreciseCommand.Execute("1"); Commit(w.Col);
        Assert.Single(browser.Items);
        Assert.Equal(w.Items.OrderBy(i => i.Card.Order).First().Card.Id, browser.Items[0].Card.Id);
        w.Col.OwnedOnly = false;
        Assert.Equal(all, browser.Items.Count);            // « toutes » : comme avant
    }

    [Fact]
    public void Importation_precise_talent_2_donne_aussi_le_talent_1_et_se_recharge()
    {
        if (Make() is not { } w) return;
        var card = w.Items.First(i => i.Card.Talents.Count >= 3);
        w.Col.PreciseCommand.Execute(null);
        w.Col.Search = card.Name;
        var row = w.Col.Rows.First(r => r.Item.Card.Id == card.Card.Id);
        w.Col.PreciseSelected = row;
        w.Col.SetPreciseCommand.Execute("2"); Commit(w.Col);
        Assert.Equal(2, w.Col.Data.LevelOf(card.Card.Id));
        w.Col.OwnedOnly = true;
        Assert.Equal(card.Card.Talents.Take(2), w.Col.AllowedTalents(card));
        // Rechargée depuis le fichier.
        var again = new CollectionViewModel(w.Kb, w.Items, w.Path, w.Settings, () => { });
        Assert.Equal(2, again.Data.LevelOf(card.Card.Id));
        // « Carte non possédée » la retire.
        w.Col.SetPreciseCommand.Execute("-1"); Commit(w.Col);
        Assert.False(w.Col.Data.Owns(card.Card.Id));
    }

    [Fact]
    public void Importation_complete_continuer_plus_tard_puis_terminer()
    {
        if (Make() is not { } w) return;
        var col = w.Col;
        var ordered = w.Items.OrderBy(i => i.Card.Order).ToList();
        col.OpenCommand.Execute(null);
        col.FullCommand.Execute(null);
        Assert.True(col.IsFull);
        Assert.Equal(ordered[0].Card.Id, col.Current!.Card.Id);   // dans l'ordre du jeu
        col.AnswerCommand.Execute("3");                           // carte 1 : talent 3 (au plus ses talents)
        col.AnswerCommand.Execute("-1");                          // carte 2 : non possédée
        col.AnswerCommand.Execute("0");                           // carte 3 : sans talent
        Assert.True(col.Data.IsEmpty);                            // la collection ne change qu'à la fin
        col.LaterCommand.Execute(null);
        Assert.True(col.IsMenu);
        Assert.True(col.HasDraft);
        // Reprise (même après un redémarrage) : on retrouve la 4e carte.
        var again = new CollectionViewModel(w.Kb, w.Items, w.Path, w.Settings, () => { });
        again.OpenCommand.Execute(null); again.FullCommand.Execute(null);
        Assert.Equal(ordered[3].Card.Id, again.Current!.Card.Id);
        // Retour arrière : on revient à la 3e carte pour la corriger.
        again.BackCommand.Execute(null);
        Assert.Equal(ordered[2].Card.Id, again.Current!.Card.Id);
        again.AnswerCommand.Execute("1");
        // Toutes les autres : non possédées.
        while (again.IsFull) again.AnswerCommand.Execute("-1");
        Assert.True(again.IsMenu);
        Assert.False(again.HasDraft);
        Assert.Equal(2, again.Data.Count);
        Assert.Equal(Math.Min(3, ordered[0].Card.Talents.Count), again.Data.LevelOf(ordered[0].Card.Id));
        Assert.False(again.Data.Owns(ordered[1].Card.Id));
        Assert.Equal(Math.Min(1, ordered[2].Card.Talents.Count), again.Data.LevelOf(ordered[2].Card.Id));
    }

    [Fact]
    public void Importation_precise_demande_une_confirmation_avec_la_liste_des_changements()
    {
        if (Make() is not { } w) return;
        var card = w.Items.First(i => i.Card.Talents.Count >= 3);
        w.Col.Data.SetLevel(card.Card.Id, 1);
        w.Col.PreciseCommand.Execute(null);
        w.Col.Search = card.Name;
        w.Col.PreciseSelected = w.Col.Rows.First(r => r.Item.Card.Id == card.Card.Id);
        w.Col.SetPreciseCommand.Execute("3");
        Assert.Equal(1, w.Col.Data.LevelOf(card.Card.Id));        // rien n'est enregistré avant la confirmation
        Assert.True(w.Col.HasPending);
        w.Col.ReviewCommand.Execute(null);
        Assert.True(w.Col.IsPreciseConfirm);
        Assert.Single(w.Col.Changes);
        Assert.Contains(card.Name, w.Col.Changes[0]);
        Assert.Contains("talent 1", w.Col.Changes[0]); Assert.Contains("talent 3", w.Col.Changes[0]);
        w.Col.KeepEditingCommand.Execute(null);
        Assert.True(w.Col.IsPrecise);
        w.Col.SetPreciseCommand.Execute("1");                      // retour à l'état d'origine : plus rien à confirmer
        Assert.False(w.Col.HasPending);
        w.Col.SetPreciseCommand.Execute("-1");
        w.Col.ReviewCommand.Execute(null);
        w.Col.DiscardPendingCommand.Execute(null);                 // abandon : rien ne change
        Assert.Equal(1, w.Col.Data.LevelOf(card.Card.Id));
        Assert.False(w.Col.HasPending);
    }

    [Fact]
    public void Les_etoiles_de_talents_suivent_la_collection_et_les_talents_non_possedes_restent_visibles()
    {
        if (Make() is not { } w) return;
        var card = w.Items.First(i => i.Card.Talents.Count >= 3);
        Assert.Equal(card.Card.Talents.Count, card.Stars.Count);   // mode « toutes » : toutes les étoiles, toutes jaunes
        Assert.Same(card.Stars[0].Fill, card.Stars[^1].Fill);
        w.Col.Data.SetLevel(card.Card.Id, 2);
        w.Col.OwnedOnly = true;
        Assert.Equal(card.Card.Talents.Count, card.Stars.Count);
        Assert.Same(card.Stars[0].Fill, card.Stars[1].Fill);       // 2 talents possédés : jaunes
        Assert.NotSame(card.Stars[1].Fill, card.Stars[2].Fill);    // le 3e : gris
        // Fiche : les 3 talents sont listés, le 3e est grisé.
        var lib = new LibraryViewModel(new CardBrowser(w.Items, w.Kb), w.Kb, null, w.Items) { Collection = w.Col };
        lib.Browser.Selected = card;
        Assert.Equal(card.Card.Talents.Count, lib.Talents.Count);
        Assert.True(lib.Talents[1].Owned); Assert.False(lib.Talents[2].Owned);
        Assert.True(lib.Talents[2].Dim < 1);
        w.Col.OwnedOnly = false;
        Assert.Same(card.Stars[0].Fill, card.Stars[^1].Fill);      // de nouveau toutes jaunes
        Assert.All(lib.Talents, t => Assert.True(t.Owned));
    }

    [Fact]
    public void Le_deck_de_l_accueil_est_un_deck_visible_dans_le_builder()
    {
        if (Make() is not { } w) return;
        var lib = new DeckLibrary(new DeckStore(Path.Combine(_dir, "hdecks")));
        Deck Mk(string n, IEnumerable<CardItem> cs) => new() { Name = n, GameVersion = "1.30", Slots = cs.Select(c => new DeckSlot(c.Card.Id, null)).ToList() };
        var mine = w.Items.Take(4).ToList();
        lib.Save(Mk("Possédé", mine)); lib.Save(Mk("Pas possédé", w.Items.Skip(4).Take(4)));
        foreach (var c in mine) w.Col.Data.SetLevel(c.Card.Id, 0);
        var home = new HomeViewModel(w.Kb, w.Items, lib, _ => { }, _ => { }) { DeckFilter = w.Col.ShowsDeck };
        w.Col.Changed += home.Refresh;
        w.Col.OwnedOnly = true;
        Assert.Equal("Possédé", home.LastDeck!.Name);
        w.Col.OwnedOnly = false;
        Assert.NotNull(home.LastDeck);
    }

    [Fact]
    public void Accueil_invitation_a_importer_deck_par_mode_et_bonjour_stable()
    {
        if (Make() is not { } w) return;
        var lib = new DeckLibrary(new DeckStore(Path.Combine(_dir, "h2decks")));
        Deck Mk(string n, IEnumerable<CardItem> cs) => new() { Name = n, GameVersion = "1.30", Slots = cs.Select(c => new DeckSlot(c.Card.Id, null)).ToList() };
        var mine = w.Items.Take(4).ToList();
        lib.Save(Mk("Possédé", mine)); lib.Save(Mk("Autre", w.Items.Skip(4).Take(4)));
        var home = new HomeViewModel(w.Kb, w.Items, lib, _ => { }, _ => { })
        {
            DeckFilter = w.Col.ShowsDeck, OwnedMode = () => w.Col.OwnedOnly, NoCards = () => w.Col.OwnedOnly && w.Col.Data.IsEmpty,
        };
        w.Col.Changed += home.Refresh;
        var greeting = home.Greeting;
        w.Col.OwnedOnly = true;                                  // collection vide : invitation à importer
        Assert.True(home.ShowImportPrompt); Assert.False(home.ShowCreatePrompt); Assert.Null(home.LastDeck);
        foreach (var c in mine) w.Col.Data.SetLevel(c.Card.Id, 0);
        w.Col.OwnedOnly = false; w.Col.OwnedOnly = true;
        Assert.Equal("Possédé", home.LastDeck!.Name);
        Assert.False(home.ShowImportPrompt);
        w.Col.OwnedOnly = false;
        var allDeck = home.LastDeck!.Id;
        w.Col.OwnedOnly = true; w.Col.OwnedOnly = false;
        Assert.Equal(allDeck, home.LastDeck!.Id);                // le deck de chaque mode reste le même pendant la session
        Assert.Equal(greeting, home.Greeting);                   // le bonjour est le même pour les deux modes
    }

    [Fact]
    public void Le_tutoriel_est_en_diapositives_avec_le_numero_de_page()
    {
        if (Make() is not { } w) return;
        var c = w.Col;
        Assert.Equal(5, c.TutorialSteps.Count);                   // 6 étapes, les deux premières sur la même diapositive
        Assert.Equal(2, c.TutorialSteps[0].Lines.Count);
        Assert.StartsWith("Bonus", c.TutorialSteps[4].Lines[0]);
        c.OpenCommand.Execute(null); c.TutorialCommand.Execute(null);
        Assert.True(c.ShowTutorial);
        Assert.Equal("Page 1 / 5", c.PageText); Assert.False(c.CanPreviousPage);
        c.NextPageCommand.Execute(null);
        Assert.Equal("Page 2 / 5", c.PageText); Assert.True(c.CanPreviousPage);
        c.PreviousPageCommand.Execute(null);
        Assert.Equal("Page 1 / 5", c.PageText);
        for (var i = 0; i < 4; i++) c.NextPageCommand.Execute(null);
        Assert.True(c.IsLastPage); Assert.Equal("Terminer", c.NextLabel);
        c.NextPageCommand.Execute(null);                       // « Terminer » ferme le tutoriel
        Assert.False(c.ShowTutorial);
        c.TutorialCommand.Execute(null);
        Assert.Equal("Page 1 / 5", c.PageText);                // il se rouvre à la page 1
    }

    [Fact]
    public void Effacer_la_collection_exige_d_ecrire_confirmer()
    {
        if (Make() is not { } w) return;
        w.Col.Data.SetLevel(w.Items[0].Card.Id, 1);
        w.Col.ClearCommand.Execute(null);
        Assert.True(w.Col.ConfirmingClear);
        Assert.False(w.Col.ConfirmClearCommand.CanExecute(null));
        w.Col.ClearText = "confirm";
        w.Col.ConfirmClearCommand.Execute(null);
        Assert.Equal(1, w.Col.Data.Count);                 // mot incomplet : rien n'est effacé
        w.Col.ClearText = "  Confirmer ";
        Assert.True(w.Col.ConfirmClearCommand.CanExecute(null));
        w.Col.ConfirmClearCommand.Execute(null);
        Assert.True(w.Col.Data.IsEmpty);
        Assert.False(w.Col.ConfirmingClear);
        w.Col.Data.SetLevel(w.Items[0].Card.Id, 1);
        w.Col.ClearCommand.Execute(null); w.Col.CancelClearCommand.Execute(null);
        Assert.Equal(1, w.Col.Data.Count);                 // annuler : rien n'est effacé
    }

    [Fact]
    public void Annuler_l_importation_complete_ne_change_pas_la_collection()
    {
        if (Make() is not { } w) return;
        w.Col.Data.SetLevel(w.Items[0].Card.Id, 1);
        w.Col.OpenCommand.Execute(null); w.Col.FullCommand.Execute(null);
        w.Col.AnswerCommand.Execute("-1");
        w.Col.CancelImportCommand.Execute(null);
        Assert.True(w.Col.ConfirmingCancel);                       // d'abord une confirmation : rien n'est annulé
        Assert.True(w.Col.IsFull);
        w.Col.KeepImportCommand.Execute(null);
        Assert.True(w.Col.IsFull); Assert.False(w.Col.ConfirmingCancel);
        w.Col.CancelImportCommand.Execute(null);
        w.Col.ConfirmCancelImportCommand.Execute(null);
        Assert.True(w.Col.IsMenu);
        Assert.False(w.Col.HasDraft);
        Assert.Equal(1, w.Col.Data.LevelOf(w.Items[0].Card.Id));
        Assert.Null(PlayerCollection.Load(w.Path).Draft);
    }

    [Fact]
    public void Un_deck_n_est_montre_que_s_il_est_entierement_possede()
    {
        if (Make() is not { } w) return;
        var cards = w.Items.Take(4).ToList();
        var slots = cards.Select(c => new DeckSlot(c.Card.Id, c.Card.Talents.Count > 0 ? c.Card.Talents[0] : null)).ToList();
        var deck = new Deck { Name = "Test", Slots = slots };
        Assert.True(w.Col.ShowsDeck(deck));                // mode « toutes » : toujours
        w.Col.OwnedOnly = true;
        Assert.False(w.Col.ShowsDeck(deck));
        foreach (var c in cards) w.Col.Data.SetLevel(c.Card.Id, 3);
        Assert.True(w.Col.ShowsDeck(deck));
        w.Col.Data.SetLevel(cards[0].Card.Id, 0);          // il perd le talent 1 de la carte 1 : le deck n'est plus possédé
        if (cards[0].Card.Talents.Count > 0) Assert.False(w.Col.ShowsDeck(deck));
    }

    [Fact]
    public void Le_deck_builder_et_le_drafter_ne_montrent_que_les_decks_possedes()
    {
        if (Make() is not { } w) return;
        var lib = new DeckLibrary(new DeckStore(Path.Combine(_dir, "decks")));
        Deck Mk(string name, IEnumerable<CardItem> cs) => new()
        {
            Name = name, GameVersion = "1.30", Slots = cs.Select(c => new DeckSlot(c.Card.Id, null)).ToList(),
        };
        var mine = w.Items.Take(4).ToList();
        var other = w.Items.Skip(4).Take(4).ToList();
        lib.Save(Mk("Possédé", mine)); lib.Save(Mk("Pas possédé", other));
        var builder = new DeckBuilderViewModel(new CardBrowser(w.Items, w.Kb), w.Items, w.Kb, lib) { AllowIncompleteDecks = true };
        var drafter = new DrafterViewModel(new CardBrowser(w.Items, w.Kb), w.Items, w.Kb, lib, builder);
        builder.Collection = w.Col; drafter.Collection = w.Col;
        Assert.Equal(2, builder.SavedDecks.Count);
        w.Col.OwnedOnly = true;
        foreach (var c in mine) w.Col.Data.SetLevel(c.Card.Id, 0);
        w.Col.OpenCommand.Execute(null); w.Col.PreciseCommand.Execute(null);
        w.Col.PreciseSelected = w.Col.Rows.First(r => r.Item.Card.Id == mine[0].Card.Id);
        w.Col.SetPreciseCommand.Execute("0"); Commit(w.Col);              // déclenche le rafraîchissement des listes
        Assert.Equal(new[] { "Possédé" }, builder.SavedDecks.Select(d => d.Name).ToArray());
        Assert.Equal(new[] { "Possédé" }, drafter.Results.Select(r => r.Name).ToArray());   // le Drafter suit la liste du Builder
        w.Col.OwnedOnly = false;
        Assert.Equal(2, builder.SavedDecks.Count);
        Assert.Equal(2, drafter.Results.Count);
    }

    // ----- Générateur -----

    private (GeneratorViewModel Vm, World W)? MakeGenerator()
    {
        if (Make() is not { } w) return null;
        string? dir = null;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null && dir is null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "knowledge", "1.30");
            if (File.Exists(Path.Combine(p, "manifest.json"))) dir = p;
        }
        var coordinator = new SpamDrafter.Engines.Coordinator(SpamDrafter.Simulator.Data.SimKnowledge.Load(dir!))
        {
            CookOverride = new SpamDrafter.Engines.Cooking.CookSettings
            {
                Population = 6, Elites = 2, MutantsPerElite = 2, Generations = 2, ChampionEvery = 0, ScreenOpponents = 4,
                ConfirmPlayer = "rapide", ConfirmCandidates = 3, ConfirmOpponents = 4, Proposals = 2,
            },
        };
        var lib = new DeckLibrary(new DeckStore(Path.Combine(_dir, "gdecks")));
        var vm = new GeneratorViewModel(new CardBrowser(w.Items, w.Kb), w.Items, w.Kb, coordinator, lib, post: a => a()) { Collection = w.Col };
        vm.Browser.Collection = w.Col;
        return (vm, w);
    }

    [Fact]
    public async Task Le_generateur_ne_propose_que_des_cartes_et_talents_possedes()
    {
        if (MakeGenerator() is not { } g) return;
        var (vm, w) = g;
        var owned = w.Items.OrderBy(i => i.Card.Order).Take(8).ToList();
        foreach (var c in owned) w.Col.Data.SetLevel(c.Card.Id, 1);
        w.Col.OwnedOnly = true;
        vm.Browser.Selected = vm.Browser.Items[0];
        vm.AddCommand.Execute(null);
        Assert.Equal(8, vm.Browser.Items.Count);                  // la liste ne montre que les cartes possédées
        await vm.GenerateAsync();
        Assert.NotEmpty(vm.Proposals);
        var ids = owned.Select(c => c.Card.Id).ToHashSet();
        foreach (var p in vm.Proposals)
            foreach (var slot in p.Deck.Deck)
            {
                Assert.Contains(slot.CardId, ids);
                var talents = w.Items.First(i => i.Card.Id == slot.CardId).Card.Talents;
                if (slot.TalentId is not null) Assert.Equal(talents[0], slot.TalentId);   // niveau 1 : seulement le talent 1
            }
    }

    [Fact]
    public async Task Moins_de_quatre_cartes_possedees_le_generateur_explique_pourquoi()
    {
        if (MakeGenerator() is not { } g) return;
        var (vm, w) = g;
        foreach (var c in w.Items.Take(3)) w.Col.Data.SetLevel(c.Card.Id, 3);
        w.Col.OwnedOnly = true;
        vm.Browser.Selected = vm.Browser.Items[0];
        vm.AddCommand.Execute(null);
        await vm.GenerateAsync();
        Assert.Empty(vm.Proposals);
        Assert.Contains("au moins 4 cartes possédées", vm.Problem);
    }
}
