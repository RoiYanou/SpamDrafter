using System.Text.Json;
using SpamDrafter.Engines;
using SpamDrafter.Engines.Analysis;
using SpamDrafter.Engines.History;
using SpamDrafter.Simulator.Data;
using SpamDrafter.Simulator.Engine;

namespace SpamDrafter.Engines.Tests;

/// <summary>Phase 14, lot D : confrontation de deux decks (moteur, verdict, texte, mémoire).</summary>
public class Phase14LotDTests : IDisposable
{
    private static SimKnowledge? Kb => Phase8LotATests.Kb.Value;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "spamdrafter-p14d-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static IReadOnlyList<DuelSlot> Deck(SimKnowledge kb, int skip) =>
        kb.Cards.Skip(skip).Take(4).Select(c => new DuelSlot(c.Id, null)).ToList();

    /// <summary>Joueur rapide et peu de paires : on vérifie la mécanique, pas la qualité du joueur fin.</summary>
    private static readonly ConfrontationSettings Mini = new() { Player = "rapide", Pairs = 12 };

    private static ConfrontationResult Fake(double score, double margin) =>
        new([new DuelSlot("a")], [new DuelSlot("b")], "rapide", 100, 50, 50, 0, 0,
            new Measured(score, margin, 100), new Measured(score, margin, 50), new Measured(score, margin, 50),
            6, 6, 6, [], [], 1);

    // ----- Verdict -----

    [Fact]
    public void Un_ecart_net_designe_le_meneur_et_un_ecart_dans_la_marge_ne_designe_personne()
    {
        Assert.Equal(0, Fake(0.70, 0.06).Leader);
        Assert.Equal(1, Fake(0.30, 0.06).Leader);
        Assert.Null(Fake(0.54, 0.08).Leader);        // la marge contient 50 %
        Assert.Null(Fake(0.50, 0.00).Leader);
    }

    [Fact]
    public void Le_verdict_ne_dit_nettement_que_pour_un_grand_ecart()
    {
        Assert.Contains("nettement meilleur", ConfrontationWriter.VerdictLine("Feu", "Eau", Fake(0.70, 0.05)));
        var small = ConfrontationWriter.VerdictLine("Feu", "Eau", Fake(0.60, 0.05));
        Assert.Contains("Feu est meilleur que Eau", small);
        Assert.DoesNotContain("nettement", small);
        Assert.Contains("Eau est nettement meilleur que Feu", ConfrontationWriter.VerdictLine("Feu", "Eau", Fake(0.25, 0.05)));
        Assert.Contains("Pas d'écart net", ConfrontationWriter.VerdictLine("Feu", "Eau", Fake(0.52, 0.09)));
    }

    // ----- Confrontation -----

    [Fact]
    public void Les_duels_vont_par_paires_et_chaque_deck_commence_autant_de_fois_qu_il_le_peut()
    {
        if (Kb is not { } kb) return;
        var r = Confrontation.Play(kb, Deck(kb, 0), Deck(kb, 4), Mini);
        Assert.Equal(24, r.Games);
        Assert.Equal(r.Games - r.Errors, r.WinsA + r.WinsB + r.Draws);
        Assert.Equal(r.Games - r.Errors, r.ScoreAFirst.Games + r.ScoreASecond.Games);
        Assert.Equal(r.Games - r.Errors, r.ScoreA.Games);
        Assert.Equal((r.WinsA + 0.5 * r.Draws) / (r.Games - r.Errors), r.ScoreA.Value, 9);
        Assert.Equal(4, r.CardsA.Count);
        Assert.Equal(4, r.CardsB.Count);
        Assert.All(r.CardsA.Concat(r.CardsB), c => Assert.InRange(c.DeathRate, 0, 1));
    }

    [Fact]
    public void Meme_confrontation_meme_resultat_et_la_graine_compte()
    {
        if (Kb is not { } kb) return;
        var a = Confrontation.Play(kb, Deck(kb, 0), Deck(kb, 4), Mini);
        var b = Confrontation.Play(kb, Deck(kb, 0), Deck(kb, 4), Mini with { Parallel = false });
        Assert.Equal((a.WinsA, a.WinsB, a.Draws), (b.WinsA, b.WinsB, b.Draws));          // aussi sans parallélisme
        Assert.Equal(a.AverageRounds, b.AverageRounds, 9);
    }

    [Fact]
    public void Un_deck_contre_lui_meme_est_a_egalite()
    {
        if (Kb is not { } kb) return;
        var d = Deck(kb, 0);
        var r = Confrontation.Play(kb, d, d, Mini with { Pairs = 30 });
        Assert.InRange(r.ScoreA.Value, 0.35, 0.65);   // les places sont échangées dans chaque paire
    }

