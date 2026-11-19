using SpamDrafter.App.ViewModels;
using SpamDrafter.Core.Decks;
using SpamDrafter.Core.Knowledge;
using SpamDrafter.Engines;
using SpamDrafter.Engines.History;
using SpamDrafter.Simulator.Data;
using SpamDrafter.Simulator.Engine;

namespace SpamDrafter.Core.Tests;

/// <summary>Phase 14, lot B : mode « Un deck » de l'onglet Tester (note, Fiche rapport, mémoire).</summary>
public class Phase14LotBTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "spamdrafter-p14b-" + Guid.NewGuid().ToString("N"));
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

    /// <summary>Réglages miniatures : on vérifie l'onglet (boutons, messages, mémoire), pas la qualité du moteur.</summary>
    private World? Make(bool withEngine = true, string? historyPath = null, GenerationHistory? sharedHistory = null)
    {
        if (FindKnowledge() is not { } p) return null;
        var kb = KnowledgeBase.Load(p);
        var coordinator = new Coordinator(SimKnowledge.Load(p))
        {
            AnalysisOverride = SpamDrafter.Engines.Analysis.AnalysisSettings.Sheet with
            {
                Player = "rapide", Opponents = 4, Pairs = 1, TalentPairs = 2, RoleVariants = 2, PoolSize = 20, CalibrationOpponents = 4,
                DangerMinGames = 2, ToxMinGames = 1, MetaPairs = 1, MetaRatingPairs = 1,
            },
        };
        var lib = new DeckLibrary(new DeckStore(Path.Combine(_dir, "decks")));
        var path = historyPath ?? Path.Combine(_dir, "history.json");
        var history = sharedHistory ?? new GenerationHistory(path, coordinator.CacheContext);
        var tester = new TesterViewModel(lib, null, withEngine ? coordinator : null, withEngine ? history : null, post: a => a());
        return new World(tester, lib, history, path, coordinator, kb);
    }

    private static Deck MakeDeck(KnowledgeBase kb, string name, int skip = 0) =>
        new() { Name = name, Slots = kb.Cards.Skip(skip).Take(4).Select(c => new DeckSlot(c.Id, null)).ToList() };

    [Fact]
    public void Sans_moteur_l_onglet_ne_calcule_rien()
    {
        var w = Make(withEngine: false); if (w is null) return;
        w.Library.Save(MakeDeck(w.Kb, "A"));
        w.Tester.Selected = w.Tester.Results[0];
        Assert.False(w.Tester.CanCompute);
        Assert.False(w.Tester.TestCommand.CanExecute(null));
    }

    [Fact]
    public async Task Tester_un_deck_donne_la_note_et_la_fiche()
    {
        var w = Make(); if (w is null) return;
        w.Library.Save(MakeDeck(w.Kb, "A"));
        var t = w.Tester;
        Assert.False(t.TestCommand.CanExecute(null));   // aucun deck choisi
        t.Selected = t.Results[0];
        Assert.True(t.TestCommand.CanExecute(null));
        Assert.False(t.HasReport);

        await t.TestAsync(complete: false);

        Assert.True(t.HasReport);
        Assert.Matches(@"^\d{1,3} / 100$", t.NoteText);
        Assert.Contains(t.NoteLabel, new[] { "Intankable", "Faible", "Passable", "Bon", "Excellent", "Parfait" });
        Assert.Contains("VERDICT", t.Report);
        Assert.Contains("Fiche du deck — A", t.ReportTitle);
        Assert.False(t.IsBusy);
        Assert.False(t.CanShowDetail);             // le détail n'existe qu'après l'analyse complète
        Assert.True(t.CanRunComplete);
        Assert.Contains("duels simulés", t.Status);
    }

    [Fact, Trait("Categorie", "Lent")]
    public async Task La_note_est_celle_de_la_fiche_du_moteur()
    {
        var w = Make(); if (w is null) return;
        var deck = MakeDeck(w.Kb, "A");
        w.Library.Save(deck);
        w.Tester.Selected = w.Tester.Results[0];
        await w.Tester.TestAsync(false);
        var slots = deck.Slots.Select(s => new DuelSlot(s.CardId, s.TalentId)).ToList();
        var direct = await w.Coordinator.AnalyseAsync(slots, false, Effort.Normal, Coordinator.AppSeed);
        var n = SpamDrafter.Engines.Analysis.DeckNote.From(direct.Report.ForNote.Value, direct.Report.ForNote.Margin95);
        Assert.Equal(n.Text, w.Tester.NoteText);
    }

    [Fact, Trait("Categorie", "Lent")]
    public async Task Un_deck_deja_teste_s_affiche_depuis_la_memoire_sans_calcul()
    {
        var w = Make(); if (w is null) return;
        w.Library.Save(MakeDeck(w.Kb, "A"));
        w.Tester.Selected = w.Tester.Results[0];
        await w.Tester.TestAsync(false);
        var note = w.Tester.NoteText;

        // Autre séance : même fichier d'historique, nouvel onglet.
        var again = Make(historyPath: w.HistoryPath)!;
        again.Library.Reload();
        again.Tester.Selected = again.Tester.Results[0];
        Assert.True(again.Tester.HasReport);                    // affiché dès le choix du deck
        Assert.Equal(note, again.Tester.NoteText);
        Assert.Contains("mémoire", again.Tester.Status);
        Assert.False(again.Coordinator.IsBusy);
    }

    [Fact, Trait("Categorie", "Lent")]
    public async Task Un_rapport_garde_sans_note_est_recalcule()
    {
        var w = Make(); if (w is null) return;
        var deck = MakeDeck(w.Kb, "A");
        w.Library.Save(deck);
        var slots = deck.Slots.Select(s => new DuelSlot(s.CardId, s.TalentId)).ToList();
        w.History.AddReport(slots, false, Effort.Normal, "A", "ancien", "VERDICT ancien", "", 10, 1);   // sans note : avant la phase 14
        w.Tester.Selected = w.Tester.Results[0];
        Assert.False(w.Tester.HasReport);                        // pas repris : la note manque
        await w.Tester.TestAsync(false);
        Assert.True(w.Tester.HasReport);
        Assert.NotNull(w.History.FindReport(slots, false, Effort.Normal)!.NoteScore);
    }

    [Fact, Trait("Categorie", "Lent")]
    public async Task Analyse_complete_donne_le_detail_et_garde_la_meme_note()
    {
        var w = Make(); if (w is null) return;
        w.Library.Save(MakeDeck(w.Kb, "A"));
        var t = w.Tester;
        t.Selected = t.Results[0];
        await t.TestAsync(false);
        var note = t.NoteText;
        var sheet = t.Report;

        await t.TestAsync(true);

        Assert.Equal(note, t.NoteText);                          // même note partout (D-074)
        Assert.True(t.CanShowDetail);
        Assert.False(t.CanRunComplete);
        Assert.True(t.ShowDetail);
        var detail = t.Report;
        Assert.NotEqual(sheet, detail);
        t.ToggleDetailCommand.Execute(null);
        Assert.NotEqual(detail, t.Report);
        Assert.Contains("VERDICT", t.Report);                    // retour à la fiche
        Assert.Equal("Voir le détail", t.DetailButtonLabel);
    }

    [Fact, Trait("Categorie", "Lent")]
    public async Task Changer_de_deck_efface_le_rapport()
    {
        var w = Make(); if (w is null) return;
        w.Library.Save(MakeDeck(w.Kb, "A"));
        w.Library.Save(MakeDeck(w.Kb, "B", skip: 4));
        var t = w.Tester;
        t.Selected = t.Results.First(r => r.Name == "A");
        await t.TestAsync(false);
        Assert.True(t.HasReport);
        t.Selected = t.Results.First(r => r.Name == "B");
        Assert.False(t.HasReport);
        Assert.Equal("", t.NoteText);
        t.Selected = t.Results.First(r => r.Name == "A");        // revenir : rapport repris de la mémoire
        Assert.True(t.HasReport);
    }

    [Fact, Trait("Categorie", "Lent")]
    public async Task Renommer_le_deck_garde_le_rapport_car_les_cartes_sont_les_memes()
    {
        var w = Make(); if (w is null) return;
        var deck = MakeDeck(w.Kb, "A");
        w.Library.Save(deck);
        w.Tester.Selected = w.Tester.Results[0];
        await w.Tester.TestAsync(false);
        var renamed = w.Library.Decks[0];
        renamed.Name = "A bis";
        w.Library.Save(renamed);
        Assert.True(w.Tester.HasReport);
        Assert.Equal("A bis", w.Tester.SelectedTitle);
    }

    [Fact]
    public void L_historique_garde_la_note_d_un_rapport()
    {
        if (FindKnowledge() is not { } p) return;
        var path = Path.Combine(_dir, "h.json");
        var slots = new List<DuelSlot> { new("a", null), new("b", null), new("c", null), new("d", null) };
        var h = new GenerationHistory(path, "ctx");
        h.AddReport(slots, false, Effort.Normal, "x", "t", "s", "d", 5, 1, new SpamDrafter.Engines.Analysis.Measured(0.31, 0.07, 80));
        var again = new GenerationHistory(path, "ctx").FindReport(slots, false, Effort.Normal)!;
        Assert.Equal(0.31, again.NoteScore);
        Assert.Equal(0.07, again.NoteMargin95);
        Assert.Equal(80, again.NoteGames);
    }
}
