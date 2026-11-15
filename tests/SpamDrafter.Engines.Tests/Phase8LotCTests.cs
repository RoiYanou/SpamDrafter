using SpamDrafter.Engines.Analysis;
using SpamDrafter.Simulator.Data;
using SpamDrafter.Simulator.Engine;
using SpamDrafter.Simulator.Players;

namespace SpamDrafter.Engines.Tests;

/// <summary>Phase 8, lot C : Grand Analyseur.</summary>
public class Phase8LotCTests
{
    private static SimKnowledge? Kb => Phase8LotATests.Kb.Value;

    /// <summary>Réglages minuscules pour des tests rapides (le principe est le même).</summary>
    private static readonly AnalysisSettings Tiny = new()
    {
        Player = "rapide", Opponents = 4, Pairs = 1, RoleVariants = 2, PoolSize = 20, CalibrationOpponents = 4,
        DangerMinGames = 2, ToxMinGames = 1, FieldSeed = 77,
    };

    [Fact]
    public void Mesures_moyenne_et_marge()
    {
        var m = Measured.Of([1, 0, 1, 0]);
        Assert.Equal(0.5, m.Value, 9);
        Assert.Equal(4, m.Games);
        Assert.True(m.Margin95 > 0);
        Assert.False(m.ClearlyPositive);
        Assert.True(new Measured(0.3, 0.1, 10).ClearlyPositive);
        Assert.True(new Measured(-0.3, 0.1, 10).ClearlyNegative);
        // Un score de 0 % sur peu de duels n'a pas une marge nulle.
        Assert.True(Measured.OfScores([0, 0, 0, 0, 0]).Margin95 > 0.1);
        Assert.Equal(new Measured(0, 0, 3), GrandAnalyseur.Paired([1, 0, 0.5], [1, 0, 0.5]));
    }

    [Fact]
    public void Les_textes_des_talents_viennent_de_la_base()
    {
        if (Kb is not { } kb) return;
        var withPlaceholder = kb.Talents.Select(t => t.Id).FirstOrDefault(id => kb.Text("Talent.Description." + id)?.Contains('<') == true);
        if (withPlaceholder is null) return;
        var text = ReportWriter.TalentText(kb, withPlaceholder)!;
        Assert.DoesNotContain("<", text);
        Assert.Contains("X", text);
        Assert.Null(ReportWriter.TalentText(kb, "talent-inexistant"));
    }

    [Fact]
    public void L_analyse_est_complete_et_reproductible()
    {
        if (Kb is not { } kb) return;
        var deck = Bench.RandomDeck(kb, new Rng(12));
        var a = GrandAnalyseur.Analyse(kb, deck, Tiny);
        var b = GrandAnalyseur.Analyse(kb, deck, Tiny);
        Assert.Equal(0, a.Errors);
        Assert.Equal(4, a.Roles.Count);
        Assert.Equal(6, a.Synergies.Count);
        Assert.InRange(a.Percentile, 0, 1);
        Assert.InRange(a.Strength.Value, 0, 1);
        for (var i = 0; i < 4; i++)
        {
            var t = a.Talents[i];
            Assert.Equal(kb.Card(deck[i].CardId).Talents.Count, t.Options.Count);   // les 3 talents + « sans talent », moins le choix actuel
            Assert.DoesNotContain(t.Options, o => o.TalentId == deck[i].TalentId);
            Assert.True(t.Best == t.Current || t.Options.Any(o => o.TalentId == t.Best && o.Difference.Value > 0));
        }
        Assert.InRange(a.Roles.Sum(r => r.DamageShare), 0.999, 1.001);
        var ma = Strip(ReportWriter.ToMarkdown(kb, a));
        Assert.Equal(ma, Strip(ReportWriter.ToMarkdown(kb, b)));
        Assert.All(deck, x => Assert.Contains(kb.CardName(kb.Card(x.CardId)), ma));
        Assert.DoesNotContain("NaN", ma);
        Assert.DoesNotContain("∞", ma);
    }

    [Fact]
    public void Un_deck_incomplet_est_refuse_et_l_analyse_s_annule()
    {
        if (Kb is not { } kb) return;
        var deck = Bench.RandomDeck(kb, new Rng(3));
        Assert.Throws<ArgumentException>(() => GrandAnalyseur.Analyse(kb, deck.Take(3).ToList(), Tiny));
        Assert.Throws<ArgumentException>(() => GrandAnalyseur.Analyse(kb, [deck[0], deck[0], deck[1], deck[2]], Tiny));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => GrandAnalyseur.Analyse(kb, deck, Tiny with { FieldSeed = 78 }, cts.Token));
    }

    [Fact]
    public void Avancement_jusqu_a_la_fin()
    {
        if (Kb is not { } kb) return;
        var reports = new List<AnalysisProgress>();
        GrandAnalyseur.Analyse(kb, Bench.RandomDeck(kb, new Rng(4)), Tiny with { FieldSeed = 79 },
            progress: new Sync<AnalysisProgress>(r => { lock (reports) reports.Add(r); }));
        Assert.Contains(reports, r => r.Stage == AnalysisStage.Calibration);
        Assert.Contains(reports, r => r.Stage == AnalysisStage.Roles);
        Assert.Equal(new AnalysisProgress(AnalysisStage.Done, 1), reports[^1]);
        Assert.All(reports, r => Assert.InRange(r.Fraction, 0, 1));
    }

    /// <summary>La ligne « Méthode » contient la durée, qui change d'une fois à l'autre.</summary>
    private static string Strip(string md) => string.Join("\n", md.Split('\n').Where(l => !l.Contains(" duels simulés (SPAM")));

    private sealed class Sync<T>(Action<T> f) : IProgress<T> { public void Report(T value) => f(value); }
}
