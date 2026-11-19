using SpamDrafter.App.ViewModels;
using SpamDrafter.Core.Decks;
using SpamDrafter.Core.Knowledge;
using SpamDrafter.Engines;
using SpamDrafter.Engines.Analysis;
using SpamDrafter.Engines.History;
using SpamDrafter.Simulator.Data;

namespace SpamDrafter.Core.Tests;

/// <summary>Phase 14, lot D : mode « Comparer 2 decks » de l'onglet Tester.</summary>
public class Phase14LotDViewTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "spamdrafter-p14dv-" + Guid.NewGuid().ToString("N"));
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

    private sealed record World(TesterViewModel Tester, DeckLibrary Library, GenerationHistory History, string HistoryPath, Coordinator Coordinator, KnowledgeBase Kb);

    private World? Make(string? historyPath = null)
    {
        if (FindKnowledge() is not { } p) return null;
        var kb = KnowledgeBase.Load(p);
        var coordinator = new Coordinator(SimKnowledge.Load(p))
        {
            ConfrontationOverride = new ConfrontationSettings { Player = "rapide", Pairs = 8 },
            AnalysisOverride = AnalysisSettings.Sheet with
            {
                Player = "rapide", Opponents = 4, Pairs = 1, TalentPairs = 2, RoleVariants = 2, PoolSize = 20, CalibrationOpponents = 4,
                DangerMinGames = 2, ToxMinGames = 1, MetaPairs = 1, MetaRatingPairs = 1,
            },
        };
        var lib = new DeckLibrary(new DeckStore(Path.Combine(_dir, "decks")));
        var path = historyPath ?? Path.Combine(_dir, "history.json");
        var history = new GenerationHistory(path, coordinator.CacheContext);
        var tester = new TesterViewModel(lib, null, coordinator, history, post: a => a());
        return new World(tester, lib, history, path, coordinator, kb);
    }

    private static Deck MakeDeck(KnowledgeBase kb, string name, int skip) =>
        new() { Name = name, Slots = kb.Cards.Skip(skip).Take(4).Select(c => new DeckSlot(c.Id, null)).ToList() };

    private static World? TwoDecks(Func<World?> make, out TesterDeck a, out TesterDeck b)
    {
        a = b = null!;
        if (make() is not { } w) return null;
        w.Library.Save(MakeDeck(w.Kb, "A", 0));
        w.Library.Save(MakeDeck(w.Kb, "B", 4));
        a = w.Tester.Results.First(r => r.Name == "A");
        b = w.Tester.Results.First(r => r.Name == "B");
        return w;
    }

    [Fact]
    public void Les_decks_cliques_remplissent_A_puis_B_et_la_liste_ne_garde_pas_de_selection()
    {
        if (TwoDecks(() => Make(), out var a, out var b) is not { } w) return;
        var t = w.Tester;
        t.IsCompare = true;
        Assert.True(t.ActiveA);
        Assert.Contains("Choisis deux decks", t.CompareHint);

        t.Selected = a;
        Assert.Equal("A", t.TitleA);
        Assert.True(t.ActiveB);                       // le camp actif passe à B
        Assert.Null(t.Selected);                      // la liste ne retient rien : on peut recliquer
        t.Selected = b;
        Assert.Equal("B", t.TitleB);
        Assert.Equal(4, t.ChipsA.Count);
        Assert.False(t.HasCompareHint);
        Assert.True(t.CompareCommand.CanExecute(null));

        t.ActivateACommand.Execute(null);             // remplacer A par un autre deck
        t.Selected = b;
        Assert.Equal("B", t.TitleA);
        Assert.True(t.HasCompareHint);                // A et B identiques : refusé
        Assert.Contains("mêmes cartes", t.CompareHint);
        Assert.False(t.CompareCommand.CanExecute(null));
    }

    [Fact]
    public void Retirer_et_echanger_les_decks()
    {
        if (TwoDecks(() => Make(), out var a, out var b) is not { } w) return;
        var t = w.Tester;
        t.IsCompare = true;
        t.Selected = a; t.Selected = b;
        t.SwapCommand.Execute(null);
        Assert.Equal("B", t.TitleA);
        Assert.Equal("A", t.TitleB);
        t.ClearACommand.Execute(null);
        Assert.False(t.HasA);
        Assert.True(t.ActiveA);
        Assert.False(t.CompareCommand.CanExecute(null));
    }

    [Fact, Trait("Categorie", "Lent")]
    public async Task Comparer_donne_le_verdict_le_texte_et_une_barre_de_0_a_100()
    {
        if (TwoDecks(() => Make(), out var a, out var b) is not { } w) return;
        var t = w.Tester;
        t.IsCompare = true;
        t.Selected = a; t.Selected = b;
        Assert.False(t.HasCompareResult);

        await t.CompareAsync();

        Assert.True(t.HasCompareResult);
        Assert.False(t.IsBusy);
        Assert.Equal("A contre B", t.CompareTitle);
        Assert.Equal("Deck A", t.CompareNameA);                // le texte parle de « Deck A » et « Deck B » (colorés)…
        Assert.Equal("Deck B", t.CompareNameB);
        Assert.Equal("A", t.CompareTipA);                      // …le vrai nom est dans l'info-bulle
        Assert.Equal("B", t.CompareTipB);
        Assert.InRange(t.CompareBar, 0, 100);
        Assert.StartsWith("Deck A", t.CompareScoreText);
        Assert.Contains("%", t.CompareScoreText);
        Assert.False(string.IsNullOrWhiteSpace(t.CompareVerdict));
        Assert.StartsWith("NOTES CONTRE LA MÉTA", t.CompareReport);   // le verdict est dans la barre, au-dessus
        Assert.Contains("NOTES CONTRE LA MÉTA", t.CompareReport);     // les deux notes sont mesurées
        Assert.DoesNotContain("CARTES", t.CompareReport);          // les cartes sont dans un tableau à part
        Assert.Equal(4, t.CardsA.Count);
        Assert.Equal(4, t.CardsB.Count);
        Assert.All(t.CardsA.Concat(t.CardsB), row => { Assert.EndsWith(" %", row.Fall); Assert.False(string.IsNullOrEmpty(row.Damage)); Assert.NotNull(row.Chip); });
        Assert.Contains("change une fois sur deux", t.Status);
        Assert.DoesNotContain("duels simulés", t.Status);        // le nombre de duels n'est plus cité
        Assert.NotNull(w.History.FindComparison(a.Deck.Slots.Select(s => new SpamDrafter.Simulator.Engine.DuelSlot(s.CardId, s.TalentId)).ToList(),
            b.Deck.Slots.Select(s => new SpamDrafter.Simulator.Engine.DuelSlot(s.CardId, s.TalentId)).ToList()));
    }

    [Fact, Trait("Categorie", "Lent")]
    public async Task Une_confrontation_deja_faite_revient_de_la_memoire_et_l_echange_est_une_autre_demande()
    {
        if (TwoDecks(() => Make(), out var a, out var b) is not { } w) return;
        var t = w.Tester;
        t.IsCompare = true;
        t.Selected = a; t.Selected = b;
        await t.CompareAsync();
        var score = t.CompareScoreText;

        // Autre séance : même fichier d'historique, nouvel onglet.
        var again = Make(w.HistoryPath)!;
        again.Library.Reload();
        again.Tester.IsCompare = true;
        again.Tester.Selected = again.Tester.Results.First(r => r.Name == "A");
        again.Tester.Selected = again.Tester.Results.First(r => r.Name == "B");
        Assert.True(again.Tester.HasCompareResult);                    // affiché dès le choix des deux decks
        Assert.Equal(score, again.Tester.CompareScoreText);
        Assert.Contains("mémoire", again.Tester.Status);

        again.Tester.SwapCommand.Execute(null);                         // B contre A : pas encore calculé
        Assert.False(again.Tester.HasCompareResult);
        again.Tester.SwapCommand.Execute(null);                         // retour : de nouveau en mémoire
        Assert.True(again.Tester.HasCompareResult);
    }

    [Fact, Trait("Categorie", "Lent")]
    public async Task Un_deck_supprime_quitte_la_comparaison_et_le_mode_un_deck_reste_intact()
    {
        if (TwoDecks(() => Make(), out var a, out var b) is not { } w) return;
        var t = w.Tester;
        t.Selected = a;                                  // mode « Un deck » : A choisi
        t.IsCompare = true;
        Assert.Null(t.Selected);
        Assert.False(t.HasSelection);
        t.Selected = a; t.Selected = b;
        await t.CompareAsync();
        w.Library.Delete(b.Deck.Id);
        Assert.False(t.HasB);
        Assert.False(t.HasCompareResult);
        t.IsCompare = false;
        Assert.True(t.HasSelection);                     // le deck choisi en mode « Un deck » est toujours là
        Assert.Equal("A", t.SelectedTitle);
    }

    [Fact, Trait("Categorie", "Lent")]
    public async Task Le_Generateur_n_affiche_pas_les_confrontations_dans_son_historique()
    {
        if (TwoDecks(() => Make(), out var a, out var b) is not { } w) return;
        w.Tester.IsCompare = true;
        w.Tester.Selected = a; w.Tester.Selected = b;
        await w.Tester.CompareAsync();
        Assert.Single(w.History.Entries.Where(e => e.Kind == HistoryKind.Comparison));

        var items = w.Kb.Cards.Select(c => new CardItem(c, w.Kb)).ToList();
        var gen = new GeneratorViewModel(new CardBrowser(items, w.Kb), items, w.Kb, w.Coordinator, w.Library, post: act => act(), history: w.History);
        Assert.Empty(gen.HistoryItems);
    }

    [Fact]
    public void Sans_moteur_le_mode_comparer_ne_calcule_rien()
    {
        if (FindKnowledge() is not { } p) return;
        var kb = KnowledgeBase.Load(p);
        var lib = new DeckLibrary(new DeckStore(Path.Combine(_dir, "decks2")));
        lib.Save(MakeDeck(kb, "A", 0)); lib.Save(MakeDeck(kb, "B", 4));
        var t = new TesterViewModel(lib);
        t.IsCompare = true;
        t.Selected = t.Results[0]; t.Selected = t.Results[1];
        Assert.False(t.CanCompute);
        Assert.False(t.CompareCommand.CanExecute(null));
    }
}
