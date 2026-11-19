using System.Globalization;
using System.Text;
using SpamDrafter.Simulator.Data;
using SpamDrafter.Simulator.Engine;
using SpamDrafter.Simulator.Players;

namespace SpamDrafter.Engines.Analysis;

/// <summary>Réglages d'une confrontation (réglages de l'outil, pas du jeu).</summary>
public sealed record ConfrontationSettings
{
    /// <summary>Joueur qui joue les deux camps : le joueur fin, celui de la note.</summary>
    public string Player { get; init; } = "heuristique";
    /// <summary>Paires de duels : même graine, places échangées (chaque deck est autant de fois premier que second joueur).</summary>
    public int Pairs { get; init; } = 150;
    public ulong Seed { get; init; } = 1;
    public bool Parallel { get; init; } = true;

    public static ConfrontationSettings Default { get; } = new();
}

/// <summary>Ce qu'une carte a fait en moyenne par duel (sa propre place dans son deck).</summary>
public sealed record SideCard(DuelSlot Slot, double DamageDone, double HealingDone, double DamageTaken, double Kills, double DeathRate);

/// <summary>Bilan d'une confrontation du deck A contre le deck B.</summary>
/// <param name="ScoreA">Victoires de A + moitié des égalités (0 à 1), avec marge à 95 %.</param>
/// <param name="ScoreAFirst">Score de A dans les duels où A joue en premier.</param>
/// <param name="ScoreASecond">Score de A dans les duels où B joue en premier.</param>
public sealed record ConfrontationResult(
    IReadOnlyList<DuelSlot> DeckA, IReadOnlyList<DuelSlot> DeckB, string Player,
    int Games, int WinsA, int WinsB, int Draws, int Errors,
    Measured ScoreA, Measured ScoreAFirst, Measured ScoreASecond,
    double AverageRounds, double AverageRoundsWinsA, double AverageRoundsWinsB,
    IReadOnlyList<SideCard> CardsA, IReadOnlyList<SideCard> CardsB, double ElapsedSeconds)
{
    /// <summary>Écart (en points de score) à partir duquel l'avantage est dit « net » (présentation 🟡).</summary>
    public const double ClearGap = 0.15;

    /// <summary>0 = A mène, 1 = B mène, null = pas d'écart net : la marge d'erreur contient 50 %.</summary>
    public int? Leader =>
        ScoreA.Value - ScoreA.Margin95 > 0.5 ? 0 : ScoreA.Value + ScoreA.Margin95 < 0.5 ? 1 : null;
}

/// <summary>Une confrontation et, quand ils sont connus, les deux notes (mesure de référence contre la méta, D-074).</summary>
public sealed record ComparisonResult(ConfrontationResult Confrontation, Measured? NoteA, Measured? NoteB);

/// <summary>
/// Confrontation de deux decks (phase 14, lot D) : A et B se jouent l'un contre l'autre, le même joueur tenant les deux
/// camps. Les duels vont par paires (même graine, places échangées) : le tirage et l'ordre de jeu comptent pareil pour les
/// deux. Même decks + mêmes réglages = même résultat.
/// </summary>
public static class Confrontation
{
    private sealed record Game(int Outcome, int Rounds, bool AFirst, DuelStats? Stats, int SeatA, string? Error);

    public static ConfrontationResult Play(SimKnowledge kb, IReadOnlyList<DuelSlot> deckA, IReadOnlyList<DuelSlot> deckB,
        ConfrontationSettings? settings = null, CancellationToken cancel = default, IProgress<int>? progress = null)
    {
        var s = settings ?? ConfrontationSettings.Default;
        var factory = PlayerCatalog.Factory(s.Player) ?? throw new ArgumentException($"Joueur inconnu : {s.Player}.");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        // Tout le hasard est tiré d'avance, dans l'ordre : le résultat ne dépend pas de l'ordre d'exécution.
        var rng = new Rng(s.Seed);
        var plans = Enumerable.Range(0, s.Pairs).Select(_ => (Duel: rng.NextULong(), PA: rng.NextULong(), PB: rng.NextULong())).ToList();
        var games = new Game[s.Pairs * 2];
        var done = 0;

        void PlayPair(int i)
        {
            cancel.ThrowIfCancellationRequested();
            var plan = plans[i];
            for (var seat = 0; seat < 2; seat++)   // seat : joueur (0 ou 1) qui tient le deck A
            {
                var g = 2 * i + seat;
                try
                {
                    var (d0, d1) = seat == 0 ? (deckA, deckB) : (deckB, deckA);
                    var duel = new Duel(kb, d0, d1, plan.Duel) { Log = { Enabled = false }, Stats = new DuelStats(kb.Settings.CardsPerDeck) };
                    var a = factory(plan.PA);
                    var b = factory(plan.PB);
                    var r = seat == 0 ? DuelRunner.Play(duel, a, b) : DuelRunner.Play(duel, b, a);
                    var outcome = !r.Finished || r.Winner is null or -1 ? 0 : r.Winner == seat ? 1 : -1;
                    games[g] = new Game(outcome, r.Rounds, duel.FirstPlayer == seat, duel.Stats, seat, r.Finished ? null : "partie non terminée");
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    games[g] = new Game(0, 0, false, null, seat, e.GetType().Name + " : " + e.Message);
                }
                progress?.Report(Interlocked.Increment(ref done));
            }
        }

        if (s.Parallel) SpamDrafter.Simulator.ComputeLimits.For(s.Pairs, PlayPair, cancel);
        else for (var i = 0; i < s.Pairs; i++) PlayPair(i);

        var ok = games.Where(g => g.Error is null).ToList();
        double Val(Game g) => g.Outcome > 0 ? 1 : g.Outcome == 0 ? 0.5 : 0;
        var scoreA = Measured.OfScores(ok.Select(Val).ToList());
        var first = Measured.OfScores(ok.Where(g => g.AFirst).Select(Val).ToList());
        var second = Measured.OfScores(ok.Where(g => !g.AFirst).Select(Val).ToList());
        double Avg(IEnumerable<Game> q) { var l = q.ToList(); return l.Count == 0 ? 0 : l.Average(g => g.Rounds); }

        // Moyennes par carte : A est le joueur « SeatA », B l'autre.
        IReadOnlyList<SideCard> Cards(IReadOnlyList<DuelSlot> deck, bool isA)
        {
            var lines = ok.Where(g => g.Stats is not null)
                .Select(g => g.Stats!.Cards[isA ? g.SeatA : 1 - g.SeatA]).ToList();
            if (lines.Count == 0) return [];
            return deck.Select((slot, i) => new SideCard(slot,
                lines.Average(l => l[i].DamageDone), lines.Average(l => l[i].HealingDone), lines.Average(l => l[i].DamageTaken),
                lines.Average(l => l[i].Kills), lines.Average(l => l[i].Died ? 1.0 : 0.0))).ToList();
        }

        return new ConfrontationResult(deckA, deckB, s.Player, games.Length,
            ok.Count(g => g.Outcome > 0), ok.Count(g => g.Outcome < 0), ok.Count(g => g.Outcome == 0), games.Length - ok.Count,
            scoreA, first, second, Avg(ok), Avg(ok.Where(g => g.Outcome > 0)), Avg(ok.Where(g => g.Outcome < 0)),
            Cards(deckA, true), Cards(deckB, false), watch.Elapsed.TotalSeconds);
    }
}

