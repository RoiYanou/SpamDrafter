using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SpamDrafter.Simulator.Data;
using SpamDrafter.Simulator.Engine;

namespace SpamDrafter.Engines.Analysis;

/// <summary>
/// Rédige le rapport du Grand Analyseur en français (Markdown). Règle : chaque affirmation cite le chiffre mesuré
/// qui la justifie ; les noms et descriptions viennent de la base de connaissances ; rien n'est inventé.
/// Les mots « nettement » / « se renforcent » ne sont employés que si la marge d'erreur n'atteint pas 0.
/// </summary>
public static partial class ReportWriter
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public static string ToMarkdown(SimKnowledge kb, AnalysisReport r)
    {
        var sb = new StringBuilder();
        var s = r.Settings;
        string Card(int i) => CardName(kb, r.Deck[i].CardId);
        string Pct(double v) => (100 * v).ToString("0", Fr) + " %";
        string Pts(Measured m) => Signed(100 * m.Value) + " pts (± " + (100 * m.Margin95).ToString("0", Fr) + ")";

        sb.AppendLine("# Analyse du deck");
        sb.AppendLine();
        foreach (var (slot, i) in r.Deck.Select((x, i) => (x, i)))
        {
            var c = kb.Card(slot.CardId);
            sb.Append($"- **{Card(i)}** — {ElementName(kb, c.Element)}, {RarityName(kb, c.Rarity)}, {c.Hp} PV");
            if (slot.TalentId is { } t)
            {
                sb.Append($" ; talent **{kb.AbilityName(t)}**");
                if (TalentText(kb, t) is { } txt) sb.Append($" : « {txt} »");
            }
            else sb.Append(" ; sans talent");
            sb.AppendLine();
        }
        sb.AppendLine();

        // Force
        sb.AppendLine("## Force");
        if (r.UsesMeta && r.NoteStrength is { } reference && reference != r.Strength)
            sb.AppendLine($"- Note sur 100 : **{DeckNote.From(reference.Value, reference.Margin95).Text}**, mesurée comme dans la fiche et dans les propositions " +
                          $"({reference.Games} duels, {Pct(reference.Value)} de victoires). Les chiffres ci-dessous viennent de cette analyse, plus poussée : ils peuvent un peu différer.");
        if (r.UsesMeta)
        {
            sb.AppendLine($"- Contre les {r.Matchups.Count} {s.MetaPlural} ({r.Strength.Games} duels, joueur « {s.Player} »), ce deck marque " +
                          $"**{Pct(r.Strength.Value)}** (± {(100 * r.Strength.Margin95).ToString("0", Fr)}) : victoires + moitié des égalités.");
            sb.AppendLine($"- Classement : il se place devant **{Pct(r.Percentile)}** des {s.MetaPlural} " +
                          $"(joueur « {s.RolePlayer} », chacun joué contre tous les autres).");
            sb.AppendLine($"- Les talents, les Tox, l'importance des cartes et leurs liens sont mesurés contre ces mêmes {s.MetaPlural} " +
                          $"(autres duels), où il marque {Pct(r.LevelStrength.Value)} (± {(100 * r.LevelStrength.Margin95).ToString("0", Fr)}).");
        }
        else
        {
        sb.AppendLine($"- Contre {s.Opponents} decks tirés au hasard ({r.Strength.Games} duels, joueur « {s.Player} »), ce deck marque " +
                      $"**{Pct(r.Strength.Value)}** (± {(100 * r.Strength.Margin95).ToString("0", Fr)}) : victoires + moitié des égalités. " +
                      "Par symétrie, un deck tiré au hasard marque en moyenne 50 % contre ce genre d'adversaires.");
        sb.AppendLine($"- Classement : il fait mieux que **{Pct(r.Percentile)}** de {s.PoolSize} decks tirés au hasard " +
                      $"(joueur « {s.RolePlayer} », mêmes {s.CalibrationOpponents} adversaires pour tous).");
        sb.AppendLine($"- Les talents, les Tox, l'importance des cartes et leurs liens sont mesurés contre les {s.Opponents} decks de ce groupe " +
                      $"les plus proches de son niveau, où il marque {Pct(r.LevelStrength.Value)} (± {(100 * r.LevelStrength.Margin95).ToString("0", Fr)}) : " +
                      "contre des adversaires trop faibles, il gagnerait presque tout et aucune variante ne se distinguerait.");
        }
        sb.AppendLine();

        // Style
        sb.AppendLine("## Style de jeu");
        sb.AppendLine($"- Durée moyenne d'un duel : {Num(r.AverageRounds)} rounds (victoires : {Num(r.AverageRoundsWins)} ; défaites : {Num(r.AverageRoundsLosses)}).");
        var dmg = r.Roles.OrderByDescending(x => x.DamageShare).First();
        sb.AppendLine($"- Principale source de dégâts : **{Card(dmg.Index)}** ({Pct(dmg.DamageShare)} des dégâts du deck, {Num(dmg.Averages.DamageDone)} par duel en moyenne).");
        var heal = r.Roles.OrderByDescending(x => x.Averages.HealingDone).First();
        if (heal.Averages.HealingDone >= 1)
            sb.AppendLine($"- Soins : **{Card(heal.Index)}** rend {Num(heal.Averages.HealingDone)} PV par duel en moyenne ({Pct(heal.HealingShare)} des soins du deck).");
        else sb.AppendLine("- Soins : le deck ne soigne presque pas (moins de 1 PV par duel en moyenne).");
        var tank = r.Roles.OrderByDescending(x => x.DamageTakenShare).First();
        sb.AppendLine($"- Carte la plus visée : **{Card(tank.Index)}** ({Pct(tank.DamageTakenShare)} des dégâts reçus ; morte dans {Pct(tank.Averages.DeathRate)} des duels).");
        var killer = r.Roles.OrderByDescending(x => x.KillShare).First();
        if (killer.Averages.Kills > 0)
            sb.AppendLine($"- Éliminations : **{Card(killer.Index)}** porte {Pct(killer.KillShare)} des coups fatals.");
        sb.AppendLine();
        sb.AppendLine("| Carte | Dégâts / duel | Soins / duel | Dégâts reçus / duel | Éliminations / duel | Lancers / duel | Talent activé / duel | Mort |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var x in r.Roles)
            sb.AppendLine($"| {Card(x.Index)} | {Num(x.Averages.DamageDone)} | {Num(x.Averages.HealingDone)} | {Num(x.Averages.DamageTaken)} | " +
                          $"{x.Averages.Kills.ToString("0.00", Fr)} | {Num(x.Averages.Casts, 1)} | {Num(x.Averages.TalentActivations, 1)} | {Pct(x.Averages.DeathRate)} |");
        sb.AppendLine();

        // Rôles
        sb.AppendLine("## Importance de chaque carte");
        sb.AppendLine($"Chaque carte est remplacée, à la même place, par {s.RoleVariants} cartes tirées au hasard ; on mesure ce que le deck perd " +
                      $"contre les adversaires de même niveau (joueur « {s.RolePlayer} », mêmes duels pour toutes les variantes).");
        sb.AppendLine();
        foreach (var x in r.Roles.OrderByDescending(x => x.Contribution.Value))
        {
            var verdict = x.Contribution.ClearlyPositive ? "le deck perd nettement sans elle"
                : x.Contribution.ClearlyNegative ? "une carte au hasard fait mieux en moyenne"
                : "pas d'écart net avec une carte au hasard";
            sb.AppendLine($"- **{Card(x.Index)}** : {Pts(x.Contribution)} — {verdict}.");
        }
        sb.AppendLine();

        // Synergies
        sb.AppendLine("## Liens entre les cartes");
        sb.AppendLine("Pour chaque paire : deck − sans l'une − sans l'autre + sans les deux (adversaires de même niveau). " +
                      "Positif : elles se complètent (chacune vaut plus quand l'autre est là). " +
                      "Négatif : elles font double emploi (l'une compense l'absence de l'autre).");
        sb.AppendLine();
        if (!r.SynergiesMeasured) sb.AppendLine("- Non mesurés dans cette analyse (fiche rapide) : lancer l'analyse complète pour les obtenir.");
        var plus = r.Synergies.Where(x => x.Interaction.ClearlyPositive).OrderByDescending(x => x.Interaction.Value).ToList();
        var minus = r.Synergies.Where(x => x.Interaction.ClearlyNegative).OrderBy(x => x.Interaction.Value).ToList();
        foreach (var x in plus) sb.AppendLine($"- **{Card(x.I)}** et **{Card(x.J)}** se complètent : {Pts(x.Interaction)}.");
        foreach (var x in minus) sb.AppendLine($"- **{Card(x.I)}** et **{Card(x.J)}** font double emploi : {Pts(x.Interaction)}.");
        if (r.SynergiesMeasured && plus.Count + minus.Count == 0) sb.AppendLine("- Aucun lien net mesuré (toutes les marges d'erreur atteignent 0).");
        var others = r.Synergies.Except(plus).Except(minus).ToList();
        if (others.Count > 0 && plus.Count + minus.Count > 0)
            sb.AppendLine($"- Autres paires : pas d'effet net ({string.Join(" ; ", others.Select(x => $"{Card(x.I)} + {Card(x.J)} : {Pts(x.Interaction)}"))}).");
        sb.AppendLine();

        // Talents
        sb.AppendLine("## Talents");
        var talentGames = r.Talents.SelectMany(t => t.Options).Select(o => o.Difference.Games).DefaultIfEmpty(0).First();
        sb.AppendLine($"Chaque autre choix de talent est joué sur les mêmes duels que le deck ({talentGames} duels contre les adversaires " +
                      $"de même niveau, joueur « {r.TalentPlayer} »).");
        sb.AppendLine();
        foreach (var t in r.Talents)
        {
            var current = t.Current is null ? "sans talent" : kb.AbilityName(t.Current);
            if (t.Options.Count == 0) { sb.AppendLine($"- **{CardName(kb, t.CardId)}** : aucun talent disponible."); continue; }
            var better = t.ClearlyBetter;
            var line = better is not null
                ? $"**changer** pour {TalentLabel(kb, better.TalentId)} ({Pts(better.Difference)} par rapport à « {current} »)"
                : $"garder « {current} » (aucun autre choix ne fait nettement mieux)";
            sb.AppendLine($"- **{CardName(kb, t.CardId)}** : {line}. Détail : " +
                          string.Join(" ; ", t.Options.Select(o => $"{TalentLabel(kb, o.TalentId)} {Pts(o.Difference)}")) + ".");
        }
        sb.AppendLine();

        // Tox
        sb.AppendLine("## Tox");
        if (r.Tox.Count == 0)
            sb.AppendLine($"Aucun Tox n'a été acheté assez souvent pour en parler (au moins {s.ToxMinGames} duels sur {r.ToxGames}).");
        else
        {
            sb.AppendLine($"Achats du joueur « {s.Player} » sur {r.ToxGames} duels (le deck et ses variantes de talents, tous adversaires). " +
                          "Attention : c'est un lien observé, pas une preuve de cause (on achète aussi plus quand la partie va bien).");
            sb.AppendLine();
            sb.AppendLine("| Tox | Duels avec achat | Score avec | Score sans | Écart | Surtout posé sur |");
            sb.AppendLine("|---|---|---|---|---|---|");
            foreach (var t in r.Tox.Take(6))
                sb.AppendLine($"| {ToxLabel(kb, t.ToxUuid)} | {t.GamesWith} | {Pct(t.ScoreWith)} | {Pct(t.ScoreWithout)} | " +
                              $"{Signed(100 * (t.ScoreWith - t.ScoreWithout))} pts | {Card(t.MostOftenOn)} |");
        }
        sb.AppendLine();

        // Adversaires
        sb.AppendLine("## Adversaires difficiles");
        if (r.UsesMeta)
        {
            sb.AppendLine($"Score contre chaque {s.MetaSingular} (joueur « {s.RolePlayer} », {s.MetaPairs * 2} duels chacun), du plus dur au plus facile :");
            sb.AppendLine();
            sb.AppendLine(s.MetaIsSynthetic ? "| Deck de la méta synthétique | Score | Marge |" : "| Deck de référence | Score | Marge |");
            sb.AppendLine("|---|---|---|");
            foreach (var m in r.Matchups)
                sb.AppendLine($"| {m.Name} | {Pct(m.Score.Value)} | ± {(100 * m.Score.Margin95).ToString("0", Fr)} |");
            sb.AppendLine();
        }
        else
        {
        sb.AppendLine($"Contre les {s.PoolSize} decks du groupe tirés au hasard ({r.DangerOverall.Games} duels, joueur « {s.RolePlayer} »), " +
                      $"le deck marque {Pct(r.DangerOverall.Value)}. Cartes adverses contre lesquelles il marque le moins " +
                      $"(au moins {s.DangerMinGames} duels) :");
        sb.AppendLine();
        foreach (var d in r.Dangers.Take(5))
            sb.AppendLine($"- **{CardName(kb, d.CardId)}** : {Pct(d.Score.Value)} (± {(100 * d.Score.Margin95).ToString("0", Fr)}, {d.Score.Games} duels)" +
                          (d.Score.Value + d.Score.Margin95 < r.DangerOverall.Value ? " — nettement sous la moyenne du deck." : "."));
        if (r.Dangers.Count == 0) sb.AppendLine("- Pas assez de duels par carte adverse pour conclure.");
        sb.AppendLine();
        }

        // Méthode
        sb.AppendLine("## Méthode");
        sb.AppendLine($"- {r.Duels} duels simulés (SPAM {kb.GameVersion}), {r.Errors} erreur(s), {r.Elapsed.TotalSeconds.ToString("0", Fr)} s.");
        sb.AppendLine($"- Graines : adversaires {s.FieldSeed}, duels {s.Seed}. Mêmes graines et même deck = même rapport.");
        sb.AppendLine("- « pts » = points de pourcentage de score ; « ± » = marge d'erreur à 95 %.");
        sb.AppendLine(r.UsesMeta && s.MetaIsSynthetic ? "- Les adversaires sont les decks de la méta synthétique : calculés par le Grand Cuisinier pour cette version de SPAM (pas des decks réellement joués), joués par l'IA du simulateur."
                     : r.UsesMeta ? "- Les adversaires sont les decks de référence (decks réellement joués), joués par l'IA du simulateur."
                                 : "- Les adversaires sont des decks tirés au hasard, pas les decks réellement joués (KI-040).");
        return sb.ToString();
    }

    public static string CardName(SimKnowledge kb, string id) => kb.CardName(kb.Card(id));

    private static string TalentLabel(SimKnowledge kb, string? id) => id is null ? "« sans talent »" : $"« {kb.AbilityName(id)} »";

    /// <summary>Description du talent tirée de la base ; les valeurs à compléter (&lt;value1&gt;…) deviennent « X ».</summary>
    public static string? TalentText(SimKnowledge kb, string id) =>
        kb.Text("Talent.Description." + id) is { } t ? Placeholder().Replace(t.Replace("\r", "").Replace("\n", " "), "X").Trim() : null;

    public static string ElementName(SimKnowledge kb, string element) => kb.Text("Element.Name." + element) ?? element;

    private static string RarityName(SimKnowledge kb, string rarity) =>
        kb.Text("Rarity.Name." + rarity.Replace("rarity-", "")) ?? rarity;

    /// <summary>
    /// La base n'a pas de nom français pour les Tox : on affiche leur type (nom interne du jeu), leur valeur et leur rareté.
    /// </summary>
    public static string ToxLabel(SimKnowledge kb, string uuid) => kb.Tox(uuid) is { } t
        ? $"{t.StatusType}{(t.Value != 0 ? " " + t.Value.ToString("0.##", Fr) : "")}{(t.Rarity is { } r ? " (" + RarityName(kb, r) + ")" : "")}"
        : uuid;

    private static string Num(double v, int decimals = 0) => v.ToString(decimals == 0 ? "0" : "0.0", Fr);
    private static string Signed(double v) => (v >= 0 ? "+" : "−") + Math.Abs(v).ToString("0", Fr);

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Placeholder();
}
