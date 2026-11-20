using SpamDrafter.App.ViewModels;
using SpamDrafter.Core.Challenges;
using SpamDrafter.Core.Decks;
using SpamDrafter.Core.Knowledge;

namespace SpamDrafter.Core.Tests;

/// <summary>Onglet Challenges : règles, roue, pré-sets, éditeur de deck restreint, badge et verrouillage.</summary>
public class ChallengesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "spamdrafter-ch-" + Guid.NewGuid());
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

    private static CardView V(string element = "fire", int hp = 900, int income = 10, string name = "Zed", string species = "A", string region = "R",
        int abilities = 2, int tox = 0, CardTag? tag = null) =>
        new(new Card(Guid.NewGuid().ToString(), 1, name, name, element, "rarity-common", hp, income, new Dictionary<string, int>(),
            Enumerable.Range(0, abilities).Select(i => "a" + i).ToList(), new[] { "t" }, null, null, Enumerable.Range(0, tox).Select(i => "x" + i).ToList()),
            name, species, region, tag);

    private static ChallengeDefinition Def(string id) => ChallengeCatalog.Defaults.First(d => d.Id == id);

    // ----- Liste par défaut -----
    [Fact]
    public void Dix_huit_challenges_par_defaut_et_spamtons_inclusifs()
    {
        Assert.Equal(18, ChallengeCatalog.Defaults.Count);
        Assert.Equal(18, ChallengeCatalog.Defaults.Select(d => d.Id).Distinct().Count());
        Assert.Equal("Jouer un deck dont la somme de Spamtons est inférieure ou égale à 25", Def("spamtons-25").Title);
        Assert.Equal("Jouer un deck dont la somme de Spamtons est supérieure ou égale à 40", Def("spamtons-40").Title);
    }

    // ----- Règles -----
    [Fact]
    public void Spamtons_bornes_incluses()
    {
        var low = Def("spamtons-25"); var high = Def("spamtons-40");
        var s25 = new[] { V(income: 10), V(income: 5), V(income: 5), V(income: 5) };
        var s26 = new[] { V(income: 10), V(income: 5), V(income: 5), V(income: 6) };
        Assert.True(ChallengeCatalog.Satisfied(low, s25)); Assert.False(ChallengeCatalog.Satisfied(low, s26));
        var s40 = new[] { V(income: 10), V(income: 10), V(income: 10), V(income: 10) };
        var s39 = new[] { V(income: 10), V(income: 10), V(income: 10), V(income: 9) };
        Assert.True(ChallengeCatalog.Satisfied(high, s40)); Assert.False(ChallengeCatalog.Satisfied(high, s39));
    }

    [Fact]
    public void Regles_par_carte_pv_tox_et_emplacements()
    {
        Assert.True(ChallengeCatalog.CardEligible(Def("moins-de-800-pv"), V(hp: 799)));
        Assert.False(ChallengeCatalog.CardEligible(Def("moins-de-800-pv"), V(hp: 800)));
        Assert.True(ChallengeCatalog.CardEligible(Def("plus-de-800-pv"), V(hp: 801)));
        Assert.False(ChallengeCatalog.CardEligible(Def("plus-de-800-pv"), V(hp: 800)));
        Assert.True(ChallengeCatalog.CardEligible(Def("sans-tox-de-depart"), V(tox: 0)));
        Assert.False(ChallengeCatalog.CardEligible(Def("sans-tox-de-depart"), V(tox: 1)));
        Assert.True(ChallengeCatalog.CardEligible(Def("trois-emplacements"), V(abilities: 2, tox: 1)));
        Assert.True(ChallengeCatalog.CardEligible(Def("trois-emplacements"), V(abilities: 3, tox: 0)));
        Assert.False(ChallengeCatalog.CardEligible(Def("trois-emplacements"), V(abilities: 2, tox: 0)));
    }

    [Fact]
    public void Regles_d_ensemble_elements_lore_et_lettres()
    {
        var diff = Def("elements-differents");
        Assert.True(ChallengeCatalog.Satisfied(diff, new[] { V("fire"), V("water"), V("earth"), V("light") }));
        Assert.False(ChallengeCatalog.Satisfied(diff, new[] { V("fire"), V("fire"), V("earth"), V("light") }));
        var two = Def("deux-meme-element");
        Assert.True(ChallengeCatalog.Satisfied(two, new[] { V("fire"), V("fire"), V("water"), V("water") }));
        Assert.False(ChallengeCatalog.Satisfied(two, new[] { V("fire"), V("fire"), V("fire"), V("water") }));
        var four = Def("quatre-meme-element");
        Assert.True(ChallengeCatalog.Satisfied(four, new[] { V("fire"), V("fire"), V("fire"), V("fire") }));
        Assert.False(ChallengeCatalog.Satisfied(four, new[] { V("fire"), V("fire"), V("fire"), V("water") }));
        Assert.True(ChallengeCatalog.Satisfied(Def("meme-region"), new[] { V(region: "X"), V(region: "X"), V(region: "X"), V(region: "X") }));
        Assert.False(ChallengeCatalog.Satisfied(Def("meme-region"), new[] { V(region: "X"), V(region: "Y"), V(region: "X"), V(region: "X") }));
        Assert.True(ChallengeCatalog.Satisfied(Def("meme-espece"), new[] { V(species: "S"), V(species: "S"), V(species: "S"), V(species: "S") }));
        Assert.False(ChallengeCatalog.Satisfied(Def("meme-espece"), new[] { V(species: "S"), V(species: "T"), V(species: "S"), V(species: "S") }));
        var letter = Def("meme-lettre");
        Assert.True(ChallengeCatalog.Satisfied(letter, new[] { V(name: "Amber"), V(name: "Éclat"), V(name: "Aidan"), V(name: "Zed") }));
        Assert.True(ChallengeCatalog.Satisfied(letter, new[] { V(name: "Éclat"), V(name: "Eau"), V(name: "Aidan"), V(name: "Zed") }));   // « É » et « E » : même lettre
        Assert.False(ChallengeCatalog.Satisfied(letter, new[] { V(name: "Amber"), V(name: "Bob"), V(name: "Cid"), V(name: "Zed") }));
    }

    [Fact]
    public void Regles_d_illustration_utilisent_les_etiquettes()
    {
        var left = new CardTag("L", true, false, 2); var right = new CardTag("R", false, true, 0);
        Assert.True(ChallengeCatalog.CardEligible(Def("regard-gauche"), V(tag: left)));
        Assert.False(ChallengeCatalog.CardEligible(Def("regard-gauche"), V(tag: right)));
        Assert.True(ChallengeCatalog.CardEligible(Def("regard-droite"), V(tag: right)));
        Assert.True(ChallengeCatalog.CardEligible(Def("quadrupedes"), V(tag: left)));
        Assert.False(ChallengeCatalog.CardEligible(Def("quadrupedes"), V(tag: right)));
        Assert.True(ChallengeCatalog.CardEligible(Def("avec-vetements"), V(tag: right)));
        Assert.True(ChallengeCatalog.CardEligible(Def("sans-vetements"), V(tag: left)));
        Assert.True(ChallengeCatalog.CardEligible(Def("deux-animaux"), V(tag: left)));
        Assert.False(ChallengeCatalog.CardEligible(Def("deux-animaux"), V(tag: right)));
        // Sans étiquette : jamais proposée, plutôt que de supposer.
        Assert.False(ChallengeCatalog.CardEligible(Def("sans-vetements"), V(tag: null)));
        Assert.False(ChallengeCatalog.CardEligible(Def("regard-gauche"), V(tag: null)));
    }

    [Fact]
    public void Problemes_exigent_4_cartes_et_nomment_les_fautives()
    {
        var d = Def("moins-de-800-pv");
        Assert.Contains(ChallengeCatalog.Problems(d, new[] { V(hp: 700) }), p => p.Contains("4 cartes"));
        var p2 = ChallengeCatalog.Problems(d, new[] { V(hp: 700), V(hp: 700), V(hp: 700), V(hp: 900, name: "Gros") });
        Assert.Contains(p2, p => p.Contains("Gros"));
        Assert.Empty(ChallengeCatalog.Problems(d, new[] { V(hp: 700), V(hp: 700), V(hp: 700), V(hp: 700) }));
        Assert.Empty(ChallengeCatalog.Problems(new ChallengeDefinition("c", "Perso", "", ChallengeKind.Custom), new[] { V() }));   // challenge du joueur : pas de vérification
    }

    // ----- Données réelles -----
    [Fact]
    public void Etiquettes_couvrent_toutes_les_cartes()
    {
        if (Kb() is not { } kb) return;
        Assert.All(kb.Cards, c => Assert.True(CardTags.All.ContainsKey(c.SanitizedName), c.SanitizedName + " sans étiquette"));
        Assert.All(CardTags.All.Values, t => Assert.Contains(t.Facing, new[] { "L", "R", "F", "N" }));
    }

    private static bool Search(ChallengeDefinition d, ChallengeContext ctx, List<CardView> current, List<CardView> pool, int from)
    {
        if (current.Count == ChallengeCatalog.DeckSize) return ChallengeCatalog.Problems(d, current).Count == 0;
        for (var i = from; i < pool.Count; i++)
        {
            if (!ChallengeCatalog.CanAdd(d, current, pool[i], ctx)) continue;
            current.Add(pool[i]);
            if (Search(d, ctx, current, pool, i + 1)) return true;
            current.RemoveAt(current.Count - 1);
        }
        return false;
    }

    [Fact]
    public void Chaque_challenge_automatique_peut_etre_realise_avec_les_cartes_du_jeu()
    {
        if (Kb() is not { } kb) return;
        var ctx = new ChallengeContext(kb);
        var pool = ctx.All.ToList();
        foreach (var d in ChallengeCatalog.Defaults)
        {
            var deck = new List<CardView>();
            Assert.True(Search(d, ctx, deck, pool, 0), "Aucun deck possible pour : " + d.Title);
        }
    }

    // ----- Roue -----
    [Fact]
    public void Parts_de_la_roue_proportionnelles_aux_poids()
    {
        var s = WheelMath.Slices(new[] { 10, 30, 0, 60 });
        Assert.Equal((0.0, 0.1), s[0]);
        Assert.Equal(0.1, s[1].Start, 9); Assert.Equal(0.4, s[1].End, 9);
        Assert.Equal(s[2].Start, s[2].End);
        Assert.Equal(1.0, s[3].End, 9);
    }

    [Fact]
    public void Tirage_suit_les_poids()
    {
        var w = new[] { 10, 30, 0, 60 };
        Assert.Equal(0, WheelMath.Pick(w, 0.05));
        Assert.Equal(1, WheelMath.Pick(w, 0.1));
        Assert.Equal(1, WheelMath.Pick(w, 0.39));
        Assert.Equal(3, WheelMath.Pick(w, 0.4));
        Assert.Equal(3, WheelMath.Pick(w, 0.9999));
        Assert.Equal(-1, WheelMath.Pick(new[] { 0, 0 }, 0.5));
    }

    // ----- Réglages, pré-sets, modèle de vue -----
    private string Path_() => Path.Combine(_dir, "challenges.json");

    [Fact]
    public void Roue_tire_le_challenge_selon_les_poids_et_s_arrete_sur_lui()
    {
        var rolls = new Queue<double>(new[] { 0.95, 0.5 });
        var vm = new ChallengesViewModel(Path_(), () => rolls.Dequeue());
        foreach (var e in vm.Entries) e.Weight = 1;
        vm.Entries[0].Weight = 1; vm.Entries[^1].Weight = 100;   // le dernier occupe presque toute la roue
        double asked = -1;
        vm.SpinRequested += f => asked = f;
        vm.Spin();
        Assert.True(vm.IsSpinning);
        Assert.Null(vm.Result);   // le résultat n'apparaît qu'une fois la roue arrêtée
        var last = vm.Slices[^1];
        Assert.InRange(asked, last.Start, last.End);
        vm.FinishSpin();
        Assert.Equal(vm.Entries[^1].Title, vm.Result!.Title);
        Assert.False(vm.IsSpinning);
    }

    [Fact]
    public void Desactiver_un_challenge_le_retire_de_la_roue_et_l_etat_est_retenu()
    {
        var vm = new ChallengesViewModel(Path_());
        var total = vm.Slices.Count;
        vm.Entries[3].Enabled = false; vm.Entries[4].Weight = 55;
        Assert.Equal(total - 1, vm.Slices.Count);
        Assert.Equal("", vm.Entries[3].Percent);
        var vm2 = new ChallengesViewModel(Path_());
        Assert.False(vm2.Entries[3].Enabled);
        Assert.Equal(55, vm2.Entries[4].Weight);
        Assert.Equal(total - 1, vm2.Slices.Count);
    }

    [Fact]
    public void Ajouter_un_challenge_et_le_supprimer()
    {
        var vm = new ChallengesViewModel(Path_());
        vm.NewTitle = "Jouer un deck sans soin"; vm.NewConditions = "Aucune carte ne soigne.";
        vm.AddCommand.Execute(null);
        Assert.Equal(19, vm.Entries.Count);
        var added = vm.Entries[^1];
        Assert.True(added.IsCustom); Assert.True(added.Enabled);
        Assert.Equal(19, new ChallengesViewModel(Path_()).Entries.Count);
        vm.NewTitle = "jouer un deck sans soin"; vm.AddCommand.Execute(null);
        Assert.Equal(19, vm.Entries.Count);   // doublon refusé
        vm.DeleteCommand.Execute(added);
        Assert.Equal(18, vm.Entries.Count);
        vm.DeleteCommand.Execute(vm.Entries[0]);   // un challenge par défaut ne se supprime pas
        Assert.Equal(18, vm.Entries.Count);
    }

    [Fact]
    public void Presets_de_roue_enregistrer_appliquer_supprimer()
    {
        var vm = new ChallengesViewModel(Path_());
        foreach (var e in vm.Entries.Skip(3)) e.Enabled = false;
        vm.Entries[0].Weight = 40;
        vm.PresetName = "Petite roue"; vm.SavePresetCommand.Execute(null);
        Assert.Contains("Petite roue", vm.PresetNames);
        vm.SelectedPreset = ChallengeBook.DefaultPresetName;
        Assert.Equal(18, vm.Slices.Count);
        Assert.Equal(ChallengeBook.DefaultWeight, vm.Entries[0].Weight);
        vm.SelectedPreset = "Petite roue";
        Assert.Equal(3, vm.Slices.Count);
        Assert.Equal(40, vm.Entries[0].Weight);
        var vm2 = new ChallengesViewModel(Path_());   // retenu d'une fois sur l'autre
        Assert.Contains("Petite roue", vm2.PresetNames);
        vm.DeletePresetCommand.Execute(null);
        Assert.DoesNotContain("Petite roue", vm.PresetNames);
    }

    // ----- Éditeur de deck restreint, badge et verrouillage -----
    private DeckBuilderViewModel MakeBuilder(KnowledgeBase kb)
    {
        var items = kb.Cards.Select(c => new CardItem(c, kb)).ToList();
        return new DeckBuilderViewModel(new CardBrowser(items, kb), items, kb, new DeckLibrary(new DeckStore(Path.Combine(_dir, "decks"))));
    }

    private static void Add(DeckBuilderViewModel vm, CardItem c) { vm.Browser.Selected = c; vm.AddCommand.Execute(null); }

    [Fact]
    public void Editeur_de_challenge_ne_propose_que_les_cartes_permises_et_refuse_les_autres()
    {
        if (Kb() is not { } kb) return;
        var vm = MakeBuilder(kb);
        var all = vm.Browser.Items.ToList();
        vm.StartChallenge(Def("moins-de-800-pv"));
        Assert.True(vm.ChallengeBannerVisible);
        Assert.All(vm.Browser.Items, i => Assert.True(i.Card.Hp < 800));
        Assert.True(vm.Browser.Items.Count < all.Count);
        // Une carte interdite est refusée même si elle est choisie par un autre chemin.
        var big = all.First(i => i.Card.Hp >= 800);
        Add(vm, big);
        Assert.Empty(vm.Slots);
        Assert.Contains("challenge", vm.Status);
    }

    [Fact]
    public void Deck_de_challenge_sauvegarde_porte_le_badge_et_n_est_plus_modifiable()
    {
        if (Kb() is not { } kb) return;
        var vm = MakeBuilder(kb);
        var def = Def("quatre-meme-element");
        vm.StartChallenge(def);
        // Incomplet : refus.
        var fire = vm.Browser.Items.Where(i => i.Card.Element == "fire").Take(4).ToList();
        Add(vm, fire[0]);
        vm.SaveCommand.Execute(null);
        Assert.Contains("impossible", vm.Status);
        // Après une carte de feu, seules des cartes de feu sont proposées.
        Assert.All(vm.Browser.Items, i => Assert.Equal("fire", i.Card.Element));
        for (var i = 1; i < 4; i++) Add(vm, fire[i]);
        Assert.Equal(4, vm.Slots.Count);
        vm.SaveCommand.Execute(null);
        var saved = Assert.Single(vm.SavedDecks);
        Assert.NotNull(saved.Challenge);
        Assert.Equal(def.Id, saved.Challenge!.Id);
        Assert.True(saved.IsChallenge);
        Assert.Equal(def.Title, saved.ChallengeTip);   // le survol du badge ne montre que le titre
        Assert.True(vm.IsLocked);
        Assert.False(vm.CanEditCards);
        Assert.All(vm.Slots, s => Assert.False(s.IsEditable));
        // Plus de modification de cartes.
        vm.RemoveCommand.Execute(vm.Slots[0]);
        Assert.Equal(4, vm.Slots.Count);
        // Le badge survit au rechargement et au passage en favori.
        vm.ToggleFavoriteCommand.Execute(null);
        var vm2 = MakeBuilder(kb);
        Assert.NotNull(Assert.Single(vm2.SavedDecks).Challenge);
        Assert.True(Assert.Single(vm2.SavedDecks).Favorite);
        // Sortie du mode challenge : la liste des cartes est complète de nouveau.
        vm.NewCommand.Execute(null);
        Assert.False(vm.ChallengeBannerVisible);
        Assert.True(vm.Browser.Items.Count > 4 && vm.Browser.Items.Any(i => i.Card.Element != "fire"));
    }

    [Fact]
    public void Filtre_d_origine_restreint_par_un_challenge()
    {
        if (Kb() is not { } kb) return;
        var vm = MakeBuilder(kb);
        vm.StartChallenge(Def("moins-de-800-pv"));
        foreach (var c in vm.Browser.Items.Take(4).ToList()) Add(vm, c);
        vm.Name = "Challengé"; vm.SaveCommand.Execute(null);
        vm.NewCommand.Execute(null);
        foreach (var c in vm.Browser.Items.Skip(10).Take(4).ToList()) Add(vm, c);
        vm.Name = "Libre"; vm.SaveCommand.Execute(null);
        Assert.Equal(2, vm.SavedDecks.Count);
        vm.OriginFilter = vm.OriginOptions.First(o => o.Id == "challenge");
        Assert.Equal("Challengé", Assert.Single(vm.SavedDecks).Name);
        vm.OriginFilter = vm.OriginOptions.First(o => o.Id == "manual");
        Assert.Equal("Libre", Assert.Single(vm.SavedDecks).Name);
        Assert.Contains(vm.OriginOptions, o => o.Label == "Restreints par un challenge");
    }

    [Fact]
    public void Dupliquer_un_deck_de_challenge_donne_une_copie_libre_sans_badge()
    {
        if (Kb() is not { } kb) return;
        var vm = MakeBuilder(kb);
        vm.StartChallenge(Def("moins-de-800-pv"));
        foreach (var c in vm.Browser.Items.Take(4).ToList()) Add(vm, c);
        vm.SaveCommand.Execute(null);
        Assert.True(vm.IsLocked);
        vm.DuplicateCommand.Execute(null);
        Assert.False(vm.IsLocked);
        Assert.Equal(2, vm.SavedDecks.Count);
        Assert.Single(vm.SavedDecks, d => d.Challenge is not null);
    }

    [Fact]
    public void Challenge_du_joueur_se_sauvegarde_sans_verification()
    {
        if (Kb() is not { } kb) return;
        var vm = MakeBuilder(kb);
        var custom = new CustomChallenge { Title = "Sans soin", Conditions = "À toi de jouer." }.ToDefinition();
        vm.StartChallenge(custom);
        var all = vm.Browser.Items.Take(4).ToList();
        foreach (var c in all) Add(vm, c);
        vm.SaveCommand.Execute(null);
        var saved = Assert.Single(vm.SavedDecks);
        Assert.Equal("Sans soin", saved.Challenge!.Title);
    }

    [Fact]
    public void Ancien_fichier_de_deck_sans_challenge_se_lit()
    {
        var store = new DeckStore(Path.Combine(_dir, "d"));
        var d = store.Deserialize("{\"Name\":\"Vieux\",\"Slots\":[]}")!;
        Assert.Null(d.Challenge); Assert.False(d.IsChallenge); Assert.Equal("", d.ChallengeTip);
    }

    // ----- Retouches du 20/11 -----
    [Fact]
    public void Pourcentage_tape_a_la_main_regle_le_poids()
    {
        var vm = new ChallengesViewModel(Path_());
        var e = vm.Entries[0];
        e.BeginEditCommand.Execute(null);
        Assert.True(e.IsEditingPercent);
        e.PercentEdit = "25 %"; e.Commit();
        Assert.False(e.IsEditingPercent);
        var share = vm.Slices.First(s => s.Label == e.ShortTitle);
        Assert.InRange((share.End - share.Start) * 100, 24.5, 25.5);
        Assert.True(e.SliderMax >= e.Weight);   // le curseur peut montrer un poids au-delà de 100
        e.BeginEditCommand.Execute(null); e.PercentEdit = "abc"; var before = e.Weight; e.Commit();
        Assert.Equal(before, e.Weight);   // texte invalide : rien ne change
    }

    [Fact]
    public void Curseurs_suivent_le_changement_de_preset_et_fleche_deroule_le_bouton()
    {
        var vm = new ChallengesViewModel(Path_());
        vm.Entries[0].Weight = 60; vm.PresetName = "Fort"; vm.SavePresetCommand.Execute(null);
        vm.SelectedPreset = ChallengeBook.DefaultPresetName;
        var moved = new List<string>();
        vm.Entries[0].PropertyChanged += (_, a) => moved.Add(a.PropertyName!);
        vm.SelectedPreset = "Fort";
        Assert.Contains("WeightValue", moved);   // le curseur est prévenu et se replace
        Assert.Equal(60, vm.Entries[0].WeightValue);
        var row = vm.Entries[1];
        Assert.False(row.IsExpanded);
        row.ToggleCommand.Execute(null);
        Assert.True(row.IsExpanded); Assert.Equal("▴", row.Arrow);
    }

    [Fact]
    public void Croix_annule_la_restriction_et_garde_les_cartes()
    {
        if (Kb() is not { } kb) return;
        var vm = MakeBuilder(kb);
        vm.StartChallenge(Def("moins-de-800-pv"));
        Assert.True(vm.ChallengeCancelable);
        Add(vm, vm.Browser.Items.First());
        vm.CancelChallengeCommand.Execute(null);
        Assert.False(vm.ChallengeCancelable); Assert.False(vm.ChallengeBannerVisible);
        Assert.Single(vm.Slots);
        Assert.Contains(vm.Browser.Items, i => i.Card.Hp >= 800);   // toutes les cartes sont de nouveau proposées
        for (var i = 0; i < 3; i++) Add(vm, vm.Browser.Items.First(c => c.Card.Hp >= 800 && vm.Slots.All(s => s.Card.Card.Id != c.Card.Id)));
        vm.SaveCommand.Execute(null);
        Assert.Null(Assert.Single(vm.SavedDecks).Challenge);   // sans restriction : pas de badge
    }

    [Fact]
    public void Chances_egales_remet_le_meme_poids_partout_et_le_retient()
    {
        var vm = new ChallengesViewModel(Path_());
        vm.Entries[0].Weight = 70; vm.Entries[1].Weight = 3;
        vm.EqualChancesCommand.Execute(null);
        Assert.All(vm.Entries, e => Assert.Equal(ChallengeBook.DefaultWeight, e.Weight));
        Assert.All(vm.Entries, e => Assert.Equal(ChallengeBook.DefaultWeight, e.WeightValue));
        Assert.Equal(vm.Entries[0].Percent, vm.Entries[1].Percent);
        Assert.All(new ChallengesViewModel(Path_()).Entries, e => Assert.Equal(ChallengeBook.DefaultWeight, e.Weight));
    }
}

