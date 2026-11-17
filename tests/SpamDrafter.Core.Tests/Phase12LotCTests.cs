using SpamDrafter.App.ViewModels;
using SpamDrafter.Core.Decks;
using SpamDrafter.Core.Knowledge;
using SpamDrafter.Core.Settings;

namespace SpamDrafter.Core.Tests;

/// <summary>Phase 12, lot C : lore et filtres, tri, fiche carte, descriptions chiffrées (KI-021).</summary>
public class Phase12LotCTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "spamdrafter-lotc-" + Guid.NewGuid().ToString("N"));

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

    private static List<CardItem> Items(KnowledgeBase kb) => kb.Cards.Select(c => new CardItem(c, kb)).ToList();

    // ----- Lore -----

    [Fact]
    public void Les_151_cartes_ont_un_lore_avec_espece_region_et_chronologie()
    {
        if (Kb() is not { } kb) return;
        Assert.All(kb.Cards, c =>
        {
            var lore = kb.Lore(c);
            Assert.NotNull(lore);
            Assert.NotEmpty(lore!.Species);
            Assert.NotEmpty(lore.Region);
            Assert.NotEmpty(lore.Chronology);
            Assert.NotEmpty(lore.Description);
        });
        var all = kb.Cards.Select(kb.Lore).ToList();
        Assert.Equal(25, all.Select(l => l!.Species).Distinct().Count());
        Assert.Equal(17, all.Select(l => l!.Region).Distinct().Count());
        Assert.Equal(5, all.Select(l => l!.Chronology).Distinct().Count());
    }

    [Fact]
    public void Le_lore_d_une_carte_connue()
    {
        if (Kb() is not { } kb) return;
        var card = kb.Cards.First(c => kb.CardName(c) == "Oisillons de Midas");
        var lore = kb.Lore(card)!;
        Assert.Equal("Créature", lore.Species);
        Assert.Equal("Aelther", lore.Region);
        Assert.Equal("Ère prospère", lore.Chronology);
        Assert.StartsWith("Alors que le monde", lore.Description);
    }

    // ----- Filtres -----

    [Fact]
    public void Les_filtres_de_lore_se_cumulent_avec_les_autres_et_se_reinitialisent()
    {
        if (Kb() is not { } kb) return;
        var items = Items(kb);
        var b = new CardBrowser(items, kb);
        Assert.Equal(151, b.Items.Count);
        Assert.Equal(26, b.SpeciesOptions.Count);      // « toutes » + 25
        Assert.Equal(18, b.RegionOptions.Count);
        Assert.Equal(6, b.ChronologyOptions.Count);

        b.Region = b.RegionOptions.First(o => o.Id == "Aelther");
        Assert.NotEmpty(b.Items);
        Assert.All(b.Items, i => Assert.Equal("Aelther", i.Lore!.Region));
        var regionCount = b.Items.Count;

        b.Species = b.SpeciesOptions.First(o => o.Id == "Créature");
        Assert.All(b.Items, i => { Assert.Equal("Aelther", i.Lore!.Region); Assert.Equal("Créature", i.Lore.Species); });
        Assert.InRange(b.Items.Count, 1, regionCount);
        Assert.True(b.HasLoreFilter);
        Assert.Contains("filtre actif", b.AdvancedHeader);

        b.Chronology = b.ChronologyOptions.First(o => o.Id == "Ère prospère");
        b.Search = "oisillons";
        Assert.Contains(b.Items, i => i.Name == "Oisillons de Midas");

        b.ClearFilters();
        Assert.Equal(151, b.Items.Count);
        Assert.False(b.HasLoreFilter);
        Assert.Equal("", b.Search);
    }

    [Fact]
    public void Les_especes_et_regions_sont_classees_sans_tenir_compte_des_accents()
    {
        if (Kb() is not { } kb) return;
        var b = new CardBrowser(Items(kb), kb);
        foreach (var options in new[] { b.RegionOptions, b.SpeciesOptions })
        {
            var labels = options.Skip(1).Select(o => SpamDrafter.Core.Text.TextSearch.Normalize(o.Label)).ToList();
            Assert.Equal(labels.OrderBy(x => x, StringComparer.Ordinal), labels);
        }
    }

    [Fact]
    public void Les_periodes_sont_classees_de_la_plus_ancienne_a_la_plus_recente()
    {
        if (Kb() is not { } kb) return;
        var b = new CardBrowser(Items(kb), kb);
        Assert.Equal(new[] { "Aube primordiale", "Âge des Divinités", "Grand Cataclysme", "Malédiction des Rois", "Ère prospère" },
            b.ChronologyOptions.Skip(1).Select(o => o.Label));
    }

    [Fact]
    public void Les_filtres_et_le_tri_sont_retenus_dans_les_reglages()
    {
        if (Kb() is not { } kb) return;
        var items = Items(kb);
        var b = new CardBrowser(items, kb);
        b.Region = b.RegionOptions.First(o => o.Id == "Aelther");
        b.Sort = b.SortOptions.First(o => o.Id == "hp");
        var saved = b.CaptureFilters();
        Assert.Equal("Aelther", saved.Region);
        Assert.Equal("hp", saved.Sort);

        var other = new CardBrowser(items, kb);
        other.ApplyFilters(saved);
        Assert.Equal("Aelther", other.Region.Id);
        Assert.Equal("hp", other.Sort.Id);
        Assert.Equal(b.Items.Select(i => i.Card.Id), other.Items.Select(i => i.Card.Id));

        // Une valeur qui n'existe plus (autre version du jeu) est ignorée.
        other.ApplyFilters(new BrowserSettings { Region = "Région disparue", Sort = "inconnu" });
        Assert.Null(other.Region.Id);
        Assert.Equal("order", other.Sort.Id);
        Assert.Equal(151, other.Items.Count);
    }

    // ----- Tri -----

    [Fact]
    public void Tri_par_pv_revenu_nom_element_et_rarete()
    {
        if (Kb() is not { } kb) return;
        var b = new CardBrowser(Items(kb), kb);
        Assert.Equal(kb.Cards.Select(c => c.Id), b.Items.Select(i => i.Card.Id));   // ordre du jeu par défaut

        b.Sort = b.SortOptions.First(o => o.Id == "hp");
        Assert.Equal(b.Items.Select(i => i.Card.Hp).OrderByDescending(x => x), b.Items.Select(i => i.Card.Hp));
        // À PV égaux : l'ordre du jeu (jamais le hasard).
        foreach (var group in b.Items.GroupBy(i => i.Card.Hp))
            Assert.Equal(group.Select(i => i.Card.Order).Order(), group.Select(i => i.Card.Order));

        b.Sort = b.SortOptions.First(o => o.Id == "income");
        Assert.Equal(b.Items.Select(i => i.Card.GoldIncome).OrderByDescending(x => x), b.Items.Select(i => i.Card.GoldIncome));

        b.Sort = b.SortOptions.First(o => o.Id == "name");
        var names = b.Items.Select(i => SpamDrafter.Core.Text.TextSearch.Normalize(i.Name)).ToList();
        Assert.Equal(names.OrderBy(x => x, StringComparer.Ordinal), names);

        b.Sort = b.SortOptions.First(o => o.Id == "element");
        var elementOrder = kb.Elements.Select(e => e.Id).ToList();
        Assert.Equal(b.Items.Select(i => elementOrder.IndexOf(i.Card.Element)).Order(), b.Items.Select(i => elementOrder.IndexOf(i.Card.Element)));

        b.Sort = b.SortOptions.First(o => o.Id == "rarity");
        var rarityOrder = kb.Rarities.Select(r => r.Id).ToList();
        Assert.Equal(b.Items.Select(i => rarityOrder.IndexOf(i.Card.Rarity)).OrderByDescending(x => x), b.Items.Select(i => rarityOrder.IndexOf(i.Card.Rarity)));
    }

    [Fact]
    public void Le_tri_s_applique_apres_les_filtres()
    {
        if (Kb() is not { } kb) return;
        var b = new CardBrowser(Items(kb), kb);
        b.Element = b.ElementOptions.First(o => o.Id == "fire");
        b.Sort = b.SortOptions.First(o => o.Id == "hp");
        Assert.All(b.Items, i => Assert.Equal("fire", i.Card.Element));
        Assert.Equal(b.Items.Select(i => i.Card.Hp).OrderByDescending(x => x), b.Items.Select(i => i.Card.Hp));
    }

    // ----- Descriptions chiffrées (KI-021) -----

    [Fact]
    public void Aucune_capacite_ni_talent_ne_garde_d_emplacement_de_valeur()
    {
        if (Kb() is not { } kb) return;
        var f = kb.Descriptions;
        foreach (var c in kb.Cards)
            foreach (var a in c.Abilities)
            {
                var d = f.Ability(a);
                Assert.NotEmpty(d);
                Assert.DoesNotContain("<", d);
                Assert.DoesNotContain("[FEU]", d);
            }
        var withText = 0;
        foreach (var t in kb.Talents)
        {
            var d = f.Talent(t.Id);
            Assert.DoesNotContain("<", d);
            if (d.Length > 0) withText++;
        }
        Assert.True(withText >= 90, $"{withText} talents avec une description sur {kb.Talents.Count}");
    }

    [Fact]
    public void Descriptions_connues()
    {
        if (Kb() is not { } kb) return;
        var f = kb.Descriptions;
        Assert.Equal("Attaque 10 Feu\nBonus Feu (+1)", f.Ability("Souffle de feu"));
        Assert.Equal("Attaque 35 Eau\nEcho (50%)", f.Ability("Hydrochance"));
        Assert.Contains("Usage Limité 1", f.Ability("Chenille"));
        Assert.Equal("Bonus précision (+10 %)", f.Talent("Depart precis"));
        // Capacité à choix : les options sont séparées par « Ou ».
        var choice = f.Ability("Choix Terrestre");
        Assert.Contains("\nOu\n", choice);
        Assert.Contains("[1 à 20]", choice);
    }

    private static AbilityRecord Record(params (double Min, double Max)[] values) =>
        new("test", values.Select(v => new AbilityAction("damage", v.Min, v.Max)).ToList(), 3, 4, 150, Array.Empty<string>(), false);

    [Fact]
    public void Les_emplacements_de_valeurs_suivent_le_calcul_du_jeu()
    {
        if (Kb() is not { } kb) return;
        var f = kb.Descriptions;
        // valeur = moyenne tronquée ; min / max tels quels ; D = arrondi à l'entier le plus proche (moitié vers le pair).
        Assert.Equal("7 | 5 | 10", f.Resolve("<value1> | <min1> | <max1>", Record((5, 10))));
        Assert.Equal("7 | 5 | 10", f.Resolve("<value1D> | <min1D> | <max1D>", Record((5, 10))));
        Assert.Equal("2 | 4", f.Resolve("<min1D> | <max1D>", Record((2.5, 3.5))));
        Assert.Equal("5", f.Resolve("<value1>", Record((-5, -5))));           // valeur absolue
        Assert.Equal("8 | 3", f.Resolve("<value2> | <value1>", Record((3, 3), (8, 8))));
        Assert.Equal("3 tours, 100 %, 4 fois", f.Resolve("<duration> tours, <chanceOfReset> %, <limitedUse1> fois", Record((1, 1))));
        Assert.Equal("Usage Limité 4", f.Resolve("Usage Limité <limitedUse1D>/<limitedUse1>", Record((1, 1))));
        Assert.Equal("Bonus X", f.Resolve("Bonus <missingHpBonusD>", Record((1, 1))));
    }

    [Fact]
    public void Les_icones_et_couleurs_du_jeu_deviennent_du_texte_simple()
    {
        if (Kb() is not { } kb) return;
        var f = kb.Descriptions;
        Assert.Equal("Attaque 10 Feu", f.Resolve("Attaque 10 FEU[FEU]", Record()));
        Assert.Equal("Bonus Lumière", f.Resolve("Bonus LUMIERE[LUMIERE]", Record()));
        Assert.Equal("Gagne un critique", f.Resolve("Gagne un [CRITIQUE]", Record()));
        Assert.Equal("5 dégâts", f.Resolve("<color=#00ff00>5</color> <sprite=4> dégâts", Record()).Replace("  ", " ").Replace(" ", " "));
        Assert.Equal("a\nb", f.Resolve("  a  \r\n  b  ", Record()));
    }

    // ----- Fiche carte -----

    private (LibraryViewModel Vm, KnowledgeBase Kb, DeckLibrary Decks, List<CardItem> Items)? MakeLibrary()
    {
        if (Kb() is not { } kb) return null;
        var items = Items(kb);
        var decks = new DeckLibrary(new DeckStore(_dir));
        var vm = new LibraryViewModel(new CardBrowser(items, kb), kb, decks, items);
        return (vm, kb, decks, items);
    }

    [Fact]
    public void La_fiche_affiche_le_lore_les_capacites_et_les_talents_chiffres()
    {
        if (MakeLibrary() is not { } w) return;
        var card = w.Items.First(i => i.Name == "Oisillons de Midas");
        w.Vm.Browser.Selected = card;
        Assert.True(w.Vm.HasLore);
        Assert.Equal("Créature · Aelther · Ère prospère", w.Vm.LoreTags);
        Assert.StartsWith("Alors que le monde", w.Vm.LoreText);
        Assert.Equal(card.Card.Abilities.Count, w.Vm.AbilityLines.Count);
        Assert.All(w.Vm.AbilityLines, l => { Assert.NotEmpty(l.Description); Assert.DoesNotContain("<", l.Description); });
        Assert.Equal("Piou-piou-piou", w.Vm.AbilityLines[0].Name);
        Assert.Equal(3, w.Vm.Talents.Count);
        Assert.All(w.Vm.Talents, t => Assert.DoesNotContain("à venir", t.Description));
        // Talents numérotés, dans l'ordre de la carte.
        Assert.Equal(new[] { 1, 2, 3 }, w.Vm.Talents.Select((t, i) => t.Name.StartsWith($"Talent {i + 1} : ") ? i + 1 : -1));
        Assert.Equal($"Talent 2 : {w.Kb.TalentName(card.Card.Talents[1])}", w.Vm.Talents[1].Name);
    }

    [Fact]
    public void La_fiche_liste_mes_decks_qui_utilisent_la_carte_et_les_ouvre()
    {
        if (MakeLibrary() is not { } w) return;
        var used = w.Items[0]; var other = w.Items[5];
        w.Decks.Save(new Deck { Name = "Mon feu", Favorite = true, GameVersion = "1.30", Slots = { new DeckSlot(used.Card.Id, null), new DeckSlot(other.Card.Id, null) } });
        w.Decks.Save(new Deck { Name = "Généré — x", Origin = DeckOrigin.Generated, GameVersion = "1.30", Slots = { new DeckSlot(used.Card.Id, null) } });
        w.Decks.Save(new Deck { Name = "Sans elle", GameVersion = "1.30", Slots = { new DeckSlot(other.Card.Id, null) } });

        w.Vm.Browser.Selected = used;
        Assert.Equal(2, w.Vm.DecksUsing.Count);
        Assert.Equal("Dans mes decks (2)", w.Vm.DecksUsingTitle);
        Assert.Contains(w.Vm.DecksUsing, l => l.Label == "★ Mon feu (brouillon)");
        Assert.Contains(w.Vm.DecksUsing, l => l.Label.StartsWith("⚙ Généré"));
        Assert.DoesNotContain(w.Vm.DecksUsing, l => l.Label.Contains("Sans elle"));

        Deck? opened = null;
        w.Vm.OpenDeckRequested += d => opened = d;
        w.Vm.OpenDeckCommand.Execute(w.Vm.DecksUsing[0]);
        Assert.NotNull(opened);

        // La liste suit les changements de decks.
        w.Decks.Delete(w.Decks.Decks.First(d => d.Name == "Mon feu").Id);
        Assert.Single(w.Vm.DecksUsing);
    }
}
