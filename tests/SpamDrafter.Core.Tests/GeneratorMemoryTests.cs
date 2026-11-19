using SpamDrafter.App.ViewModels;
using SpamDrafter.Core.Decks;
using SpamDrafter.Core.Knowledge;
using SpamDrafter.Engines;
using SpamDrafter.Engines.History;
using SpamDrafter.Simulator.Data;
using SpamDrafter.Simulator.Engine;

namespace SpamDrafter.Core.Tests;

/// <summary>Phase 12, lot B : l'onglet Génération garde en mémoire ses résultats, a un historique et affiche une note sur 100.</summary>
public class GeneratorMemoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "spamdrafter-genmem-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static string? FindKnowledge()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "knowledge", "1.30");
            if (File.Exists(Path.Combine(p, "manifest.json"))) return p;
        }
        return null;
    }

    private (GeneratorViewModel Vm, KnowledgeBase Kb, GenerationHistory History, Coordinator Coordinator, DeckLibrary Library)? Make(string context = "ctx")
    {
        if (FindKnowledge() is not { } p) return null;
        var kb = KnowledgeBase.Load(p);
        var items = kb.Cards.Select(c => new CardItem(c, kb)).ToList();
        var library = new DeckLibrary(new DeckStore(Path.Combine(_dir, "decks")));
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
        var history = new GenerationHistory(Path.Combine(_dir, "history.json"), context);
        var vm = new GeneratorViewModel(new CardBrowser(items, kb), items, kb, coordinator, library, post: a => a(), history: history);
        return (vm, kb, history, coordinator, library);
    }

    private static void Pick(GeneratorViewModel vm, int index)
    {
        vm.Browser.Selected = vm.Browser.Items[index];
        vm.AddCommand.Execute(null);
    }

    [Fact]
    public async Task Les_propositions_affichent_une_note_sur_100_et_plus_le_pourcentage_avec_marge()
    {
        if (Make() is not { } w) return;
        Pick(w.Vm, 0); Pick(w.Vm, 1); Pick(w.Vm, 2);
        await w.Vm.GenerateAsync();
        Assert.NotEmpty(w.Vm.Proposals);
        foreach (var p in w.Vm.Proposals)
        {
            Assert.Matches(@"^\d{1,3} / 100$", p.NoteText);
            Assert.Contains(p.NoteLabel, new[] { "Intankable", "Faible", "Passable", "Bon", "Excellent", "Parfait" });
            Assert.DoesNotContain("duels de confirmation", p.NoteText + p.NoteLabel + p.NoteHint);
            Assert.DoesNotContain("±", p.NoteText + p.NoteLabel + p.NoteHint);
        }
        Assert.Contains("0 à 100", w.Vm.Status);
        Assert.DoesNotContain("Score =", w.Vm.Status);
    }

    [Fact]
    public async Task La_note_d_une_proposition_est_celle_de_sa_fiche()
    {
        if (Make() is not { } w) return;
        Pick(w.Vm, 0); Pick(w.Vm, 1); Pick(w.Vm, 2);
        await w.Vm.GenerateAsync();
        var proposal = w.Vm.Proposals[0];
        var sheet = await w.Coordinator.AnalyseAsync(proposal.Deck.Deck, complete: false, Effort.Normal, Coordinator.AppSeed);
        Assert.Contains($"Note : {proposal.NoteText} — {proposal.NoteLabel}", sheet.Sheet);
    }

    [Fact]
    public async Task Une_meme_generation_est_reprise_en_memoire_sans_recalcul()
    {
        if (Make() is not { } w) return;
        Pick(w.Vm, 0); Pick(w.Vm, 1); Pick(w.Vm, 2);
        await w.Vm.GenerateAsync();
        var first = w.Vm.Proposals.Select(p => (p.NoteText, string.Join("|", p.Lines))).ToList();
        Assert.Single(w.History.Entries);
        Assert.DoesNotContain("mémoire", w.Vm.Status);

        w.Vm.Proposals.Clear();
        await w.Vm.GenerateAsync();
        Assert.Contains("retrouvé en mémoire", w.Vm.Status);
        Assert.Equal(first, w.Vm.Proposals.Select(p => (p.NoteText, string.Join("|", p.Lines))).ToList());
        Assert.Single(w.History.Entries);   // pas de doublon dans l'historique
    }

    [Fact]
    public async Task La_memoire_survit_a_un_redemarrage_et_change_avec_le_contexte()
    {
        if (Make() is not { } w) return;
        Pick(w.Vm, 0); Pick(w.Vm, 1); Pick(w.Vm, 2);
        await w.Vm.GenerateAsync();
        var again = Make()!.Value;   // « nouvelle ouverture » de l'application : mêmes fichiers
        Pick(again.Vm, 0); Pick(again.Vm, 1); Pick(again.Vm, 2);
        await again.Vm.GenerateAsync();
        Assert.Contains("retrouvé en mémoire", again.Vm.Status);

        var other = Make("autre-contexte")!.Value;   // version de SPAM, méta ou poids différents : rien à reprendre
        Assert.Empty(other.History.Entries);
        Pick(other.Vm, 0); Pick(other.Vm, 1); Pick(other.Vm, 2);
        await other.Vm.GenerateAsync();
        Assert.DoesNotContain("mémoire", other.Vm.Status);
    }

    [Fact]
    public async Task Une_autre_selection_ou_un_autre_effort_recalcule()
    {
        if (Make() is not { } w) return;
        Pick(w.Vm, 0); Pick(w.Vm, 1); Pick(w.Vm, 2);
        await w.Vm.GenerateAsync();
        w.Vm.SelectedEffort = w.Vm.EffortOptions.First(o => o.Value == Effort.Rapide);
        await w.Vm.GenerateAsync();
        Assert.DoesNotContain("mémoire", w.Vm.Status);
        Assert.Equal(2, w.History.Entries.Count);
    }

    [Fact]
    public async Task L_historique_du_Generateur_liste_et_rouvre_les_generations_seulement()
    {
        if (Make() is not { } w) return;
        Pick(w.Vm, 0); Pick(w.Vm, 1);
        await w.Vm.GenerateAsync();
        // Un rapport et une confrontation gardés par l'onglet Tester ne sont pas dans cette liste.
        var deck = w.Kb.Cards.Take(4).Select(c => new DuelSlot(c.Id, c.Talents[0])).ToList();
        w.History.AddReport(deck, false, Effort.Normal, "mon deck", "Fiche du deck — mon deck", "VERDICT", "", 10, 1);
        Assert.Equal(2, w.History.Entries.Count);
        Assert.Single(w.Vm.HistoryItems);
        Assert.Equal("Historique (1)", w.Vm.HistoryTitle);
        Assert.StartsWith("Cuisine", w.Vm.HistoryItems[0].Subtitle);

        // Repartir de zéro puis rouvrir la génération : la sélection et les propositions reviennent.
        w.Vm.ClearCommand.Execute(null);
        w.Vm.Proposals.Clear();
        w.Vm.OpenHistoryCommand.Execute(w.Vm.HistoryItems[0]);
        Assert.Equal(2, w.Vm.Fixed.Count);
        Assert.NotEmpty(w.Vm.Proposals);
        Assert.Contains("rouverte", w.Vm.Status);
    }

    [Fact]
    public async Task Vider_l_historique_du_Generateur_garde_les_rapports_de_Tester()
    {
        if (Make() is not { } w) return;
        Pick(w.Vm, 0); Pick(w.Vm, 1);
        await w.Vm.GenerateAsync();
        var deck = w.Kb.Cards.Take(4).Select(c => new DuelSlot(c.Id, c.Talents[0])).ToList();
        w.History.AddReport(deck, false, Effort.Normal, "mon deck", "t", "VERDICT", "", 10, 1);
        w.Vm.ClearHistoryCommand.Execute(null);
        Assert.Empty(w.Vm.HistoryItems);
        Assert.Single(w.History.Entries);
        Assert.Equal(SpamDrafter.Engines.History.HistoryKind.Report, w.History.Entries[0].Kind);
    }

    [Fact]
    public async Task La_colonne_Resultat_a_des_propositions_apres_la_generation()
    {
        if (Make() is not { } w) return;
        Assert.False(w.Vm.HasProposals);
        Pick(w.Vm, 0); Pick(w.Vm, 1); Pick(w.Vm, 2);
        await w.Vm.GenerateAsync();
        Assert.True(w.Vm.HasProposals);
        w.Vm.Proposals.Clear();
        Assert.False(w.Vm.HasProposals);
    }

    [Fact]
    public void Seuls_les_problemes_sont_affiches_dans_la_colonne_Generateur()
    {
        if (Make() is not { } w) return;
        Assert.False(w.Vm.HasProblem);
        w.Vm.AddCommand.Execute(null);                                  // aucune carte choisie dans la liste
        Assert.True(w.Vm.HasProblem);
        Assert.Contains("Choisis d'abord", w.Vm.Problem);
        Pick(w.Vm, 0);
        Assert.False(w.Vm.HasProblem);                                  // une action réussie efface le message
        w.Vm.Browser.Selected = w.Vm.Browser.Items[0];
        w.Vm.AddCommand.Execute(null);                                  // la même carte une 2e fois
        Assert.Contains("déjà choisie", w.Vm.Problem);
    }

    [Fact]
    public async Task Une_generation_reussie_n_affiche_aucun_message_et_l_effort_reste_normal()
    {
        if (Make() is not { } w) return;
        Pick(w.Vm, 0); Pick(w.Vm, 1); Pick(w.Vm, 2);
        Assert.Equal(Effort.Normal, w.Vm.SelectedEffort.Value);
        await w.Vm.GenerateAsync();
        Assert.True(w.Vm.HasProposals);
        Assert.False(w.Vm.HasProblem);
        Assert.Equal(Effort.Normal, w.Vm.SelectedEffort.Value);
    }

    [Fact]
    public void Comment_ca_marche_met_en_couleur_le_conseil_et_ne_parle_plus_du_bouton_Tester()
    {
        if (Make() is not { } w) return;
        Assert.Contains("{{prends les propositions comme des idées à essayer}}", w.Vm.HowItWorks);
        Assert.DoesNotContain("Tester ce deck", w.Vm.HowItWorks);
        Assert.DoesNotContain("historique", w.Vm.HowItWorks);
    }

    [Fact]
    public async Task Tester_ce_deck_sauvegarde_la_proposition_puis_demande_l_onglet_Tester()
    {
        if (Make() is not { } w) return;
        Pick(w.Vm, 0); Pick(w.Vm, 1); Pick(w.Vm, 2);
        await w.Vm.GenerateAsync();
        var asked = new List<Deck>();
        w.Vm.TestRequested += asked.Add;
        var proposal = w.Vm.Proposals[0];
        Assert.False(proposal.Saved);

        w.Vm.TestCommand.Execute(proposal);
        Assert.True(proposal.Saved);
        var saved = Assert.Single(w.Library.Decks);
        Assert.Equal(DeckOrigin.Generated, saved.Origin);
        Assert.Equal(saved.Id, Assert.Single(asked).Id);

        w.Vm.TestCommand.Execute(proposal);                       // déjà sauvegardée : pas de doublon
        Assert.Single(w.Library.Decks);
        Assert.Equal(2, asked.Count);

        // L'onglet Tester choisit ce deck.
        var tester = new TesterViewModel(w.Library);
        Assert.True(tester.Select(asked[0]));
        Assert.Equal(saved.Name, tester.SelectedTitle);
    }

    [Fact]
    public async Task Retirer_et_vider_l_historique()
    {
        if (Make() is not { } w) return;
        Pick(w.Vm, 0); Pick(w.Vm, 1);
        await w.Vm.GenerateAsync();
        Assert.True(w.Vm.ClearHistoryCommand.CanExecute(null));
        w.Vm.RemoveHistoryCommand.Execute(w.Vm.HistoryItems[0]);
        Assert.Empty(w.Vm.HistoryItems);
        Assert.Equal("Historique (vide)", w.Vm.HistoryTitle);
        Assert.False(w.Vm.ClearHistoryCommand.CanExecute(null));
    }

    [Fact]
    public void Le_contexte_est_stable_et_explique_comment_ca_marche()
    {
        if (Make() is not { } w) return;
        Assert.Equal(w.Coordinator.CacheContext, w.Coordinator.CacheContext);
        Assert.Equal(24, w.Coordinator.CacheContext.Length);
        Assert.Contains("0 à 100", w.Vm.HowItWorks);
        Assert.Contains("simulat", w.Vm.HowItWorks);
    }
}