/// <summary>Rédige une confrontation en français (texte lu par <c>RichReport</c>) : chaque phrase cite son chiffre mesuré.</summary>
public static class ConfrontationWriter
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>
    /// Le texte du résultat : notes, qui commence, durée. Le verdict (phrase et barre) et le tableau des cartes sont affichés à part par
    /// l'écran ; le nombre de duels n'est pas cité.
    /// </summary>
    public static string ToText(SimKnowledge kb, string nameA, string nameB, ComparisonResult c)
    {
        var r = c.Confrontation;
        var sb = new StringBuilder();
        string Pct(double v) => (100 * v).ToString("0", Fr) + " %";
        string Marg(Measured m) => "± " + (100 * m.Margin95).ToString("0", Fr) + " %";

        if (c.NoteA is { } na && c.NoteB is { } nb)
        {
            var a = DeckNote.From(na.Value, na.Margin95);
            var b = DeckNote.From(nb.Value, nb.Margin95);
            sb.AppendLine("NOTES CONTRE LA MÉTA");
            sb.AppendLine($"{nameA} : Note : {a.Text} — {a.Label}");
            sb.AppendLine($"{nameB} : Note : {b.Text} — {b.Label}");
            switch (r.Leader)
            {
                case 0 when b.Points > a.Points:
                    sb.AppendLine($"Face à face, {nameA} l'emporte, alors que {nameB} a la meilleure note contre la méta : les deux mesures ne disent pas la même chose.");
                    break;
                case 1 when a.Points > b.Points:
                    sb.AppendLine($"Face à face, {nameB} l'emporte, alors que {nameA} a la meilleure note contre la méta : les deux mesures ne disent pas la même chose.");
                    break;
            }
            sb.AppendLine();
        }

        sb.AppendLine("QUI COMMENCE");
        sb.AppendLine("Celui qui commence change une fois sur deux : chaque deck joue autant de fois en premier qu'en second.");
        sb.AppendLine($"• Quand {nameA} joue en premier : {nameA} gagne {Pct(r.ScoreAFirst.Value)} des duels ({Marg(r.ScoreAFirst)}).");
        sb.AppendLine($"• Quand {nameB} joue en premier : {nameA} gagne {Pct(r.ScoreASecond.Value)} des duels ({Marg(r.ScoreASecond)}).");
        sb.AppendLine();

        sb.AppendLine("DURÉE DES DUELS");
        sb.AppendLine($"• En moyenne {r.AverageRounds.ToString("0.0", Fr)} tours" +
                      (r.WinsA > 0 ? $" ; {r.AverageRoundsWinsA.ToString("0.0", Fr)} quand {nameA} gagne" : "") +
                      (r.WinsB > 0 ? $" ; {r.AverageRoundsWinsB.ToString("0.0", Fr)} quand {nameB} gagne" : "") + ".");
        return sb.ToString().TrimEnd();
    }

    /// <summary>La phrase du verdict : un avantage n'est annoncé que si la marge d'erreur n'atteint pas 50 %.</summary>
    public static string VerdictLine(string nameA, string nameB, ConfrontationResult r)
    {
        string Pct(double v) => (100 * v).ToString("0", Fr) + " %";
        switch (r.Leader)
        {
            case null:
                return $"Pas d'écart net : {nameA} marque {Pct(r.ScoreA.Value)} (± {(100 * r.ScoreA.Margin95).ToString("0", Fr)} %), la marge d'erreur contient 50 %. " +
                       "Les deux decks sont à égalité dans cette mesure.";
            case var l:
                var (win, lose) = l == 0 ? (nameA, nameB) : (nameB, nameA);
                var share = l == 0 ? r.ScoreA.Value : 1 - r.ScoreA.Value;
                var degree = share - 0.5 >= ConfrontationResult.ClearGap ? "nettement meilleur" : "meilleur";
                return $"{win} est {degree} que {lose} : il gagne {Pct(share)} des duels (± {(100 * r.ScoreA.Margin95).ToString("0", Fr)} %).";
        }
    }
}
