using SpamDrafter.Engines.Analysis;
using SpamDrafter.Simulator.Data;
using SpamDrafter.Simulator.Engine;
using SpamDrafter.Simulator.Players;

namespace SpamDrafter.Engines.Tests;

/// <summary>Phase 8, lot E (D-050) : fiche joueur.</summary>
public class Phase8LotETests
{
    private static SimKnowledge? Kb => Phase8LotATests.Kb.Value;

    /// <summary>Fiche en réglages minuscules (même principe que <see cref="AnalysisSettings.Sheet"/>).</summary>
    private static readonly AnalysisSettings TinySheet = AnalysisSettings.Sheet with
    {
        Player = "rapide", Opponents = 4, Pairs = 1, TalentPairs = 2, RoleVariants = 2, PoolSize = 20, CalibrationOpponents = 4,
        DangerMinGames = 2, ToxMinGames = 1, FieldSeed = 91,
    };

    [Trait("Categorie", "Lent")]
    [Fact]
    public void La_fiche_est_courte_complete_et_sans_liens_mesures()
    {
        if (Kb is not { } kb) return;
        var deck = Bench.RandomDeck(kb, new Rng(31));
        var r = GrandAnalyseur.Analyse(kb, deck, TinySheet);
        Assert.False(r.SynergiesMeasured);
        Assert.Empty(r.Synergies);
        Assert.Equal("rapide", r.TalentPlayer);
        var text = SheetWriter.ToText(kb, r);
        foreach (var part in new[] { "VERDICT", "LES CARTES", "TALENTS", "TOX À ACHETER", "À CRAINDRE" }) Assert.Contains(part, text);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.InRange(lines, 8, 26);                                   // ~15 à 20 lignes (4 talents et « pourquoi » compris)
        Assert.DoesNotContain("NaN", text);
        Assert.All(deck, x => Assert.Contains(kb.CardName(kb.Card(x.CardId)), text));
        // Le détail signale les liens non mesurés au lieu de prétendre qu'il n'y en a pas.
        var md = ReportWriter.ToMarkdown(kb, r);
        Assert.Contains("Non mesurés dans cette analyse", md);
        Assert.DoesNotContain("Aucun lien net mesuré", md);
        // Reproductible.
        Assert.Equal(text.Split('\n')[..^2], SheetWriter.ToText(kb, GrandAnalyseur.Analyse(kb, deck, TinySheet)).Split('\n')[..^2]);
    }

    [Fact]
    public void Qualificatif_selon_le_rang()
    {
        Assert.Equal("Deck très fort", SheetWriter.Level(0.97));
        Assert.Equal("Deck fort", SheetWriter.Level(0.85));
        Assert.Equal("Deck correct", SheetWriter.Level(0.5));
        Assert.Equal("Deck faible", SheetWriter.Level(0.3));
        Assert.Equal("Deck très faible", SheetWriter.Level(0.05));
    }

    [Fact]
    public void Un_Tox_sur_peu_de_duels_n_est_pas_conseille()
    {
        // 6 points d'écart sur 20 duels : pas net. 20 points d'écart sur 400 duels : net.
        var few = new ToxInsight("x", 20, 0.95, 220, 0.89, 0, 1);
        var many = new ToxInsight("x", 400, 0.80, 400, 0.60, 0, 1);
        Assert.True(SheetWriter.GapMargin(few) > few.ScoreWith - few.ScoreWithout);
        Assert.True(SheetWriter.GapMargin(many) < many.ScoreWith - many.ScoreWithout);
    }

    [Trait("Categorie", "Lent")]
    [Fact]
    public void La_preparation_remplit_le_classement_une_seule_fois()
    {
        if (Kb is not { } kb) return;
        var s = TinySheet with { FieldSeed = 92 };
        GrandAnalyseur.WarmUp(kb, s);
        var r = GrandAnalyseur.Analyse(kb, Bench.RandomDeck(kb, new Rng(2)), s);
        // Sans le classement du grand groupe : seulement le deck lui-même au calibrage.
        var withoutPool = s.CalibrationOpponents * 2;
        var r2 = GrandAnalyseur.Analyse(kb, Bench.RandomDeck(kb, new Rng(2)), s with { FieldSeed = 93 });
        Assert.Equal(s.PoolSize * s.CalibrationOpponents * 2, r2.Duels - r.Duels);
        Assert.True(r.Duels > withoutPool);
    }
}
