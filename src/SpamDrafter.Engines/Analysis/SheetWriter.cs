using System.Globalization;
using System.Text;
using SpamDrafter.Engines.Profiles;
using SpamDrafter.Simulator.Data;

namespace SpamDrafter.Engines.Analysis;

/// <summary>
/// Fiche joueur (phase 8, lot E, D-050) : l'essentiel du rapport de l'Analyseur pour un joueur qui veut jouer le deck,
/// en une quinzaine de lignes. Règles : un conseil n'apparaît que si la mesure est nette (la marge d'erreur n'atteint
/// pas 0) ; chaque conseil garde son chiffre ; rien n'est inventé. Le détail reste dans <see cref="ReportWriter"/>.
/// </summary>
public static class SheetWriter
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Écart minimal (en points de score) pour conseiller un Tox.</summary>
    private const double ToxMinGap = 0.05;

    public static string ToText(SimKnowledge kb, AnalysisReport r)
    {
        var sb = new StringBuilder();
        string Card(int i) => ReportWriter.CardName(kb, r.Deck[i].CardId);
        string Pct(double v) => (100 * v).ToString("0", Fr) + " %";
        string Pts(double v) => (v >= 0 ? "+" : "−") + Math.Abs(100 * v).ToString("0", Fr) + " pts";

        // Verdict
        sb.AppendLine("VERDICT");
        if (r.UsesMeta)
        {
            // Note sur 100 (D-072) : le pourcentage de duels simulés gagnés contre la méta ; 50 = au niveau de la méta.
            var m = r.ForNote;
            var note = DeckNote.From(m.Value, m.Margin95);
            sb.AppendLine($"Note : {note.Text} — {note.Label}" + (note.NeedsConfirmation ? " (à confirmer : peu de duels)" : ""));
            sb.AppendLine($"Le deck gagne {Pct(m.Value)} de ses duels simulés contre les {r.Matchups.Count} {r.Settings.MetaPlural} " +
                          $"(la note va de 0 à 100 : 100 = au niveau d'un deck de la méta) " +
                          $"et se classe devant {Pct(r.Percentile)} d'entre eux.");
        }
        else
        {
            // Données sans méta (développement) : la note existe aussi, contre des decks tirés au hasard.
            var m = r.ForNote;
            var note = DeckNote.From(m.Value, m.Margin95);
            sb.AppendLine($"Note : {note.Text} — {note.Label}" + (note.NeedsConfirmation ? " (à confirmer : peu de duels)" : ""));
            sb.AppendLine($"{Level(r.Percentile)} : meilleur que {Pct(r.Percentile)} des decks tirés au hasard " +
                          $"(il gagne {Pct(m.Value)} de ses duels contre eux).");
        }
        sb.AppendLine();

        // Pourquoi ça marche (lot G) : mesures de simulation + profils lus dans les données.
        var why = Why(kb, r);
        if (why.Count > 0)
        {
            sb.AppendLine("POURQUOI ÇA MARCHE");
            foreach (var line in why) sb.AppendLine("• " + line);
            sb.AppendLine();
        }

        // Cartes
        sb.AppendLine("LES CARTES");
        var key = r.Roles.OrderByDescending(x => x.Contribution.Value).First();
        if (key.Contribution.ClearlyPositive)
        {
            // Son rôle chiffré seulement s'il est marqué (au moins un quart des dégâts ou des soins du deck).
            var role = key.DamageShare >= 0.25 ? $" ; elle fait {Pct(key.DamageShare)} des dégâts"
                : key.HealingShare >= 0.25 ? $" ; elle fait {Pct(key.HealingShare)} des soins" : "";
            sb.AppendLine($"• Carte clé : {Card(key.Index)} — sans elle, le deck perd {Pts(key.Contribution.Value).TrimStart('+')}{role}. Protège-la.");
        }
        var weak = r.Roles.OrderBy(x => x.Contribution.Value).First();
        if (weak.Index != key.Index && !weak.Contribution.ClearlyPositive)
            sb.AppendLine($"• Carte la plus faible : {Card(weak.Index)} — une carte au hasard ferait " +
                          (weak.Contribution.ClearlyNegative ? $"mieux ({Pts(-weak.Contribution.Value)}). À remplacer." : "aussi bien. À remplacer si tu as mieux."));
        var tank = r.Roles.OrderByDescending(x => x.DamageTakenShare).First();
        if (tank.Index != key.Index)
            sb.AppendLine($"• Carte qui encaisse : {Card(tank.Index)} ({Pct(tank.DamageTakenShare)} des coups reçus).");
        sb.AppendLine();

        // Talents
        sb.AppendLine("TALENTS");
        var changes = r.Talents.Where(t => t.ClearlyBetter is not null).ToList();
        foreach (var t in r.Talents)
        {
            var better = t.ClearlyBetter;
            var current = t.Current is null ? "sans talent" : kb.AbilityName(t.Current);
            sb.AppendLine(better is not null
                ? $"• {ReportWriter.CardName(kb, t.CardId)} : prends {Talent(kb, better.TalentId)} au lieu de « {current} » ({Pts(better.Difference.Value)})."
                : $"• {ReportWriter.CardName(kb, t.CardId)} : « {current} »" + (t.Options.Count == 0 ? "." : " — aucun autre talent ne fait nettement mieux."));
        }
        sb.AppendLine();

        // Tox
        sb.AppendLine("TOX À ACHETER");
        var tox = r.Tox.Where(t => t.ScoreWith - t.ScoreWithout >= Math.Max(ToxMinGap, GapMargin(t))).Take(3).ToList();
        if (tox.Count == 0) sb.AppendLine("• Aucun Tox ne ressort nettement dans les mesures : achète selon la partie.");
        foreach (var t in tox)
            sb.AppendLine($"• {ReportWriter.ToxLabel(kb, t.ToxUuid)}, surtout sur {Card(t.MostOftenOn)} " +
                          $"(gagne {Pct(t.ScoreWith)} quand il est acheté, {Pct(t.ScoreWithout)} sinon ; {t.GamesWith} duels).");
        sb.AppendLine();

        // Adversaires
        sb.AppendLine("À CRAINDRE");
        if (r.UsesMeta)
        {
            // Decks de référence contre lesquels il perd nettement (la marge n'atteint pas 50 %), les pires d'abord.
            var bad = r.Matchups.Where(m => m.Score.Value + m.Score.Margin95 < 0.5).Take(3).ToList();
            if (bad.Count == 0)
                sb.AppendLine($"• Aucun {r.Settings.MetaSingular} ne le bat nettement" +
                              (r.Matchups.Count > 0 ? $" (le plus dur : {r.Matchups[0].Name}, {Pct(r.Matchups[0].Score.Value)} de victoires)." : "."));
            foreach (var m in bad)
                sb.AppendLine($"• {m.Name} : il ne gagne que {Pct(m.Score.Value)} contre lui ({m.Score.Games} duels).");
            var good = r.Matchups.Where(m => m.Score.Value - m.Score.Margin95 > 0.5).ToList();
            if (good.Count > 0) sb.AppendLine($"• Il bat nettement {good.Count} des {r.Matchups.Count} {r.Settings.MetaPlural}.");
            sb.AppendLine();
        }
        var dangers = r.UsesMeta ? [] : r.Dangers.Where(d => d.Score.Value + d.Score.Margin95 < r.DangerOverall.Value).Take(3).ToList();
        if (dangers.Count == 0 && !r.UsesMeta) sb.AppendLine("• Aucune carte adverse nettement plus dangereuse que les autres.");
        foreach (var d in dangers)
            sb.AppendLine($"• {ReportWriter.CardName(kb, d.CardId)} : le deck ne gagne que {Pct(d.Score.Value)} contre elle " +
                          $"({Pct(r.DangerOverall.Value)} en moyenne).");
        if (!r.UsesMeta) sb.AppendLine();

        sb.AppendLine($"Duels de {r.AverageRounds.ToString("0", Fr)} rounds en moyenne. Mesures : {r.Duels.ToString("N0", Fr)} duels simulés" +
                      (r.Errors > 0 ? $", {r.Errors} erreur(s)" : "") + ". Le détail explique chaque chiffre.");
        return sb.ToString();
    }

    /// <summary>
    /// Au plus 3 constats chiffrés : l'élément le plus monté (🔒 +10 % de dégâts par niveau, §5.2), la part des critiques
    /// dans les dégâts d'une carte, et les aides entre cartes repérées dans leurs profils (🃏).
    /// </summary>
    internal static List<string> Why(SimKnowledge kb, AnalysisReport r)
    {
        var lines = new List<string>();
        string Card(int i) => ReportWriter.CardName(kb, r.Deck[i].CardId);
        // Élément : le plus haut niveau atteint en moyenne, et la carte qui en apporte le plus.
        if (r.PeakLevels.Where(kv => kv.Value >= 3).OrderByDescending(kv => kv.Value).FirstOrDefault() is { Key: not null } top)
        {
            var giver = r.Roles.OrderByDescending(x => x.Averages.ElementGiven).First();
            lines.Add($"Niveau {ReportWriter.ElementName(kb, top.Key).ToLowerInvariant()} : {top.Value.ToString("0.#", Fr)} en moyenne au plus haut, " +
                      $"soit +{(10 * top.Value).ToString("0", Fr)} % de dégâts dans cet élément" +
                      (giver.Averages.ElementGiven >= 1 ? $". {Card(giver.Index)} apporte le plus de niveaux : {giver.Averages.ElementGiven.ToString("0", Fr)} par duel, tous éléments confondus." : "."));
        }
        // Critiques : la carte dont les critiques comptent le plus dans ses dégâts.
        var crit = r.Roles.Where(x => x.Averages.DamageDone + x.Averages.HealingDone > 0)
            .Select(x => (x, Share: Math.Min(1, x.Averages.CritBonus / (x.Averages.DamageDone + x.Averages.HealingDone))))
            .OrderByDescending(t => t.Share).FirstOrDefault();
        if (crit.x is not null && crit.Share >= 0.15)
            lines.Add($"Critiques : environ {(100 * crit.Share).ToString("0", Fr)} % des dégâts de {Card(crit.x.Index)} viennent de coups critiques " +
                      $"({crit.x.Averages.CritHits.ToString("0.#", Fr)} par duel).");
        // Aides entre cartes (profils).
        var profiles = CardProfiles.For(kb);
        var best = (from i in Enumerable.Range(0, r.Deck.Count) from j in Enumerable.Range(0, r.Deck.Count) where i != j
                    let h = profiles.Helps(r.Deck[i].CardId, r.Deck[j].CardId, r.Deck[i].TalentId)
                    where h >= 0.3 orderby h descending, i, j select ((int I, int J)?)(i, j)).FirstOrDefault();
        if (best is { } b)
        {
            var helper = profiles.Card(r.Deck[b.I].CardId);
            var talent = profiles.Talent(r.Deck[b.I].TalentId);
            var user = profiles.Card(r.Deck[b.J].CardId);
            bool Exact(string e) => helper.Generates.ContainsKey(e) || talent?.Generates.ContainsKey(e) == true;
            var any = helper.Generates.ContainsKey("*") || talent?.Generates.ContainsKey("*") == true;
            var shared = user.Uses.Keys.Where(e => Exact(e) || any).OrderByDescending(e => Exact(e)).ThenByDescending(e => user.Uses[e]).FirstOrDefault();
            if (shared is not null)
                lines.Add(Exact(shared)
                    ? $"{Card(b.I)} fait monter le niveau {ReportWriter.ElementName(kb, shared).ToLowerInvariant()}, que {Card(b.J)} utilise pour attaquer."
                    : $"{Card(b.I)} fait monter des niveaux d'élément (au hasard ou tous), dont l'élément {ReportWriter.ElementName(kb, shared)} que {Card(b.J)} utilise pour attaquer.");
            else if (helper.GivesCrit + (talent?.GivesCrit ?? 0) > 0 || helper.GivesCritRatio + (talent?.GivesCritRatio ?? 0) > 0)
                lines.Add($"{Card(b.I)} donne du critique à {Card(b.J)}, dont les critiques sont ×{user.CritRatio.ToString("0.#", Fr)}.");
        }
        return lines;
    }

    /// <summary>Qualificatif selon le rang parmi des decks tirés au hasard (seuils de présentation, pas du jeu).</summary>
    public static string Level(double percentile) => percentile switch
    {
        >= 0.95 => "Deck très fort",
        >= 0.80 => "Deck fort",
        >= 0.50 => "Deck correct",
        >= 0.20 => "Deck faible",
        _ => "Deck très faible",
    };

    /// <summary>
    /// Marge d'erreur à 95 % de l'écart « score avec » − « score sans » (deux groupes de duels indépendants ;
    /// règle de Laplace pour ne pas avoir de marge nulle à 0 % ou 100 %).
    /// </summary>
    internal static double GapMargin(ToxInsight t)
    {
        static double Var(double score, int n) { var p = (score * n + 1) / (n + 2); return p * (1 - p) / Math.Max(n, 1); }
        return 1.96 * Math.Sqrt(Var(t.ScoreWith, t.GamesWith) + Var(t.ScoreWithout, t.GamesWithout));
    }

    private static string Talent(SimKnowledge kb, string? id) => id is null ? "« sans talent »" : $"« {kb.AbilityName(id)} »";
}