    [Fact]
    public void Joueur_inconnu_est_refuse_et_l_annulation_est_respectee()
    {
        if (Kb is not { } kb) return;
        Assert.Throws<ArgumentException>(() => Confrontation.Play(kb, Deck(kb, 0), Deck(kb, 4), Mini with { Player = "inconnu" }));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Confrontation.Play(kb, Deck(kb, 0), Deck(kb, 4), Mini, cts.Token));
    }

    // ----- Texte -----

    [Fact]
    public void Le_texte_donne_les_notes_qui_commence_et_la_duree_sans_verdict_ni_nombre_de_duels()
    {
        if (Kb is not { } kb) return;
        var r = Confrontation.Play(kb, Deck(kb, 0), Deck(kb, 4), Mini);
        var text = ConfrontationWriter.ToText(kb, "Feu", "Eau", new ComparisonResult(r, new Measured(0.30, 0.05, 80), new Measured(0.10, 0.05, 80)));
        Assert.StartsWith("NOTES CONTRE LA MÉTA", text);          // le verdict est affiché à part (phrase et barre)
        Assert.DoesNotContain("VERDICT", text);
        Assert.Contains("Feu : Note : ", text);
        Assert.DoesNotContain("La note mesure", text);            // phrase retirée sous les notes
        Assert.Contains("QUI COMMENCE", text);
        Assert.Contains("change une fois sur deux", text);
        Assert.Matches(@"\(± \d+ %\)", text);                      // marge en pourcentage
        Assert.DoesNotContain("duels,", text);                    // plus de nombre de duels
        Assert.DoesNotContain("duels simulés", text);
        Assert.Contains("DURÉE DES DUELS", text);
        Assert.DoesNotContain("CARTES", text);                    // le tableau des cartes est affiché à part
        var without = ConfrontationWriter.ToText(kb, "Feu", "Eau", new ComparisonResult(r, null, null));
        Assert.DoesNotContain("NOTES CONTRE LA MÉTA", without);   // sans note connue, rien n'est inventé
    }

    [Fact]
    public void La_marge_du_verdict_est_en_pourcentage()
    {
        Assert.Contains("(± 5 %)", ConfrontationWriter.VerdictLine("Feu", "Eau", Fake(0.70, 0.05)));
        Assert.Contains("(± 9 %)", ConfrontationWriter.VerdictLine("Feu", "Eau", Fake(0.52, 0.09)));
    }

    // ----- Coordinateur -----

    [Fact]
    public async Task Le_coordinateur_garde_les_notes_deja_connues_et_mesure_les_autres()
    {
        if (Kb is not { } kb) return;
        var coordinator = new Coordinator(kb)
        {
            ConfrontationOverride = Mini,
            AnalysisOverride = AnalysisSettings.Sheet with { Player = "rapide", Opponents = 4, Pairs = 1, CalibrationOpponents = 4, PoolSize = 20 },
        };
        var known = new Measured(0.33, 0.04, 60);
        var res = await coordinator.CompareAsync(Deck(kb, 0), Deck(kb, 4), knownNoteA: known);
        Assert.Equal(known, res.NoteA);                    // reprise telle quelle
        Assert.NotNull(res.NoteB);                          // mesurée
        Assert.Equal(24, res.Confrontation.Games);
        Assert.False(coordinator.IsBusy);
        var both = await coordinator.CompareAsync(Deck(kb, 0), Deck(kb, 4), known, known);
        Assert.Equal(known, both.NoteB);
    }

    // ----- Mémoire -----

    [Fact]
    public void L_historique_garde_la_confrontation_dans_un_fichier_et_la_retrouve()
    {
        if (Kb is not { } kb) return;
        var path = Path.Combine(_dir, "h.json");
        var a = Deck(kb, 0); var b = Deck(kb, 4);
        var result = new ComparisonResult(Confrontation.Play(kb, a, b, Mini), new Measured(0.3, 0.05, 80), null);
        new GenerationHistory(path, "ctx").AddComparison(a, b, "Feu", "Eau", result);

        var again = new GenerationHistory(path, "ctx");
        var hit = again.FindComparison(a, b)!;
        Assert.Equal(HistoryKind.Comparison, hit.Kind);
        Assert.Equal("Feu contre Eau", hit.Label);
        Assert.Equal(result.Confrontation.WinsA, hit.Comparison!.Confrontation.WinsA);
        Assert.Equal(result.Confrontation.ScoreA, hit.Comparison.Confrontation.ScoreA);
        Assert.Equal(4, hit.Comparison.Confrontation.CardsA.Count);
        Assert.Equal(0.3, hit.Comparison.NoteA!.Value);
        Assert.Null(hit.Comparison.NoteB);
        Assert.Null(again.FindComparison(b, a));            // B contre A est une autre demande
        Assert.Null(new GenerationHistory(path, "autre contexte").FindComparison(a, b));
    }

    [Fact]
    public void Les_confrontations_ont_leur_propre_quota_et_ne_chassent_pas_les_rapports()
    {
        var h = new GenerationHistory(Path.Combine(_dir, "q.json"), "ctx", maxPerKind: 2);
        var slots = new List<DuelSlot> { new("a"), new("b"), new("c"), new("d") };
        h.AddReport(slots, false, Effort.Normal, "r", "t", "s", "d", 1, 1);
        var empty = new ComparisonResult(Fake(0.5, 0.1), null, null);
        for (var i = 0; i < 4; i++) h.AddComparison(slots, [new DuelSlot("x" + i)], "A", "B" + i, empty);
        Assert.Equal(2, h.Entries.Count(e => e.Kind == HistoryKind.Comparison));
        Assert.Equal(1, h.Entries.Count(e => e.Kind == HistoryKind.Report));
    }
}
