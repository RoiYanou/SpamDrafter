using SpamDrafter.App.ViewModels;
using SpamDrafter.Core.Decks;
using SpamDrafter.Core.Knowledge;
using SpamDrafter.Core.Text;

namespace SpamDrafter.Core.Tests;

/// <summary>La Bibliothèque en sous-onglets : Talents, Tox, Artistes, et la signature de l'artiste sur l'illustration.</summary>
public class LibraryTabsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "spamdrafter-libtabs-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static KnowledgeBase? Kb()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "knowledge", "1.30");
            if (File.Exists(Path.Combine(p, "manifest.json"))) return KnowledgeBase.Load(p);
        }
        return null;
    }

    private static (KnowledgeBase Kb, List<CardItem> Items)? Make()
    {
        if (Kb() is not { } kb) return null;
        return (kb, kb.Cards.Select(c => new CardItem(c, kb)).ToList());
    }

    // ----- Talents -----

    [Fact]
    public void Les_talents_sont_listes_par_ordre_alphabetique_sans_tenir_compte_des_accents()
    {
        if (Make() is not { } w) return;
        var vm = new TalentsViewModel(w.Kb, w.Items);
        Assert.Equal(84, vm.Rows.Count);
        var names = vm.Rows.Select(r => TextSearch.Normalize(r.Name)).ToList();
        Assert.Equal(names.OrderBy(n => n, StringComparer.Ordinal), names);
        Assert.Equal(vm.Rows.Select(r => r.Id).Distinct().Count(), vm.Rows.Count);
        // « À la caisse » se classe comme « a la caisse », pas après le « z ».
        Assert.True(names.IndexOf("a la caisse") < names.IndexOf("affaiblissement"));
        // Seuls les talents que des cartes possèdent sont listés.
        Assert.All(vm.Rows, r => Assert.True(r.CardCount >= 1));
        Assert.Equal(w.Items.Sum(i => i.Card.Talents.Count), vm.Rows.Sum(r => r.CardCount));
    }

    [Fact]
    public void Choisir_un_talent_montre_sa_description_et_les_cartes_qui_le_possedent()
    {
        if (Make() is not { } w) return;
        var vm = new TalentsViewModel(w.Kb, w.Items);
        Assert.False(vm.HasSelection);
        vm.Selected = vm.Rows.First(r => r.Name == "À la caisse");
        Assert.True(vm.HasSelection);
        Assert.Equal("À la caisse", vm.Title);
        Assert.Contains("Spamton", vm.Description);
        Assert.Equal(3, vm.Cards.Count);
        Assert.Equal("Cartes qui possèdent ce talent (3)", vm.CardsTitle);
        Assert.All(vm.Cards, c => Assert.Contains(vm.Selected.Id, c.Card.Card.Talents));
        Assert.All(vm.Cards, c => Assert.Matches(@"^Talent [123]$", c.Note));          // à quelle place de la carte il se trouve
        var order = vm.Cards.Select(c => TextSearch.Normalize(c.Card.Name)).ToList();
        Assert.Equal(order.OrderBy(n => n, StringComparer.Ordinal), order);
        // Toutes les cartes du jeu qui ont ce talent sont là, et elles seules.
        Assert.Equal(w.Items.Count(i => i.Card.Talents.Contains(vm.Selected.Id)), vm.Cards.Count);
    }

    [Fact]
    public void La_recherche_filtre_les_talents_sans_accents_et_un_clic_ouvre_la_carte()
    {
        if (Make() is not { } w) return;
        var vm = new TalentsViewModel(w.Kb, w.Items);
        vm.Search = "a la caisse";
        Assert.Equal("À la caisse", Assert.Single(vm.Rows).Name);
        Assert.Equal("1 / 84 talents", vm.CountText);
        vm.Search = "";
        Assert.Equal(84, vm.Rows.Count);
        Assert.Equal("84 talents", vm.CountText);

        vm.Selected = vm.Rows.First(r => r.Name == "À la caisse");
        CardItem? opened = null;
        vm.OpenCardRequested += c => opened = c;
        vm.OpenCardCommand.Execute(vm.Cards[0]);
        Assert.Same(vm.Cards[0].Card, opened);
    }

    // ----- Tox -----

    [Fact]
    public void Les_tox_sont_listes_par_famille_et_par_ordre_alphabetique_sans_les_passifs_de_talent()
    {
        if (Make() is not { } w) return;
        var vm = new ToxViewModel(w.Kb, w.Items);
        Assert.Equal(44, vm.Rows.Count);
        Assert.Equal(106, vm.Rows.Sum(r => r.Variants));        // 125 − 15 passifs de talent − Voleur − SolCamouflage − Coeur elementaire C − Stun_L
        var names = vm.Rows.Select(r => TextSearch.Normalize(r.Name)).ToList();
        Assert.Equal(names.OrderBy(n => n, StringComparer.Ordinal), names);
        Assert.Equal(vm.Rows.Count, names.Distinct().Count());   // plus de nom en double : le Tox « SolCamouflage » n'est plus listé
        // Retirés à la demande de l'utilisateur.
        Assert.DoesNotContain(vm.Rows, r => r.Key is "Voleur" or "SolCamouflage");
        Assert.DoesNotContain(vm.Rows, r => r.Name.Contains("Voleur"));
        Assert.Contains(vm.Rows, r => r.Name == "Camouflage");
        // Les passifs de talent (ni image, ni carte, ni boutique) ne sont pas listés.
        foreach (var key in new[] { "Roulette", "Casino", "Paradoxe", "AttackMultiColor", "Aveuglement", "ToxChanceux", "CoeurCorrompu" })
            Assert.DoesNotContain(vm.Rows, r => r.Key == key);
    }

    [Fact]
    public void Un_tox_montre_ses_valeurs_et_ses_prix_en_spamtons_pour_chaque_rarete()
    {
        if (Make() is not { } w) return;
        var vm = new ToxViewModel(w.Kb, w.Items);
        vm.Selected = vm.Rows.First(r => r.Name == "AoE");
        Assert.Equal("AoE", vm.Title);
        Assert.Equal(new[] { "Commun", "Rare", "Épique", "Légendaire" }, vm.Variants.Select(v => v.RarityName));   // de la plus commune à la plus rare
        Assert.Equal(new[] { "Valeur : 20", "Valeur : 40", "Valeur : 80", "Valeur : 100" }, vm.Variants.Select(v => v.Value));
        Assert.Equal(new[] { "Prix en boutique : 45 Spamtons", "Prix en boutique : 90 Spamtons", "Prix en boutique : 135 Spamtons", "Prix en boutique : 180 Spamtons" },
            vm.Variants.Select(v => v.Price));
        Assert.All(vm.Variants, v => Assert.True(v.InShop));
        Assert.Contains("à hauteur de 40% des dégâts", vm.Variants[1].Description);          // la valeur de chaque rareté dans le texte
        Assert.DoesNotContain(vm.Variants, v => v.Description.Contains("<"));
        Assert.Equal("Possédé de base par 3 cartes", vm.Variants[0].Innate);
        // (Le décodage de l'image elle-même demande l'interface ; ici on vérifie que le fichier de la base existe.)
        Assert.NotNull(w.Kb.ImagePath(w.Kb.AllTox.First(t => DescriptionFormatter.ToxKey(t) == "Aoe").Image));
    }

    [Fact]
    public void Camouflage_est_vendu_70_spamtons_et_un_tox_hors_boutique_ne_donne_pas_de_prix()
    {
        if (Make() is not { } w) return;
        var vm = new ToxViewModel(w.Kb, w.Items);
        vm.Selected = vm.Rows.First(r => r.Name == "Camouflage");
        var camouflage = Assert.Single(vm.Variants);
        Assert.Equal("Prix en boutique : 70 Spamtons", camouflage.Price);      // règle de l'utilisateur : les données ne le mettent dans aucune liste de la boutique
        Assert.True(camouflage.InShop);

        vm.Selected = vm.Rows.First(r => r.Name == "Contrecoup");
        Assert.Equal(new[] { "Commun", "Rare" }, vm.Variants.Select(v => v.RarityName));
        Assert.All(vm.Variants, v => { Assert.Equal("Pas vendu en boutique", v.Price); Assert.False(v.InShop); });
        // L'image fournie par l'utilisateur est bien dans la base.
        Assert.All(w.Kb.AllTox.Where(t => t.Title.StartsWith("ContreCoup")), t => Assert.NotNull(w.Kb.ImagePath(t.Image)));
    }

    [Fact]
    public void Les_cartes_qui_possedent_un_tox_de_base_sont_listees_avec_la_rarete()
    {
        if (Make() is not { } w) return;
        var vm = new ToxViewModel(w.Kb, w.Items);
        vm.Selected = vm.Rows.First(r => r.Name == "Phénix");
        Assert.Contains(vm.InnateCards, c => c.Card.Name == "Bleu-mange-cailloux" && c.Note == "Commun");
        Assert.Equal(w.Items.Count(i => (i.Card.InnatePassives ?? Array.Empty<string>()).Any(u => w.Kb.Tox(u) is { } t && DescriptionFormatter.ToxKey(t) == "Phoenix")),
            vm.InnateCards.Count);
        Assert.StartsWith("Cartes qui le possèdent de base (", vm.CardsTitle);
    }

    [Fact]
    public void La_recherche_filtre_les_tox()
    {
        if (Make() is not { } w) return;
        var vm = new ToxViewModel(w.Kb, w.Items);
        vm.Search = "phenix";
        Assert.Equal("Phénix", Assert.Single(vm.Rows).Name);
        vm.Search = "zzzz";
        Assert.Empty(vm.Rows);
        Assert.Equal("0 / 44 Tox", vm.CountText);
    }

    [Fact]
    public void Le_filtre_de_rarete_ne_garde_que_les_tox_qui_existent_dans_cette_rarete()
    {
        if (Make() is not { } w) return;
        var vm = new ToxViewModel(w.Kb, w.Items);
        var total = vm.Rows.Count;
        Assert.Equal("Toutes les raretés", vm.RarityOptions[0]);
        Assert.True(vm.RarityOptions.Count > 1);
        vm.RarityIndex = 1;
        Assert.InRange(vm.Rows.Count, 1, total);
        vm.RarityIndex = 0;
        Assert.Equal(total, vm.Rows.Count);
    }

    // ----- Artistes -----

    [Fact]
    public void Les_skins_alternatifs_apparaissent_chez_leur_artiste_et_comptent_a_part()
    {
        if (Make() is not { } w) return;
        if (w.Items.All(i => i.Skins.Count == 0)) return;                        // base sans skins : rien à vérifier
        var vm = new ArtistsViewModel(w.Kb, w.Items);
        var skin = w.Items.SelectMany(i => i.Skins.Where(s => s.Artist is not null).Select(s => (Item: i, Skin: s))).First();
        var row = vm.Rows.First(r => r.Id == skin.Skin.Artist);
        Assert.True(row.SkinCount >= 1);
        vm.Selected = row;
        Assert.Contains(vm.Cards, l => l.IsSkin && l.Skin!.Id == skin.Skin.Id && l.Card == skin.Item);
    }

    [Fact]
    public void Choisir_un_skin_change_l_affichage_par_defaut_de_la_carte_et_revient_a_la_base()
    {
        if (Make() is not { } w) return;
        var item = w.Items.FirstOrDefault(i => i.Skins.Count > 0);
        if (item is null) return;
        var changed = 0;
        item.SkinChanged += _ => changed++;
        Assert.Null(item.SelectedSkinId);
        item.SelectedSkinId = item.Skins[0].Id;
        Assert.Equal(item.Skins[0], item.SelectedSkin);
        item.SelectedSkinId = "inconnu";                                         // un skin inconnu = la carte de base
        Assert.Null(item.SelectedSkin);
        Assert.Equal(2, changed);
    }

    [Fact]
    public void Les_artistes_se_trient_par_ordre_alphabetique_ou_par_nombre_de_cartes()
    {
        if (Make() is not { } w) return;
        var vm = new ArtistsViewModel(w.Kb, w.Items);
        vm.SortIndex = 1;
        var counts = vm.Rows.Select(r => r.CardCount).ToList();
        Assert.Equal(counts.OrderByDescending(c => c), counts);
        vm.SortIndex = 0;
        var names = vm.Rows.Select(r => TextSearch.Normalize(r.Name)).ToList();
        Assert.Equal(names.OrderBy(n => n, StringComparer.Ordinal), names);
    }

    [Fact]
    public void Seuls_les_artistes_qui_ont_des_cartes_jouables_sont_listes()
    {
        if (Make() is not { } w) return;
        var vm = new ArtistsViewModel(w.Kb, w.Items);
        Assert.Equal(32, vm.Rows.Count);                                         // 32 du jeu + Shisheyu (fourni), 1 sans carte jouable ni skin retiré
        Assert.Equal(33, w.Kb.Artists.Count);
        Assert.All(vm.Rows, r => Assert.True(r.CardCount + r.SkinCount >= 1));
        Assert.Equal(151, vm.Rows.Sum(r => r.CardCount));                        // aucune carte n'a perdu son artiste
        Assert.DoesNotContain(vm.Rows, r => r.Name == "Swordsouler");                    // ni carte jouable ni skin
        Assert.All(new[] { "Crocodyle", "Etho" }, n => Assert.Equal(0, vm.Rows.First(r => r.Name == n).CardCount));   // listés pour leurs skins seulement
        var names = vm.Rows.Select(r => TextSearch.Normalize(r.Name)).ToList();
        Assert.Equal(names.OrderBy(n => n, StringComparer.Ordinal), names);
        Assert.Equal("32 artistes", vm.CountText);
        Assert.Equal("1 carte", vm.Rows.First(r => r.CardCount == 1 && r.SkinCount == 0).Count);
    }

    [Fact]
    public void Choisir_un_artiste_montre_son_lien_et_ses_cartes()
    {
        if (Make() is not { } w) return;
        var vm = new ArtistsViewModel(w.Kb, w.Items);
        vm.Selected = vm.Rows.First(r => r.Name == "Atto");
        Assert.Equal("Atto", vm.Title);
        Assert.Equal("https://x.com/Atto_Wnd", vm.Link);
        Assert.True(vm.HasLink);
        Assert.Equal(14, vm.Cards.Count);
        Assert.StartsWith("Cartes illustrées (14)", vm.CardsTitle);
        Assert.All(vm.Cards, c => Assert.Equal(vm.Selected.Id, c.Card.Card.Artist));
        Assert.All(vm.Cards, c => Assert.False(c.HasNote));
    }

    [Fact]
    public void Le_lien_d_un_artiste_s_ouvre_seulement_s_il_est_web()
    {
        if (Make() is not { } w) return;
        var vm = new ArtistsViewModel(w.Kb, w.Items);
        var opened = new List<string>();
        vm.LinkLauncher = url => { opened.Add(url); return true; };
        vm.Selected = vm.Rows.First(r => r.Name == "Atto");
        Assert.True(vm.OpenLinkCommand.CanExecute(null));
        vm.OpenLinkCommand.Execute(null);
        Assert.Equal(new[] { "https://x.com/Atto_Wnd" }, opened);

        vm.LinkLauncher = _ => false;
        vm.OpenLinkCommand.Execute(null);
        Assert.Equal("Impossible d'ouvrir ce lien.", vm.Message);

        // Un artiste listé sans lien (« Mr Wolf ») : rien à ouvrir.
        vm.Selected = vm.Rows.First(r => r.Name == "Mr Wolf");
        Assert.False(vm.HasLink);
        Assert.False(vm.OpenLinkCommand.CanExecute(null));
    }

    [Fact]
    public void Une_base_sans_artistes_se_charge_et_les_sous_onglets_restent_vides()
    {
        if (Make() is not { } w) return;
        var old = Path.Combine(_dir, "old");
        Directory.CreateDirectory(Path.Combine(old, "texts"));
        foreach (var f in new[] { "manifest.json", "cards.json", "talents.json", "elements.json", "rarities.json" }) File.Copy(Path.Combine(w.Kb.Directory, f), Path.Combine(old, f));
        File.Copy(Path.Combine(w.Kb.Directory, "texts", "fr.json"), Path.Combine(old, "texts", "fr.json"));
        var kb = KnowledgeBase.Load(old);
        Assert.Empty(kb.Artists);
        Assert.Empty(kb.AllTox);
        var items = kb.Cards.Select(c => new CardItem(c, kb)).ToList();
        Assert.Empty(new ArtistsViewModel(kb, items).Rows);
        Assert.Empty(new ToxViewModel(kb, items).Rows);
        Assert.NotEmpty(new TalentsViewModel(kb, items).Rows);
    }

    // ----- Sous-onglets et signature -----

    private (LibraryTabsViewModel Tabs, List<CardItem> Items, KnowledgeBase Kb)? MakeTabs()
    {
        if (Make() is not { } w) return null;
        var library = new LibraryViewModel(new CardBrowser(w.Items, w.Kb), w.Kb, new DeckLibrary(new DeckStore(_dir)), w.Items);
        return (new LibraryTabsViewModel(library, new TalentsViewModel(w.Kb, w.Items), new ToxViewModel(w.Kb, w.Items), new ArtistsViewModel(w.Kb, w.Items)), w.Items, w.Kb);
    }

    [Fact]
    public void Cliquer_sur_une_carte_d_un_sous_onglet_l_ouvre_dans_cartes_meme_si_un_filtre_la_cache()
    {
        if (MakeTabs() is not { } w) return;
        var tabs = w.Tabs;
        tabs.Cards.Browser.Element = tabs.Cards.Browser.ElementOptions.First(o => o.Id == "fire");   // un filtre qui cache les cartes d'eau
        tabs.SelectedTab = 1;
        tabs.Talents.Selected = tabs.Talents.Rows.First(r => r.Name == "Blindage Partagé");
        var water = tabs.Talents.Cards.First(c => c.Card.Card.Element != "fire");
        tabs.Talents.OpenCardCommand.Execute(water);
        Assert.Equal(0, tabs.SelectedTab);
        Assert.Same(water.Card, tabs.Cards.Browser.Selected);
        Assert.Contains(water.Card, tabs.Cards.Browser.Items);          // les filtres ont été remis à zéro
        Assert.Equal(water.Card.Name, tabs.Cards.Title);
    }

    [Fact]
    public void Le_nom_de_l_artiste_est_la_signature_de_l_illustration()
    {
        if (MakeTabs() is not { } w) return;
        var vm = w.Tabs.Cards;
        Assert.False(vm.HasArtistCredit);
        vm.Browser.Selected = w.Items.First(i => i.Name == "Bleu-mange-cailloux");
        Assert.True(vm.HasArtistCredit);
        Assert.Equal("Illustration : ElisaMalan", vm.ArtistCredit);
        vm.Browser.Selected = w.Items.First(i => i.Name == "Esprit Eau");
        Assert.Equal("Illustration : Atto", vm.ArtistCredit);
        // Chaque carte a une signature.
        foreach (var item in w.Items) { vm.Browser.Selected = item; Assert.StartsWith("Illustration : ", vm.ArtistCredit); }
    }
}