/// <summary>Case « Voir la carte » : retenue pour toutes les cartes et toutes les fiches jusqu'au redémarrage.</summary>
public class SeeCardTests
{
    [Fact]
    public void Voir_la_carte_reste_coche_d_une_carte_et_d_une_fiche_a_l_autre()
    {
        var dir = Path.Combine(Path.GetTempPath(), "spamdrafter-see-" + Guid.NewGuid());
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "knowledge", "1.30");
            if (!File.Exists(Path.Combine(p, "manifest.json"))) continue;
            var kb = SpamDrafter.Core.Knowledge.KnowledgeBase.Load(p);
            var items = kb.Cards.Select(c => new SpamDrafter.App.ViewModels.CardItem(c, kb)).ToList();
            var a = new SpamDrafter.App.ViewModels.LibraryViewModel(new SpamDrafter.App.ViewModels.CardBrowser(items, kb), kb, null, items);
            var b = new SpamDrafter.App.ViewModels.LibraryViewModel(new SpamDrafter.App.ViewModels.CardBrowser(items, kb), kb, null, items);
            try
            {
                a.Browser.Selected = items[0];
                a.SeeCard = true;
                a.Browser.Selected = null; a.Browser.Selected = items[1];   // autre carte, après fermeture
                Assert.True(a.SeeCard); Assert.False(a.ShowCardInfo);
                Assert.True(b.SeeCard);                                      // l'autre fiche aussi
                b.SeeCard = false;
                Assert.False(a.SeeCard);
            }
            finally { a.SeeCard = false; }
            return;
        }
    }
}
