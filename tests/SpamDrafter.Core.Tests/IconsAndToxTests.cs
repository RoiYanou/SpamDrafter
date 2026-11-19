using Avalonia.Media;
using SpamDrafter.App.ViewModels;
using SpamDrafter.Core.Decks;
using SpamDrafter.Core.Knowledge;

namespace SpamDrafter.Core.Tests;

/// <summary>Icônes des éléments et des raretés, Tox possédés de base, précision et critiques des capacités.</summary>
public class IconsAndToxTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "spamdrafter-icons-" + Guid.NewGuid().ToString("N"));

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

    private static Card Card(KnowledgeBase kb, string sanitizedName) => kb.Cards.First(c => c.SanitizedName == sanitizedName);

    // ----- Icônes et couleurs -----

    [Fact]
    public void Les_7_elements_et_les_5_raretes_ont_leur_icone_dans_la_base()
    {
        if (Kb() is not { } kb) return;
        Assert.Equal(7, kb.Elements.Count);
        Assert.All(kb.Elements, e => Assert.NotNull(kb.ImagePath(e.Icon)));
        Assert.Equal(5, kb.Rarities.Count);
        Assert.All(kb.Rarities, r => Assert.NotNull(kb.ImagePath(r.Icon)));
        Assert.Equal("images/icons/elements/fire.webp", kb.Element("fire")!.Icon);
    }

    [Fact]
    public void Les_couleurs_des_raretes_sont_celles_du_jeu()
    {
        if (Kb() is not { } kb) return;
        Assert.Equal("#0050FF", kb.Rarity("rarity-rare")!.Color);        // _primaryColor de 2-rarity-rare
        Assert.Equal("#4FE6FF", kb.Rarity("rarity-rare")!.TextColor);    // _primaryTextColor
        Assert.All(kb.Rarities, r => { Assert.Matches("^#[0-9A-F]{6}$", r.Color); Assert.Matches("^#[0-9A-F]{6}$", r.TextColor); });
        Assert.Equal(5, kb.Rarities.Select(r => r.Color).Distinct().Count());   // une couleur par rareté
    }

    [Fact]
    public void Les_pinceaux_de_rarete_suivent_la_base_et_ont_un_repli()
    {
        if (Kb() is not { } kb) return;
        var rare = (SolidColorBrush)Icons.RarityBrush(kb, "rarity-rare");
        Assert.Equal(Color.Parse("#0050FF"), rare.Color);
        var text = (SolidColorBrush)Icons.RarityTextBrush(kb, "rarity-rare");
        Assert.Equal(Color.Parse("#4FE6FF"), text.Color);
        // Rareté inconnue ou absente : un gris neutre, jamais d'erreur.
        Assert.Equal(Color.Parse("#666666"), ((SolidColorBrush)Icons.RarityBrush(kb, "rarity-inconnue")).Color);
        Assert.Equal(Color.Parse("#666666"), ((SolidColorBrush)Icons.RarityBrush(kb, null)).Color);
        Assert.Null(Icons.Load(kb, null));
        Assert.Null(Icons.Load(kb, "images/icons/absent.webp"));
    }

    [Fact]
    public void Une_base_sans_icones_se_charge_et_s_affiche_sans()
    {
        if (Kb() is not { } kb) return;
        // Base « ancienne » : pas de champ icon/color dans elements.json et rarities.json.
        var old = Path.Combine(_dir, "old");
        Directory.CreateDirectory(Path.Combine(old, "texts"));
        foreach (var f in new[] { "manifest.json", "cards.json", "talents.json", "tox.json" }) File.Copy(Path.Combine(kb.Directory, f), Path.Combine(old, f));
        File.Copy(Path.Combine(kb.Directory, "texts", "fr.json"), Path.Combine(old, "texts", "fr.json"));
        File.WriteAllText(Path.Combine(old, "elements.json"), "[{\"id\":\"fire\",\"order\":1,\"nameKey\":\"Element.Name.fire\"}]");
        File.WriteAllText(Path.Combine(old, "rarities.json"), "[{\"id\":\"rarity-common\",\"order\":1}]");
        var oldKb = KnowledgeBase.Load(old);
        Assert.Null(oldKb.Element("fire")!.Icon);
        Assert.Null(oldKb.Rarity("rarity-common")!.Color);
        Assert.Null(Icons.Element(oldKb, "fire"));
        Assert.Equal(Color.Parse("#666666"), ((SolidColorBrush)Icons.RarityBrush(oldKb, "rarity-common")).Color);
    }

    // ----- Tox possédés de base -----

    [Fact]
    public void Une_carte_connait_ses_tox_de_base_dans_l_ordre_du_jeu()
    {
        if (Kb() is not { } kb) return;
        var card = Card(kb, "bleu_mange_cailloux");
        var tox = kb.InnateTox(card);
        Assert.Equal(2, tox.Count);
        Assert.Equal(new[] { "Phénix", "Reset" }, tox.Select(kb.ToxName));
        Assert.Equal(new[] { "rarity-common", "rarity-epic" }, tox.Select(t => t.Rarity));
        Assert.All(tox, t => Assert.NotNull(kb.ImagePath(t.Image)));
        Assert.Empty(kb.InnateTox(kb.Cards.First(c => c.InnatePassives is not { Count: > 0 })));   // une carte sans Tox de base
        Assert.Equal(1, kb.Cards.Count(c => c.InnatePassives is { Count: 3 }));
    }

    [Fact]
    public void Les_descriptions_de_tox_remplacent_la_valeur_comme_le_jeu()
    {
        if (Kb() is not { } kb) return;
        var phenix = kb.InnateTox(Card(kb, "bleu_mange_cailloux"))[0];
        var d = kb.Descriptions.Tox(phenix);
        Assert.Contains("revient à la vie avec 25% de ses PV max", d);
        Assert.DoesNotContain("<value1>", d);
        var reset = kb.Descriptions.Tox(kb.InnateTox(Card(kb, "bleu_mange_cailloux"))[1]);
        Assert.StartsWith("Lorsque vous tuez une carte ADVERSE", reset);
        // « Cœur d'ombre » : le repère d'élément devient le nom de l'élément, la valeur est celle du Tox.
        var heart = kb.InnateTox(Card(kb, "aleera_mange_cauchemars"))[1];
        Assert.Contains("Bonus Ombre (+1) par tour de jeu", kb.Descriptions.Tox(heart));
    }

    [Fact]
    public void Tous_les_tox_ont_un_nom_et_une_description_sans_emplacement_restant()
    {
        if (Kb() is not { } kb) return;
        var withDescription = 0;
        foreach (var uuid in kb.Cards.SelectMany(c => c.InnatePassives ?? Array.Empty<string>()).Distinct())
        {
            var t = kb.Tox(uuid)!;
            Assert.NotNull(t);
            Assert.False(string.IsNullOrWhiteSpace(kb.ToxName(t)));
            var d = kb.Descriptions.Tox(t);
            Assert.DoesNotContain("<", d);
            if (d.Length > 0) withDescription++;
        }
        Assert.True(withDescription >= 40, $"{withDescription} Tox innés avec une description");
        Assert.Null(kb.Tox("00000000-0000-0000-0000-000000000000"));
    }

    // ----- Précision, critiques, recharge -----

    [Fact]
    public void Les_donnees_de_precision_et_de_critique_viennent_de_la_capacite()
    {
        if (Kb() is not { } kb) return;
        var s = kb.Descriptions.Stats("Gobe-pierre")!;
        Assert.Equal(100, s.Precision);
        Assert.True(s.CanCrit);
        Assert.Equal(1, s.CritChance);
        Assert.Equal(2, s.CritRatio);
        Assert.Equal(3, s.Cooldown);
        Assert.Null(kb.Descriptions.Stats("capacité inconnue"));
        // La précision est 100 − chance de rater (certaines capacités ratent une fois sur deux).
        var missing = kb.Cards.SelectMany(c => c.Abilities).Select(a => kb.Descriptions.Stats(a)).First(x => x!.ChanceOfMiss == 50)!;
        Assert.Equal(50, missing.Precision);
    }

    // ----- Fiche d'une carte -----

    private (LibraryViewModel Vm, KnowledgeBase Kb, List<CardItem> Items)? Make()
    {
        if (Kb() is not { } kb) return null;
        var items = kb.Cards.Select(c => new CardItem(c, kb)).ToList();
        var vm = new LibraryViewModel(new CardBrowser(items, kb), kb, new DeckLibrary(new DeckStore(_dir)), items);
        return (vm, kb, items);
    }

    [Fact]
    public void La_fiche_affiche_precision_critiques_et_recharge_de_chaque_capacite()
    {
        if (Make() is not { } w) return;
        w.Vm.Browser.Selected = w.Items.First(i => i.Name == "Bleu-mange-cailloux");
        var ability = Assert.Single(w.Vm.AbilityLines);
        Assert.Equal("Gobe-pierre", ability.Name);
        Assert.Equal("Précision 100 % · Critique 1 % (×2) · 3 tours de recharge", ability.Stats);
        Assert.Contains("Cible toutes les cartes adverses", ability.Description);
    }

    [Fact]
    public void La_recharge_s_ecrit_au_singulier_pour_un_tour_et_au_pluriel_au_dela()
    {
        if (Make() is not { } w) return;
        var kb = w.Kb;
        string Stats(int cooldown) => w.Items.SelectMany(i => i.Card.Abilities).Select(a => (a, s: kb.Descriptions.Stats(a)))
            .Where(x => x.s?.Cooldown == cooldown).Select(x => w.Vm.StatsText(x.a)).FirstOrDefault() ?? "";
        Assert.EndsWith(" · 1 tour de recharge", Stats(1));
        Assert.EndsWith(" · 2 tours de recharge", Stats(2));
        Assert.EndsWith(" · 3 tours de recharge", Stats(3));
        Assert.EndsWith(" · 10 tours de recharge", Stats(10));   // le pluriel vaut aussi pour deux chiffres
    }

    [Fact]
    public void Une_capacite_sans_critique_et_une_capacite_imprecise_le_disent()
    {
        if (Make() is not { } w) return;
        var kb = w.Kb;
        var noCrit = w.Items.First(i => i.Card.Abilities.Any(a => kb.Descriptions.Stats(a) is { CanCrit: false }));
        w.Vm.Browser.Selected = noCrit;
        Assert.Contains(w.Vm.AbilityLines, l => l.Stats.Contains("Pas de coup critique"));
        var imprecise = w.Items.First(i => i.Card.Abilities.Any(a => kb.Descriptions.Stats(a) is { ChanceOfMiss: 50 }));
        w.Vm.Browser.Selected = imprecise;
        Assert.Contains(w.Vm.AbilityLines, l => l.Stats.StartsWith("Précision 50 %"));
    }

    [Fact]
    public void Les_resistances_s_affichent_en_pourcentage_avec_leur_icone()
    {
        if (Make() is not { } w) return;
        var card = w.Items.First(i => i.Name == "Bleu-mange-cailloux");
        w.Vm.Browser.Selected = card;
        Assert.Equal(6, w.Vm.ResistanceItems.Count);
        Assert.All(w.Vm.ResistanceItems, r => Assert.EndsWith(" %", r.Text));
        Assert.Contains("Feu 9 %", w.Vm.Resistances);
        Assert.Contains("Ombre -20 %", w.Vm.Resistances);        // une faiblesse garde son signe
        Assert.DoesNotMatch(@"\d(?! %)( ·|$)", w.Vm.Resistances);   // aucun nombre sans « % »
    }

    [Fact]
    public void La_fiche_montre_les_tox_de_base_avec_la_couleur_de_leur_rarete()
    {
        if (Make() is not { } w) return;
        w.Vm.Browser.Selected = w.Items.First(i => i.Name == "Bleu-mange-cailloux");
        Assert.True(w.Vm.HasInnateTox);
        Assert.Equal(2, w.Vm.InnateTox.Count);
        var common = w.Vm.InnateTox[0]; var epic = w.Vm.InnateTox[1];
        Assert.Equal("Phénix", common.Name);
        Assert.Equal("Commun", common.RarityName);
        Assert.Equal("Épique", epic.RarityName);
        Assert.Equal(Color.Parse("#333333"), ((SolidColorBrush)common.Border).Color);
        Assert.Equal(Color.Parse("#8939FD"), ((SolidColorBrush)epic.Border).Color);
        Assert.Equal(Color.Parse("#C06AFF"), ((SolidColorBrush)epic.TextColor).Color);
        Assert.Contains("25%", common.Description);
        // Une carte sans Tox de base n'affiche pas la section.
        w.Vm.Browser.Selected = w.Items.First(i => i.Card.InnatePassives is not { Count: > 0 });
        Assert.False(w.Vm.HasInnateTox);
        Assert.Empty(w.Vm.InnateTox);
    }

    [Fact]
    public void L_en_tete_de_la_fiche_nomme_l_element_et_la_rarete()
    {
        if (Make() is not { } w) return;
        w.Vm.Browser.Selected = w.Items.First(i => i.Name == "Bleu-mange-cailloux");
        Assert.Equal("Terre", w.Vm.ElementName);
        Assert.Equal("Épique", w.Vm.RarityName);
        Assert.Equal("500 PV · Revenu : 4", w.Vm.StatsShort);
        Assert.Equal(Color.Parse("#C06AFF"), ((SolidColorBrush)w.Vm.RarityTextBrush).Color);
    }

    [Fact]
    public void Les_options_de_filtre_et_les_compteurs_ont_une_icone_quand_la_base_en_a()
    {
        if (Make() is not { } w) return;
        var browser = w.Vm.Browser;
        Assert.Equal(8, browser.ElementOptions.Count);     // « tous » + 7
        Assert.Equal(6, browser.RarityOptions.Count);      // « toutes » + 5
        Assert.Null(browser.ElementOptions[0].Icon);       // « Tous les éléments » n'a pas d'icône
    }

    // ----- Règles de présentation des Tox (17/11/2026) -----

    [Fact]
    public void Les_passifs_de_talent_sont_ceux_qui_n_ont_ni_image_ni_carte_ni_boutique()
    {
        if (Kb() is not { } kb) return;
        var passives = kb.AllTox.Where(ToxCatalog.IsTalentPassive).Select(t => t.Title).ToList();
        Assert.Equal(15, passives.Count);
        Assert.All(new[] { "Roulette", "Casino", "Paradoxe", "AttackMultiColor", "Aveuglement", "ChanceVitale", "Changement", "CoeurCorrompu", "PetitPrix",
                           "PlumePhoenix", "Resonance", "ToxChanceux", "InsensibleDisparition" }, n => Assert.Contains(n, passives));
        // Les trois conditions ensemble : un Tox qui n'en remplit qu'une ou deux reste listé.
        Assert.All(kb.AllTox.Where(t => !ToxCatalog.IsTalentPassive(t)), t =>
            Assert.True(!string.IsNullOrEmpty(t.Image) || t.InnateOnCards > 0 || t.ShopPool is not null || t.Title == "Camouflage"));
    }

    [Fact]
    public void Voleur_et_SolCamouflage_ne_sont_pas_listes_et_Camouflage_l_est()
    {
        if (Kb() is not { } kb) return;
        Assert.False(ToxCatalog.IsListed(kb.AllTox.First(t => t.Title == "Voleur_R")));
        Assert.False(ToxCatalog.IsListed(kb.AllTox.First(t => t.Title == "SolCamouflage_R")));
        Assert.True(ToxCatalog.IsListed(kb.AllTox.First(t => t.Title == "Camouflage")));
        Assert.True(ToxCatalog.IsListed(kb.AllTox.First(t => t.Title == "ContreCoup_R")));    // sans carte, sans boutique, mais avec l'image fournie
        Assert.Equal(106, kb.AllTox.Count(ToxCatalog.IsListed));
    }

    [Fact]
    public void Le_prix_de_camouflage_est_70_et_les_autres_viennent_de_la_boutique()
    {
        if (Kb() is not { } kb) return;
        Assert.Equal(70, ToxCatalog.ShopPrice(kb.AllTox.First(t => t.Title == "Camouflage")));
        Assert.Equal(45, ToxCatalog.ShopPrice(kb.AllTox.First(t => t.Title == "Aoe_C")));
        Assert.Null(ToxCatalog.ShopPrice(kb.AllTox.First(t => t.Title == "ContreCoup_C")));
    }

    [Theory]
    [InlineData(1, "1 Spamton")]
    [InlineData(2, "2 Spamtons")]
    [InlineData(70, "70 Spamtons")]
    [InlineData(1000, "1 000 Spamtons")]
    public void Les_prix_s_ecrivent_en_spamtons_avec_un_pluriel_des_2(int amount, string expected) =>
        Assert.Equal(expected, ToxCatalog.PriceText(amount).Replace('\u202f', ' ').Replace('\u00a0', ' '));
}
