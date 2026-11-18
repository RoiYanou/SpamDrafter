using System.Text.Json;
using System.Text.Json.Nodes;
using SpamDrafter.App.ViewModels;
using SpamDrafter.Core.Decks;
using SpamDrafter.Core.Knowledge;
using SpamDrafter.Core.Settings;
using SpamDrafter.Core.Versions;

namespace SpamDrafter.Core.Tests;

/// <summary>Phase 12, lot E : différences entre deux versions de SPAM, impact sur les decks.</summary>
public class Phase12LotETests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "spamdrafter-lote-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private static string? FindKnowledge()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "knowledge", "1.30");
            if (File.Exists(Path.Combine(p, "manifest.json"))) return p;
        }
        return null;
    }

    /// <summary>Ce que contient la « version 1.31 » fabriquée à partir de la 1.30 pour les essais.</summary>
    private sealed record Fake(string OldDir, string NewDir, KnowledgeBase Old, KnowledgeBase New,
        string HpCard, string RemovedCard, string AbilityCard, string AbilityId, string TalentSwapCard, string TalentChangedId,
        string RemovedTalentId, string RemovedTalentCard);

    private static readonly string[] Needed = { "manifest.json", "cards.json", "talents.json", "elements.json", "rarities.json", "abilities.json" };

    /// <summary>
    /// Copie les fichiers utiles de la 1.30 dans un dossier « 1.31 » puis y change : les PV d'une carte, une carte retirée, les valeurs
    /// d'une capacité, une carte qui change de talent, un talent modifié, un talent retiré (inutilisé).
    /// </summary>
    private Fake? MakeFake()
    {
        if (FindKnowledge() is not { } oldDir) return null;
        var newDir = Path.Combine(_root, "1.31");
        Directory.CreateDirectory(Path.Combine(newDir, "texts"));
        foreach (var f in Needed) File.Copy(Path.Combine(oldDir, f), Path.Combine(newDir, f), overwrite: true);
        File.Copy(Path.Combine(oldDir, "texts", "fr.json"), Path.Combine(newDir, "texts", "fr.json"), overwrite: true);
        var old = KnowledgeBase.Load(oldDir);

        JsonNode Read(string f) => JsonNode.Parse(File.ReadAllText(Path.Combine(newDir, f)))!;
        void Write(string f, JsonNode n) => File.WriteAllText(Path.Combine(newDir, f), n.ToJsonString());

        var manifest = Read("manifest.json");
        manifest["gameVersion"] = "1.31";
        Write("manifest.json", manifest);

        var cards = Read("cards.json").AsArray();
        string Id(JsonNode c) => c["id"]!.GetValue<string>();
        // 1. PV d'une carte
        var hpCard = cards[0]!;
        hpCard["hp"] = hpCard["hp"]!.GetValue<int>() + 100;
        // 2. carte retirée
        var removed = cards[1]!;
        cards.RemoveAt(1);
        // 3. capacité modifiée : on prend une capacité d'une carte, on change ses valeurs
        var abilities = Read("abilities.json").AsArray();
        var abilityCardNode = cards.First(c => c!["abilities"]!.AsArray().Count > 0 && c["id"]!.GetValue<string>() != Id(hpCard))!;
        var abilityId = abilityCardNode["abilities"]![0]!.GetValue<string>();
        var abilityNode = abilities.First(a => a!["id"]!.GetValue<string>() == abilityId)!;
        var action = abilityNode["actions"]!.AsArray()[0]!;
        action["min"] = action["min"]!.GetValue<double>() + 7;
        action["max"] = action["max"]!.GetValue<double>() + 7;
        Write("abilities.json", abilities);
        // 4. talents : un talent modifié, une carte qui change son talent n°2, un talent retiré (nous le rendons inutilisé)
        var talents = Read("talents.json").AsArray();
        var usage = cards.SelectMany(c => c!["talents"]!.AsArray().Select(t => t!.GetValue<string>())).GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
        var unusedOrSingle = talents.Select(t => t!["id"]!.GetValue<string>()).First(t => usage.GetValueOrDefault(t) == 1);
        var removedTalentCard = cards.First(c => c!["talents"]!.AsArray().Any(t => t!.GetValue<string>() == unusedOrSingle))!;
        var replacement = talents.Select(t => t!["id"]!.GetValue<string>()).First(t => t != unusedOrSingle && usage.GetValueOrDefault(t) > 1);
        var tals = removedTalentCard["talents"]!.AsArray();
        for (var i = 0; i < tals.Count; i++) if (tals[i]!.GetValue<string>() == unusedOrSingle) tals[i] = replacement;
        talents.Remove(talents.First(t => t!["id"]!.GetValue<string>() == unusedOrSingle));
        var changedTalent = talents.First(t => t!["id"]!.GetValue<string>() != replacement && usage.GetValueOrDefault(t!["id"]!.GetValue<string>()) > 1 && t["actions"]!.AsArray().Count > 0)!;
        var changedTalentId = changedTalent["id"]!.GetValue<string>();
        var tAction = changedTalent["actions"]!.AsArray()[0]!;
        tAction["max"] = tAction["max"]!.GetValue<double>() + 3;
        Write("talents.json", talents);
        Write("cards.json", cards);

        return new Fake(oldDir, newDir, old, KnowledgeBase.Load(newDir), Id(hpCard), Id(removed), Id(abilityCardNode), abilityId,
            Id(removedTalentCard), changedTalentId, unusedOrSingle, Id(removedTalentCard));
    }

    // ----- Fichier de différences -----

    [Fact]
    public void Deux_fois_la_meme_version_ne_donne_aucune_difference()
    {
        if (FindKnowledge() is not { } dir) return;
        var cs = VersionDiff.Compute(dir, dir);
        Assert.True(cs.IsEmpty);
        Assert.Equal("1.30", cs.From);
        Assert.Equal("1.30", cs.To);
    }

    [Fact]
    public void Les_differences_d_une_version_fabriquee_sont_retrouvees()
    {
        if (MakeFake() is not { } f) return;
        var cs = VersionDiff.Compute(f.OldDir, f.NewDir);
        Assert.Equal("1.30", cs.From);
        Assert.Equal("1.31", cs.To);

        // PV
        var hp = Assert.Single(cs.Card(f.HpCard)!.Fields, x => x.Field == "hp");
        Assert.Equal(int.Parse(hp.Before!) + 100, int.Parse(hp.After!));
        // Carte retirée
        Assert.Equal(ChangeKind.Removed, cs.Card(f.RemovedCard)!.Kind);
        Assert.Equal(f.Old.CardName(f.Old.FindCard(f.RemovedCard)!), cs.Card(f.RemovedCard)!.Name);
        // Capacité modifiée : dans la liste des capacités, et signalée sur les cartes qui la gardent
        var ability = cs.Ability(f.AbilityId)!;
        Assert.Equal(ChangeKind.Changed, ability.Kind);
        Assert.Contains(cs.Card(f.AbilityCard)!.Fields, x => x.Field == "ability.changed" && x.Before == f.AbilityId);
        // Talent modifié, talent retiré, talent n°x remplacé sur la carte
        Assert.Equal(ChangeKind.Changed, cs.Talent(f.TalentChangedId)!.Kind);
        Assert.Equal(ChangeKind.Removed, cs.Talent(f.RemovedTalentId)!.Kind);
        Assert.Contains(cs.Card(f.TalentSwapCard)!.Fields, x => x.Field == "talents" && x.Before!.Contains(f.RemovedTalentId));
        // Les cartes qui n'ont pas bougé n'apparaissent pas.
        Assert.DoesNotContain(cs.Cards, c => c.Kind == ChangeKind.Changed && c.Fields.Count == 0);
        Assert.True(cs.Cards.Count < 20);
    }

    [Fact]
    public void Une_capacite_dont_seule_une_donnee_interne_change_est_signalee_comme_telle()
    {
        if (MakeFake() is not { } f) return;
        // On modifie une donnée qui n'apparaît pas dans la description : le délai de recharge.
        var path = Path.Combine(f.NewDir, "abilities.json");
        var abilities = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        var other = abilities.First(a => a!["id"]!.GetValue<string>() != f.AbilityId && a["cooldown"] is not null)!;
        var otherId = other["id"]!.GetValue<string>();
        other["cooldown"] = other["cooldown"]!.GetValue<double>() + 5;
        File.WriteAllText(path, abilities.ToJsonString());
        var cs = VersionDiff.Compute(f.OldDir, f.NewDir);
        Assert.Equal(ChangeKind.Changed, cs.Ability(otherId)!.Kind);
        Assert.True(cs.Ability(otherId)!.InternalOnly);
        Assert.False(cs.Ability(f.AbilityId)!.InternalOnly);   // celle-là change aussi la description
    }

    [Fact]
    public void Le_fichier_se_relit_et_ignore_ce_qu_il_ne_comprend_pas()
    {
        if (MakeFake() is not { } f) return;
        var cs = VersionDiff.Compute(f.OldDir, f.NewDir);
        var path = Path.Combine(_root, "changes", ChangeSet.FileName("1.30"));
        cs.Write(path);
        var back = ChangeSet.TryRead(path)!;
        Assert.Equal(cs.Cards.Count, back.Cards.Count);
        Assert.Equal(cs.Cards[0].Fields, back.Cards[0].Fields);
        Assert.Equal(cs.Talents.Select(t => t.Kind), back.Talents.Select(t => t.Kind));
        Assert.Equal("from-1.30.json", ChangeSet.FileName("1.30"));
        Assert.Equal("from-1.3_0_x.json", ChangeSet.FileName("1.3/0 x"));

        Assert.Null(ChangeSet.TryRead(Path.Combine(_root, "absent.json")));
        File.WriteAllText(Path.Combine(_root, "abime.json"), "{ pas du json");
        Assert.Null(ChangeSet.TryRead(Path.Combine(_root, "abime.json")));
        File.WriteAllText(Path.Combine(_root, "futur.json"), "{\"formatVersion\": 99, \"from\":\"1\", \"to\":\"2\", \"cards\":[], \"talents\":[], \"abilities\":[]}");
        Assert.Null(ChangeSet.TryRead(Path.Combine(_root, "futur.json")));   // format inconnu : on n'affirme rien
    }

    [Fact]
    public void La_base_retrouve_les_differences_depuis_une_version()
    {
        if (MakeFake() is not { } f) return;
        VersionDiff.Compute(f.OldDir, f.NewDir).Write(Path.Combine(f.NewDir, "changes", ChangeSet.FileName("1.30")));
        var kb = KnowledgeBase.Load(f.NewDir);
        Assert.NotNull(kb.ChangesFrom("1.30"));
        Assert.Null(kb.ChangesFrom("1.20"));      // pas de fichier : on ne dit rien
        Assert.Null(kb.ChangesFrom("1.31"));      // la version actuelle elle-même
        Assert.Null(kb.ChangesFrom(""));
    }

    // ----- Texte et impact sur un deck -----

    private static Deck DeckOf(params (string Card, string? Talent)[] slots) =>
        new() { Name = "d", GameVersion = "1.30", Slots = slots.Select(s => new DeckSlot(s.Card, s.Talent)).ToList() };

    [Fact]
    public void Les_differences_sont_ecrites_en_francais()
    {
        if (MakeFake() is not { } f) return;
        var cs = VersionDiff.Compute(f.OldDir, f.NewDir);
        var hp = cs.Card(f.HpCard)!.Fields.First(x => x.Field == "hp");
        Assert.Matches(@"^PV : \d+ → \d+$", ChangeText.Describe(hp, cs, f.New));
        var ability = cs.Card(f.AbilityCard)!.Fields.First(x => x.Field == "ability.changed");
        var text = ChangeText.Describe(ability, cs, f.New);
        Assert.StartsWith("Capacité « ", text);
        Assert.Contains("modifiée : ", text);
        Assert.Contains(" → ", text);
        var talents = cs.Card(f.TalentSwapCard)!.Fields.First(x => x.Field == "talents");
        Assert.Matches(@"Talent \d : .+ → .+", ChangeText.Describe(talents, cs, f.New));
        // Résistance et élément
        Assert.Equal("Résistance Feu : 10 % → 15 %", ChangeText.Describe(new FieldChange("resistance.fire", "10", "15"), cs, f.New));
        Assert.Equal("Revenu : 10 → 12", ChangeText.Describe(new FieldChange("goldIncome", "10", "12"), cs, f.New));
        Assert.Equal("Élément : Feu → Eau", ChangeText.Describe(new FieldChange("element", "fire", "water"), cs, f.New));
    }

    [Fact]
    public void Un_deck_touche_par_la_mise_a_jour_liste_ses_differences()
    {
        if (MakeFake() is not { } f) return;
        var cs = VersionDiff.Compute(f.OldDir, f.NewDir);
        var untouched = f.New.Cards.First(c => cs.Card(c.Id) is null && c.Talents.All(t => cs.Talent(t) is null)).Id;
        var deck = DeckOf((f.HpCard, null), (untouched, null));
        var impact = ChangeText.Impact(cs, deck, f.New);
        Assert.True(impact.IsImpacted);
        Assert.False(impact.Broken);
        Assert.Contains(impact.Lines, l => l.Contains("PV : "));
        Assert.All(impact.Lines, l => Assert.DoesNotContain(f.New.CardName(f.New.FindCard(untouched)!) + " —", l));   // la carte intacte n'apparaît pas
    }

    [Fact]
    public void Un_deck_sans_changement_n_est_pas_signale()
    {
        if (MakeFake() is not { } f) return;
        var cs = VersionDiff.Compute(f.OldDir, f.NewDir);
        var untouched = f.New.Cards.Where(c => cs.Card(c.Id) is null && c.Talents.All(t => cs.Talent(t) is null)).Take(2).Select(c => (c.Id, (string?)c.Talents[0])).ToArray();
        var impact = ChangeText.Impact(cs, DeckOf(untouched), f.New);
        Assert.False(impact.IsImpacted);
    }

    [Fact]
    public void Une_carte_ou_un_talent_supprime_rend_le_deck_invalide()
    {
        if (MakeFake() is not { } f) return;
        var cs = VersionDiff.Compute(f.OldDir, f.NewDir);
        var removedCard = ChangeText.Impact(cs, DeckOf((f.RemovedCard, null)), f.New);
        Assert.True(removedCard.Broken);
        Assert.Contains("carte supprimée du jeu", removedCard.Lines[0]);

        var other = f.New.Cards.First(c => cs.Card(c.Id) is null).Id;
        var removedTalent = ChangeText.Impact(cs, DeckOf((other, f.RemovedTalentId)), f.New);
        Assert.True(removedTalent.Broken);
        Assert.Contains("supprimé du jeu", removedTalent.Lines[0]);
    }

    [Fact]
    public void Un_talent_modifie_est_signale_seulement_s_il_est_choisi()
    {
        if (MakeFake() is not { } f) return;
        var cs = VersionDiff.Compute(f.OldDir, f.NewDir);
        var card = f.New.Cards.First(c => c.Talents.Contains(f.TalentChangedId) && cs.Card(c.Id) is null);
        var chosen = ChangeText.Impact(cs, DeckOf((card.Id, f.TalentChangedId)), f.New);
        Assert.Contains(chosen.Lines, l => l.Contains("talent « ") && l.Contains("modifié"));
        var other = card.Talents.First(t => t != f.TalentChangedId && cs.Talent(t) is null);
        Assert.False(ChangeText.Impact(cs, DeckOf((card.Id, other)), f.New).IsImpacted);
    }

    [Fact]
    public void Le_quoi_de_neuf_resume_toute_la_version()
    {
        if (MakeFake() is not { } f) return;
        var cs = VersionDiff.Compute(f.OldDir, f.NewDir);
        var lines = ChangeText.Summary(cs, f.New);
        Assert.Contains(lines, l => l.StartsWith("Cartes retirées (1) : "));
        Assert.Contains(lines, l => l.Contains("PV : "));
        Assert.Contains(lines, l => l.StartsWith("Talents retirés (1) : "));
        Assert.Contains(lines, l => l.StartsWith("Talent « ") && l.Contains("modifié"));
        Assert.Empty(ChangeText.Summary(VersionDiff.Compute(f.OldDir, f.OldDir), f.Old));
    }

    // ----- Deck builder : version de création et différences -----

    private sealed record Builder(DeckBuilderViewModel Vm, DeckLibrary Library, KnowledgeBase Kb, ChangeSet Changes, Fake Fake);

    private Builder? MakeBuilder()
    {
        if (MakeFake() is not { } f) return null;
        var cs = VersionDiff.Compute(f.OldDir, f.NewDir);
        cs.Write(Path.Combine(f.NewDir, "changes", ChangeSet.FileName("1.30")));
        var kb = KnowledgeBase.Load(f.NewDir);
        var items = kb.Cards.Select(c => new CardItem(c, kb)).ToList();
        var library = new DeckLibrary(new DeckStore(Path.Combine(_root, "decks")));
        return new Builder(new DeckBuilderViewModel(new CardBrowser(items, kb), items, kb, library) { AllowIncompleteDecks = true }, library, kb, cs, f);
    }

    private static Deck Saved(DeckLibrary lib, string name, string version, params (string Card, string? Talent)[] slots)
    {
        var d = new Deck { Name = name, GameVersion = version, Slots = slots.Select(s => new DeckSlot(s.Card, s.Talent)).ToList() };
        lib.Save(d);
        return d;
    }

    private static string Untouched(Builder w, int skip = 0) =>
        w.Kb.Cards.Where(c => w.Changes.Card(c.Id) is null && c.Talents.All(t => w.Changes.Talent(t) is null)).Skip(skip).First().Id;

    [Fact]
    public void Un_deck_de_la_version_actuelle_affiche_seulement_sa_version()
    {
        if (MakeBuilder() is not { } w) return;
        Assert.Equal("Version de SPAM : 1.31", w.Vm.VersionText);   // nouveau deck
        Assert.False(w.Vm.HasVersionNotice);
        var deck = Saved(w.Library, "Récent", "1.31", (w.Fake.HpCard, null));
        w.Vm.SelectedSaved = w.Vm.SavedDecks.First(d => d.Id == deck.Id);
        Assert.Equal("Version de SPAM : 1.31", w.Vm.VersionText);
        Assert.False(w.Vm.HasVersionLines);
        Assert.False(w.Vm.CanMarkChecked);
    }

    [Fact]
    public void Un_deck_d_une_ancienne_version_liste_les_differences_qui_le_touchent()
    {
        if (MakeBuilder() is not { } w) return;
        var deck = Saved(w.Library, "Ancien", "1.30", (w.Fake.HpCard, null), (Untouched(w), null));
        w.Vm.SelectedSaved = w.Vm.SavedDecks.First(d => d.Id == deck.Id);
        Assert.Equal("Créé avec SPAM 1.30 · version actuelle : 1.31", w.Vm.VersionText);
        Assert.StartsWith("⚠ ", w.Vm.VersionNotice);
        Assert.True(w.Vm.HasVersionLines);
        Assert.Contains(w.Vm.VersionLines, l => l.Contains("PV : "));
        Assert.False(w.Vm.VersionBroken);
        Assert.True(w.Vm.CanMarkChecked);
    }

    [Fact]
    public void Un_deck_non_touche_le_dit()
    {
        if (MakeBuilder() is not { } w) return;
        var deck = Saved(w.Library, "Intact", "1.30", (Untouched(w), null), (Untouched(w, 1), null));
        w.Vm.SelectedSaved = w.Vm.SavedDecks.First(d => d.Id == deck.Id);
        Assert.StartsWith("Aucune différence", w.Vm.VersionNotice);
        Assert.False(w.Vm.HasVersionLines);
        Assert.True(w.Vm.CanMarkChecked);   // on peut quand même le passer à la version actuelle
    }

    [Fact]
    public void Sans_fichier_de_differences_pour_sa_version_le_deck_ne_pretend_rien()
    {
        if (MakeBuilder() is not { } w) return;
        var deck = Saved(w.Library, "Très ancien", "1.20", (w.Fake.HpCard, null));
        w.Vm.SelectedSaved = w.Vm.SavedDecks.First(d => d.Id == deck.Id);
        Assert.Contains("ne sont pas disponibles", w.Vm.VersionNotice);
        Assert.False(w.Vm.HasVersionLines);
        Assert.False(ChangeText.ImpactOf(deck, w.Kb).IsImpacted);
    }

    [Fact]
    public void Les_differences_suivent_les_modifications_du_deck()
    {
        if (MakeBuilder() is not { } w) return;
        var deck = Saved(w.Library, "Ancien", "1.30", (w.Fake.HpCard, null), (Untouched(w), null));
        w.Vm.SelectedSaved = w.Vm.SavedDecks.First(d => d.Id == deck.Id);
        Assert.True(w.Vm.HasVersionLines);
        w.Vm.RemoveCommand.Execute(w.Vm.Slots.First(s => s.Card.Card.Id == w.Fake.HpCard));   // on retire la carte modifiée
        Assert.False(w.Vm.HasVersionLines);
        Assert.StartsWith("Aucune différence", w.Vm.VersionNotice);
        Assert.Equal("Créé avec SPAM 1.30 · version actuelle : 1.31", w.Vm.VersionText);        // la version de création ne bouge pas
    }

    [Fact]
    public void Une_carte_supprimee_rend_le_deck_invalide()
    {
        if (MakeBuilder() is not { } w) return;
        var deck = new Deck { Name = "Cassé", GameVersion = "1.30", Slots = { new DeckSlot(w.Fake.RemovedCard, null), new DeckSlot(Untouched(w), null) } };
        var impact = ChangeText.ImpactOf(deck, w.Kb);
        Assert.True(impact.Broken);
        Assert.True(impact.IsImpacted);
    }

    [Fact]
    public void Marquer_un_deck_verifie_le_passe_a_la_version_actuelle_sans_toucher_a_ses_cartes()
    {
        if (MakeBuilder() is not { } w) return;
        var deck = Saved(w.Library, "Ancien", "1.30", (w.Fake.HpCard, null), (Untouched(w), null));
        w.Vm.SelectedSaved = w.Vm.SavedDecks.First(d => d.Id == deck.Id);
        w.Vm.MarkCheckedCommand.Execute(null);
        Assert.Contains("vérifié", w.Vm.Status);
        Assert.Equal("Version de SPAM : 1.31", w.Vm.VersionText);
        Assert.False(w.Vm.CanMarkChecked);
        Assert.False(w.Vm.IsDirty);
        var onDisk = new DeckStore(Path.Combine(_root, "decks")).LoadAll().Single();
        Assert.Equal("1.31", onDisk.GameVersion);
        Assert.Equal(2, onDisk.Slots.Count);
        Assert.Equal("Ancien", onDisk.Name);
    }

    [Fact]
    public void Un_deck_modifie_puis_sauvegarde_garde_sa_version_de_creation()
    {
        if (MakeBuilder() is not { } w) return;
        var deck = Saved(w.Library, "Ancien", "1.30", (w.Fake.HpCard, null));
        w.Vm.SelectedSaved = w.Vm.SavedDecks.First(d => d.Id == deck.Id);
        w.Vm.Name = "Ancien renommé";
        w.Vm.SaveCommand.Execute(null);
        Assert.Equal("1.30", new DeckStore(Path.Combine(_root, "decks")).LoadAll().Single().GameVersion);
    }

    [Fact]
    public void La_liste_signale_et_filtre_les_decks_touches()
    {
        if (MakeBuilder() is not { } w) return;
        Saved(w.Library, "Touché", "1.30", (w.Fake.HpCard, null));
        Saved(w.Library, "Intact", "1.30", (Untouched(w), null));
        Saved(w.Library, "Récent", "1.31", (w.Fake.HpCard, null));
        w.Vm.ClearDeckFiltersCommand.Execute(null);
        Assert.True(w.Vm.SavedDecks.Single(d => d.Name == "Touché").VersionImpacted);
        Assert.False(w.Vm.SavedDecks.Single(d => d.Name == "Intact").VersionImpacted);
        Assert.False(w.Vm.SavedDecks.Single(d => d.Name == "Récent").VersionImpacted);   // créé avec la version actuelle
        Assert.Equal(1, w.Vm.ImpactedDeckCount);
        Assert.True(w.Vm.HasImpactedDecks);

        w.Vm.ImpactedOnly = true;
        Assert.Equal(new[] { "Touché" }, w.Vm.SavedDecks.Select(d => d.Name));
        w.Vm.ClearDeckFiltersCommand.Execute(null);
        Assert.False(w.Vm.ImpactedOnly);
        Assert.Equal(3, w.Vm.SavedDecks.Count);
    }

    [Fact]
    public void Le_filtre_n_est_pas_propose_quand_aucun_deck_n_est_concerne_et_disparait_avec_le_dernier()
    {
        if (MakeBuilder() is not { } w) return;
        Assert.False(w.Vm.HasImpactedDecks);                 // aucun deck
        Saved(w.Library, "Intact", "1.30", (Untouched(w), null));
        Saved(w.Library, "Récent", "1.31", (w.Fake.HpCard, null));
        Assert.False(w.Vm.HasImpactedDecks);                 // des decks, mais aucun touché
        var touched = Saved(w.Library, "Touché", "1.30", (w.Fake.HpCard, null));
        Assert.True(w.Vm.HasImpactedDecks);

        w.Vm.ImpactedOnly = true;
        Assert.Single(w.Vm.SavedDecks);
        w.Vm.SelectedSaved = w.Vm.SavedDecks[0];
        w.Vm.MarkCheckedCommand.Execute(null);               // le dernier deck concerné passe à la version actuelle
        Assert.False(w.Vm.HasImpactedDecks);
        Assert.False(w.Vm.ImpactedOnly);                     // le filtre ne reste pas actif, invisible
        Assert.Equal(3, w.Vm.SavedDecks.Count);              // la liste n'est pas vide pour autant
        Assert.Equal("1.31", w.Library.Decks.First(d => d.Id == touched.Id).GameVersion);
    }

    // ----- Quoi de neuf -----

    private static WhatsNewViewModel NewWhatsNew(Builder w, AppSettings settings, string path, int impacted = 0) =>
        new(w.Kb, settings, () => settings.TrySave(path), () => impacted);

    [Fact]
    public void A_la_premiere_ouverture_le_quoi_de_neuf_ne_montre_rien_et_retient_la_version()
    {
        if (MakeBuilder() is not { } w) return;
        var path = Path.Combine(_root, "settings.json");
        var settings = new AppSettings();
        var vm = NewWhatsNew(w, settings, path);
        Assert.False(vm.IsVisible);
        Assert.Equal("1.31", settings.LastSeenGameVersion);
        Assert.Equal("1.31", AppSettings.Load(path).LastSeenGameVersion);
    }

    [Fact]
    public void Quand_la_version_change_le_quoi_de_neuf_resume_puis_se_ferme()
    {
        if (MakeBuilder() is not { } w) return;
        var path = Path.Combine(_root, "settings.json");
        var settings = new AppSettings { LastSeenGameVersion = "1.30" };
        var vm = NewWhatsNew(w, settings, path, impacted: 2);
        Assert.True(vm.IsVisible);
        Assert.Equal("SPAM est passé de la version 1.30 à la version 1.31", vm.Title);
        Assert.Contains("carte(s)", vm.Summary);
        Assert.NotEmpty(vm.Lines);
        Assert.Contains(vm.Lines, l => l.StartsWith("Cartes retirées"));
        Assert.True(vm.HasImpactedDecks);
        Assert.Equal("Voir mes decks touchés (2)", vm.ImpactedLabel);

        Assert.False(vm.DetailVisible);
        vm.ToggleDetailCommand.Execute(null);
        Assert.True(vm.DetailVisible);
        Assert.Equal("Masquer le détail", vm.DetailButtonLabel);

        var asked = false;
        vm.ShowImpactedRequested += () => asked = true;
        vm.ShowImpactedCommand.Execute(null);
        Assert.True(asked);

        vm.DismissCommand.Execute(null);
        Assert.False(vm.IsVisible);
        Assert.Equal("1.31", AppSettings.Load(path).LastSeenGameVersion);   // retenu : il ne revient pas
        Assert.False(NewWhatsNew(w, AppSettings.Load(path), path).IsVisible);
    }

    [Fact]
    public void Le_quoi_de_neuf_ne_s_affiche_pas_pour_la_meme_version_et_reste_honnete_sans_fichier()
    {
        if (MakeBuilder() is not { } w) return;
        var path = Path.Combine(_root, "settings.json");
        Assert.False(NewWhatsNew(w, new AppSettings { LastSeenGameVersion = "1.31" }, path).IsVisible);
        var unknown = NewWhatsNew(w, new AppSettings { LastSeenGameVersion = "1.20" }, path);
        Assert.True(unknown.IsVisible);
        Assert.Contains("ne sont pas disponibles", unknown.Summary);
        Assert.Empty(unknown.Lines);
        Assert.False(unknown.DetailVisible);
    }
}
