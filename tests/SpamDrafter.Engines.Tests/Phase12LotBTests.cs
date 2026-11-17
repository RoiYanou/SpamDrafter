using SpamDrafter.Engines;
using SpamDrafter.Engines.Analysis;
using SpamDrafter.Engines.History;
using SpamDrafter.Simulator.Data;
using SpamDrafter.Simulator.Engine;
using SpamDrafter.Simulator.Players;

namespace SpamDrafter.Engines.Tests;

/// <summary>Phase 12, lot B : note sur 100 et mémoire des générations / rapports.</summary>
public class Phase12LotBTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "spamdrafter-hist-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private string Path1 => Path.Combine(_dir, "history.json");

    private static IReadOnlyList<DuelSlot> Sel(params string[] cards) => cards.Select(c => new DuelSlot(c, null)).ToList();

    private static IReadOnlyList<GeneratedDeck> Props(double score = 0.55) =>
        new[] { new GeneratedDeck(new[] { new DuelSlot("a", "t1"), new DuelSlot("b", null) }, score, 0.12, 52, 20, true) };

    // ----- Note sur 100 (D-074) -----

    [Theory]
    [InlineData(0.00, 0, "Faible")]
    [InlineData(0.05, 0, "Faible")]            // plancher : presque aucune victoire
    [InlineData(0.10, 11, "Faible")]
    [InlineData(0.20, 33, "Faible")]
    [InlineData(0.23, 40, "Correct")]
    [InlineData(0.30, 56, "Bon")]
    [InlineData(0.40, 78, "Excellent")]
    [InlineData(0.50, 100, "Excellent")]       // plafond : il gagne la moitié de ses duels contre la méta
    [InlineData(0.75, 100, "Excellent")]
    public void La_note_va_de_0_a_100_selon_les_victoires(double score, int points, string label)
    {
        var n = DeckNote.From(score, 0.05);
        Assert.Equal(points, n.Points);
        Assert.Equal(label, n.Label);
        Assert.Equal($"{points} / 100", n.Text);
        Assert.False(n.NeedsConfirmation);
    }

    [Fact]
    public void La_note_peut_atteindre_100_et_0_et_croit_avec_les_victoires()
    {
        Assert.Equal(100, DeckNote.From(DeckNote.Ceiling, 0).Points);
        Assert.Equal(0, DeckNote.From(DeckNote.Floor, 0).Points);
        var previous = -1.0;
        for (var s = 0.0; s <= 1.0; s += 0.01)
        {
            var v = DeckNote.Scale(s);
            Assert.InRange(v, previous, 100);   // jamais décroissante, jamais hors de 0 à 100
            previous = v;
        }
    }

    [Fact]
    public void La_note_est_a_confirmer_quand_la_marge_est_grande_et_reste_dans_0_100()
    {
        Assert.True(DeckNote.From(0.5, 0.20).NeedsConfirmation);
        Assert.False(DeckNote.From(0.5, DeckNote.ConfirmMargin).NeedsConfirmation);
        Assert.Equal(100, DeckNote.From(1.4, 0).Points);
        Assert.Equal(0, DeckNote.From(-0.2, 0).Points);
    }

    [Fact]
    public void L_explication_decrit_l_echelle_et_la_meta_ou_le_hasard()
    {
        Assert.Contains("0 à 100", DeckNote.Explanation(20, true));
        Assert.Contains("20 decks forts", DeckNote.Explanation(20, true));
        Assert.Contains("niveau d'un deck de la méta", DeckNote.Explanation(20, true));
        Assert.Contains("au hasard", DeckNote.Explanation(0, false));
    }

    // ----- Même note partout (D-074) -----

    private static readonly AnalysisSettings Tiny = AnalysisSettings.Sheet with
    {
        Player = "rapide", Opponents = 4, Pairs = 1, TalentPairs = 2, RoleVariants = 2, PoolSize = 20, CalibrationOpponents = 4,
        DangerMinGames = 2, ToxMinGames = 1, MetaPairs = 1, MetaRatingPairs = 1, FieldSeed = 91,
    };

    private static readonly SpamDrafter.Engines.Cooking.CookSettings TinyCook = new()
    {
        Population = 6, Elites = 2, MutantsPerElite = 2, Generations = 2, ChampionEvery = 0, ScreenOpponents = 4,
        ConfirmPlayer = "rapide", ConfirmCandidates = 3, ConfirmOpponents = 4, Proposals = 2,
    };

    [Trait("Categorie", "Lent")]
    [Fact]
    public void La_mesure_de_reference_est_exactement_la_force_de_la_fiche()
    {
        if (Phase8LotATests.Kb.Value is not { } kb) return;
        var deck = Bench.RandomDeck(kb, new Rng(77));
        var report = GrandAnalyseur.Analyse(kb, deck, Tiny with { Seed = 5 });
        var measured = GrandAnalyseur.MeasureStrength(kb, deck, Tiny with { Seed = 5 });
        Assert.Equal(report.Strength, measured);
    }

    [Trait("Categorie", "Lent")]
    [Fact]
    public async Task La_note_d_une_proposition_est_la_meme_que_celle_de_sa_fiche()
    {
        if (Phase8LotATests.Kb.Value is not { } kb) return;
        var coordinator = new Coordinator(kb) { CookOverride = TinyCook, AnalysisOverride = Tiny };
        var proposals = await coordinator.GenerateAsync([new SpamDrafter.Engines.Cooking.FixedCard(kb.Cards.First().Id)], Effort.Rapide, 1);
        Assert.NotEmpty(proposals);
        Assert.All(proposals, p => Assert.True(p.Measured));
        foreach (var p in proposals)
        {
            var sheet = await coordinator.AnalyseAsync(p.Deck, complete: false, Effort.Rapide, 1);
            var fromProposal = DeckNote.From(p.Score, p.Margin95);
            var fromSheet = DeckNote.From(sheet.Report.ForNote.Value, sheet.Report.ForNote.Margin95);
            Assert.Equal(fromProposal.Points, fromSheet.Points);                          // la même note
            Assert.Equal(p.Score, sheet.Report.Strength.Value);                           // sur les mêmes duels
            Assert.Contains($"Note : {fromProposal.Text} — {fromProposal.Label}", sheet.Sheet);
        }
        // Les propositions sont classées par cette note.
        Assert.Equal(proposals.Select(p => p.Score).OrderByDescending(x => x), proposals.Select(p => p.Score));
    }

    [Trait("Categorie", "Lent")]
    [Fact]
    public async Task L_analyse_complete_garde_la_meme_note_que_la_fiche()
    {
        if (Phase8LotATests.Kb.Value is not { } kb) return;
        // Réglages de l'analyse complète différents de la fiche : la note doit pourtant rester celle de la fiche.
        var coordinator = new Coordinator(kb) { AnalysisOverride = Tiny };
        var deck = Bench.RandomDeck(kb, new Rng(21));
        var sheet = await coordinator.AnalyseAsync(deck, complete: false, Effort.Normal, 1);
        var full = await coordinator.AnalyseAsync(deck, complete: true, Effort.Normal, 1);
        Assert.Equal(sheet.Report.ForNote, full.Report.ForNote);
    }

    [Fact]
    public async Task Les_propositions_precalculees_sont_mesurees_a_la_demande()
    {
        if (Phase8LotATests.Kb.Value is not { } kb) return;
        var coordinator = new Coordinator(kb) { AnalysisOverride = Tiny };
        var raw = new[] { new GeneratedDeck(Bench.RandomDeck(kb, new Rng(3)), 0.99, 0.01, 60, 20, true, Precomputed: true) };
        var measured = await coordinator.MeasureNotesAsync(raw, 1);
        Assert.True(measured[0].Measured);
        Assert.True(measured[0].Precomputed);
        Assert.NotEqual(0.99, measured[0].Score);   // le score optimiste d'origine est remplacé par la mesure
    }

    // ----- Mémoire -----

    [Fact]
    public void Une_generation_ajoutee_est_retrouvee_apres_rechargement()
    {
        var h = new GenerationHistory(Path1, "ctx");
        h.AddGeneration(Sel("a", "b"), Effort.Normal, "A + B · normal", Props());
        var again = new GenerationHistory(Path1, "ctx");
        var hit = again.FindGeneration(Sel("a", "b"), Effort.Normal);
        Assert.NotNull(hit);
        Assert.Equal("A + B · normal", hit!.Label);
        Assert.Equal(0.55, hit.Proposals![0].Score);
        Assert.Equal("t1", hit.Proposals[0].Deck[0].TalentId);
        Assert.Null(hit.Proposals[0].Deck[1].TalentId);
    }

    [Fact]
    public void Une_autre_demande_ne_retrouve_rien()
    {
        var h = new GenerationHistory(Path1, "ctx");
        h.AddGeneration(Sel("a", "b"), Effort.Normal, "x", Props());
        Assert.Null(h.FindGeneration(Sel("a", "b"), Effort.Rapide));     // autre effort
        Assert.Null(h.FindGeneration(Sel("a"), Effort.Normal));          // autre sélection
        Assert.Null(h.FindGeneration(new[] { new DuelSlot("a", "t1"), new DuelSlot("b", null) }, Effort.Normal));   // talent imposé
    }

    [Fact]
    public void Un_autre_contexte_efface_la_memoire()
    {
        new GenerationHistory(Path1, "version-1").AddGeneration(Sel("a"), Effort.Normal, "x", Props());
        var other = new GenerationHistory(Path1, "version-2");
        Assert.Empty(other.Entries);
        Assert.Null(other.FindGeneration(Sel("a"), Effort.Normal));
    }

    [Fact]
    public void La_fiche_ne_depend_pas_de_l_effort_mais_l_analyse_complete_si()
    {
        var deck = new[] { new DuelSlot("a", null), new DuelSlot("b", null), new DuelSlot("c", null), new DuelSlot("d", null) };
        var h = new GenerationHistory(Path1, "ctx");
        h.AddReport(deck, complete: false, Effort.Normal, "deck", "Fiche", "fiche", "", 100, 5);
        h.AddReport(deck, complete: true, Effort.Normal, "deck", "Complète", "fiche", "détail", 500, 60);
        Assert.NotNull(h.FindReport(deck, false, Effort.Precis));        // la fiche est la même quel que soit l'effort
        Assert.NotNull(h.FindReport(deck, true, Effort.Normal));
        Assert.Null(h.FindReport(deck, true, Effort.Rapide));             // l'analyse complète dépend de l'effort
        Assert.Equal("détail", h.FindReport(deck, true, Effort.Normal)!.Detail);
    }

    [Fact]
    public void Une_meme_demande_n_apparait_qu_une_fois_et_remonte_en_tete()
    {
        var h = new GenerationHistory(Path1, "ctx");
        h.AddGeneration(Sel("a"), Effort.Normal, "premier", Props());
        h.AddGeneration(Sel("b"), Effort.Normal, "second", Props());
        h.AddGeneration(Sel("a"), Effort.Normal, "premier bis", Props(0.7));
        Assert.Equal(new[] { "premier bis", "second" }, h.Entries.Select(e => e.Label));
        Assert.Equal(0.7, h.FindGeneration(Sel("a"), Effort.Normal)!.Proposals![0].Score);
    }

    [Fact]
    public void Seules_les_dernieres_entrees_sont_gardees_pour_chaque_sorte()
    {
        var h = new GenerationHistory(Path1, "ctx", maxPerKind: 3);
        for (var i = 0; i < 5; i++) h.AddGeneration(Sel("c" + i), Effort.Normal, "g" + i, Props());
        var deck = new[] { new DuelSlot("a", null) };
        for (var i = 0; i < 2; i++) h.AddReport(new[] { new DuelSlot("r" + i, null) }, false, Effort.Normal, "r" + i, "t", "s", "", 1, 1);
        Assert.Equal(new[] { "g4", "g3", "g2" }, h.Entries.Where(e => e.Kind == HistoryKind.Generation).Select(e => e.Label));
        Assert.Equal(2, h.Entries.Count(e => e.Kind == HistoryKind.Report));   // les rapports ne chassent pas les générations
        Assert.Null(h.FindGeneration(Sel("c0"), Effort.Normal));
    }

    [Fact]
    public void Retirer_et_vider()
    {
        var h = new GenerationHistory(Path1, "ctx");
        var e = h.AddGeneration(Sel("a"), Effort.Normal, "x", Props());
        h.AddGeneration(Sel("b"), Effort.Normal, "y", Props());
        var changes = 0;
        h.Changed += (_, _) => changes++;
        h.Remove(e.Id);
        Assert.Single(h.Entries);
        h.Clear();
        Assert.Empty(h.Entries);
        Assert.Equal(2, changes);
        Assert.Empty(new GenerationHistory(Path1, "ctx").Entries);   // vidé aussi sur le disque
    }

    [Fact]
    public void Un_fichier_abime_ou_inaccessible_donne_un_historique_vide_sans_erreur()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path1, "{ pas du json");
        var h = new GenerationHistory(Path1, "ctx");
        Assert.Empty(h.Entries);
        h.AddGeneration(Sel("a"), Effort.Normal, "x", Props());   // et on peut de nouveau écrire
        Assert.Single(new GenerationHistory(Path1, "ctx").Entries);

        // Écriture impossible (le « dossier » est un fichier) : aucune exception, la mémoire de la session reste utilisable.
        var blocker = Path.Combine(_dir, "bloque");
        File.WriteAllText(blocker, "x");
        var broken = new GenerationHistory(Path.Combine(blocker, "history.json"), "ctx");
        broken.AddGeneration(Sel("a"), Effort.Normal, "x", Props());
        Assert.NotNull(broken.FindGeneration(Sel("a"), Effort.Normal));
    }
}
