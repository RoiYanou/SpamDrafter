using SpamDrafter.App.ViewModels;
using SpamDrafter.Core.Decks;
using SpamDrafter.Core.Knowledge;
using SpamDrafter.Engines;
using SpamDrafter.Simulator.Data;

namespace SpamDrafter.Core.Tests;

/// <summary>Phase 8, lot D : onglet Génération de deck et coordinateur (sans fenêtre).</summary>
public class GeneratorViewModelTests
{
    private static string? FindKnowledge()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "knowledge", "1.30");
            if (File.Exists(Path.Combine(p, "manifest.json"))) return p;
        }
        return null;
    }

    private sealed record World(KnowledgeBase Kb, GeneratorViewModel Vm, DeckLibrary Library, DrafterViewModel Drafter, string Dir) : IDisposable
    {
        public void Dispose() { try { Directory.Delete(Dir, true); } catch (IOException) { } }
    }

    private static World? Make()
    {
        if (FindKnowledge() is not { } p) return null;
        var kb = KnowledgeBase.Load(p);
        var items = kb.Cards.Select(c => new CardItem(c, kb)).ToList();
        var dir = Path.Combine(Path.GetTempPath(), "spamdrafter-tests-" + Guid.NewGuid().ToString("N"));
        var library = new DeckLibrary(new DeckStore(dir));
        // Réglages miniatures : ces tests vérifient l'onglet (boutons, messages, sauvegarde), pas la qualité des moteurs.
        var coordinator = new Coordinator(SimKnowledge.Load(p))
        {
            CookOverride = new SpamDrafter.Engines.Cooking.CookSettings
            {
                Population = 6, Elites = 2, MutantsPerElite = 2, Generations = 2, ChampionEvery = 0, ScreenOpponents = 4,
                ConfirmPlayer = "rapide", ConfirmCandidates = 3, ConfirmOpponents = 4, Proposals = 2,
            },
            AnalysisOverride = SpamDrafter.Engines.Analysis.AnalysisSettings.Sheet with
            {
                Player = "rapide", Opponents = 4, Pairs = 1, TalentPairs = 2, RoleVariants = 2, PoolSize = 20, CalibrationOpponents = 4,
                DangerMinGames = 2, ToxMinGames = 1, MetaPairs = 1, MetaRatingPairs = 1,
            },
        };
        var vm = new GeneratorViewModel(new CardBrowser(items, kb), items, kb, coordinator, library, post: a => a());
        var drafter = new DrafterViewModel(new CardBrowser(items, kb), items, kb, library);
        return new World(kb, vm, library, drafter, dir);
    }

    [Fact]
    public void Selection_de_cartes_sans_doublon_et_au_plus_quatre()
    {
        using var w = Make();
        if (w is null) return;
        var vm = w.Vm;
        Assert.False(vm.GenerateCommand.CanExecute(null));
        foreach (var item in vm.Browser.Items.Take(5)) { vm.Browser.Selected = item; vm.AddCommand.Execute(null); }
        Assert.Equal(4, vm.Fixed.Count);
        vm.Browser.Selected = vm.Browser.Items[0];
        vm.AddCommand.Execute(null);
        Assert.Equal(4, vm.Fixed.Count);
        Assert.True(vm.GenerateCommand.CanExecute(null));
        Assert.Null(vm.Fixed[0].Talent.Id);                       // talent au choix du Cuisinier par défaut
        Assert.Equal(4, vm.Fixed[0].TalentOptions.Count);
        vm.ClearCommand.Execute(null);
        Assert.Empty(vm.Fixed);
    }

    [Fact]
    public async Task Generer_sauvegarder_et_retrouver_dans_le_Drafter()
    {
        using var w = Make();
        if (w is null) return;
        var vm = w.Vm;
        vm.SelectedEffort = vm.EffortOptions[0];   // rapide
        vm.Browser.Selected = vm.Browser.Items[7];
        vm.AddCommand.Execute(null);
        var imposed = vm.Fixed[0].Card.Card.Id;
        await vm.GenerateAsync();
        Assert.False(vm.IsBusy);
        Assert.NotEmpty(vm.Proposals);
        Assert.All(vm.Proposals, p => Assert.Contains(p.Deck.Deck, s => s.CardId == imposed));
        Assert.Contains("(imposée)", vm.Proposals[0].Lines.First(l => l.StartsWith(vm.Fixed[0].Card.Name)));

        vm.SaveCommand.Execute(vm.Proposals[0]);
        Assert.True(vm.Proposals[0].Saved);
        var saved = Assert.Single(w.Library.Decks);
        Assert.Equal(DeckOrigin.Generated, saved.Origin);
        Assert.Equal(w.Kb.Manifest.GameVersion, saved.GameVersion);
        Assert.Equal(4, saved.Slots.Count);
        vm.SaveCommand.Execute(vm.Proposals[0]);                   // pas de double sauvegarde
        Assert.Single(w.Library.Decks);

        // Visible dans le Drafter avec son origine.
        w.Drafter.Browser.Selected = w.Drafter.Browser.Items.First(i => i.Card.Id == imposed);
        w.Drafter.AddCommand.Execute(null);
        var found = Assert.Single(w.Drafter.Results);
        Assert.Contains("deck du chef", found.Badge);
    }

    [Fact]
    public async Task Une_generation_annulee_le_dit()
    {
        using var w = Make();
        if (w is null) return;
        var vm = w.Vm;
        vm.Browser.Selected = vm.Browser.Items[3];
        vm.AddCommand.Execute(null);
        var task = vm.GenerateAsync();
        Assert.True(vm.IsBusy);
        Assert.True(vm.CancelCommand.CanExecute(null));
        Assert.False(vm.GenerateCommand.CanExecute(null));
        vm.CancelCommand.Execute(null);
        await task;
        Assert.False(vm.IsBusy);
        Assert.Contains("annulée", vm.Status);
        Assert.Empty(vm.Proposals);
    }

    [Fact]
    public async Task Le_coordinateur_refuse_deux_calculs_a_la_fois()
    {
        if (FindKnowledge() is not { } p) return;
        var c = new Coordinator(SimKnowledge.Load(p));
        var sim = SimKnowledge.Load(p);
        var card = sim.Cards.OrderBy(x => x.Id, StringComparer.Ordinal).First().Id;
        using var cts = new CancellationTokenSource();
        var first = c.GenerateAsync([new SpamDrafter.Engines.Cooking.FixedCard(card)], Effort.Rapide, cancel: cts.Token);
        Assert.True(c.IsBusy);
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.GenerateAsync([new SpamDrafter.Engines.Cooking.FixedCard(card)], Effort.Rapide));
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.False(c.IsBusy);
    }

    [Fact]
    public void Les_anciens_decks_de_reference_ne_changent_plus_les_adversaires()
    {
        // D-065 : la génération se fait toujours contre la méta synthétique (ou au hasard sans elle), jamais contre
        // les decks du joueur, même ceux qu'il avait cochés « référence » avant la 1.0.2.
        if (FindKnowledge() is not { } p) return;
        var kb = KnowledgeBase.Load(p);
        var dir = Path.Combine(Path.GetTempPath(), "spamdrafter-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var library = new DeckLibrary(new DeckStore(dir));
            var coordinator = new Coordinator(SimKnowledge.Load(p), SyntheticData.Load(p, SimKnowledge.Load(p)));
            var before = coordinator.Opponents;
            for (var i = 0; i < 3; i++)
                library.Save(new Deck
                {
                    Name = $"Ancien témoin {i}", IsReference = true, GameVersion = kb.Manifest.GameVersion,
                    Slots = kb.Cards.Skip(4 * i).Take(4).Select(c => new DeckSlot(c.Id, null)).ToList(),
                });
            Assert.Equal(before, coordinator.Opponents);
            if (File.Exists(SpamDrafter.Engines.Meta.SyntheticMeta.DefaultPath(p)))
                Assert.Equal(OpponentSource.Synthetic, coordinator.Opponents);
            // Le drapeau des anciens fichiers est conservé (outil de mesure du Studio).
            Assert.Equal(3, new DeckStore(dir).LoadAll().Count(d => d.IsReference));
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Fact]
    public void Le_rapport_s_affiche_en_texte_lisible()
    {
        var text = HistoryText.ToPlain("# Analyse du deck\n\n## Force\n- **Carte** : 50 %\n\n| A | B |\n|---|---|\n| 1 | 2 |\n");
        Assert.DoesNotContain("**", text);
        Assert.DoesNotContain("#", text);
        Assert.Contains("ANALYSE DU DECK", text);
        Assert.Contains("| 1 | 2 |", text);
        Assert.DoesNotContain("|---|", text);
    }
}
