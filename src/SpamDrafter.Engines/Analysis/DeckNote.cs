namespace SpamDrafter.Engines.Analysis;

/// <summary>
/// Note sur 100 d'un deck (phase 12, lot B, D-072 puis D-074). Elle part du pourcentage de duels simulés gagnés contre la
/// méta (une égalité compte pour une demi-victoire), ramené à une échelle qui va vraiment de 0 à 100 :
/// <c>note = (victoires − 5 %) / (50 % − 5 %)</c>, bornée entre 0 et 100.
/// 100 = le niveau d'un deck de la méta : il gagne au moins la moitié de ses duels contre elle. C'est l'objectif réaliste :
/// la méta est faite des 20 meilleurs decks trouvés par le Cuisinier ; mesurés à neuf, les decks proposés pour une carte
/// gagnent environ 10 à 50 % de leurs duels contre elle (les plus faibles autour de 5 à 10 %). Une échelle qui irait jusqu'à
/// 70 % de victoires ne donnerait jamais plus de 60 à un bon deck.
/// C'est une échelle de présentation (🟡) : elle ne change pas le classement des decks, seulement la façon de le lire.
/// </summary>
public sealed record DeckNote(int Points, string Label, bool NeedsConfirmation)
{
    /// <summary>Pourcentage de victoires qui vaut 0 : le deck ne gagne presque jamais.</summary>
    public const double Floor = 0.05;
    /// <summary>Pourcentage de victoires qui vaut 100 : le niveau d'un deck de la méta (la moitié des duels gagnés contre elle).</summary>
    public const double Ceiling = 0.50;
    /// <summary>Marge d'erreur (à 95 %) au-delà de laquelle la note est signalée « à confirmer » : trop peu de duels.</summary>
    public const double ConfirmMargin = 0.15;

    public string Text => $"{Points} / 100";

    /// <summary>Note (0 à 100, non arrondie) d'un taux de victoires.</summary>
    public static double Scale(double score) => 100 * Math.Clamp((score - Floor) / (Ceiling - Floor), 0, 1);

    public static DeckNote From(double score, double margin95)
    {
        var points = (int)Math.Round(Scale(score), MidpointRounding.AwayFromZero);
        return new DeckNote(points, LabelFor(points), margin95 > ConfirmMargin);
    }

    /// <summary>Faible (moins de 35), Correct (35 à 54), Bon (55 à 74), Excellent (75 et plus).</summary>
    public static string LabelFor(int points) => points switch
    {
        >= 75 => "Excellent",
        >= 55 => "Bon",
        >= 35 => "Correct",
        _ => "Faible",
    };

    /// <summary>Phrase qui explique la note à un joueur qui découvre l'outil.</summary>
    public static string Explanation(int opponents, bool synthetic) => synthetic
        ? $"La note va de 0 à 100 : elle mesure les duels simulés gagnés contre {opponents} decks forts (la méta). " +
          "100 = au niveau d'un deck de la méta (il gagne la moitié de ses duels contre elle)."
        : "La note va de 0 à 100 : elle mesure les duels simulés gagnés contre des decks tirés au hasard.";
}
