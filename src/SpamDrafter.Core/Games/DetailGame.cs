using SpamDrafter.Core.Knowledge;

namespace SpamDrafter.Core.Games;

/// <summary>Une zone de l'illustration, en fractions de l'image (0 à 1) : coin haut-gauche, largeur et hauteur.</summary>
public readonly record struct DetailWindow(double X, double Y, double Width, double Height);

/// <summary>
/// « Détail du jour » : une zone très zoomée de l'illustration d'une carte, à retrouver (essais illimités, zoom fixe ; « Abandonner » donne la réponse).
/// La partie finie (gagnée ou abandonnée), l'illustration entière est montrée.
/// La carte et la zone sont les mêmes pour tous (tirage à partir de la date, D-095).
/// </summary>
public sealed class DetailGame
{
    public const string GameId = "detail";
    /// <summary>Grossissement de la zone montrée (la zone visible est 1 / grossissement de l'image).</summary>
    public const double Zoom = 8.0;

    public DetailGame(Card answer, double focusX, double focusY)
    {
        Answer = answer; FocusX = focusX; FocusY = focusY;
    }

    public Card Answer { get; }
    /// <summary>Centre de la zone (fractions de l'image).</summary>
    public double FocusX { get; }
    public double FocusY { get; }

    public bool IsCorrect(string cardId) => cardId == Answer.Id;

    /// <summary>Zone visible ; <paramref name="finished"/> : l'image entière.</summary>
    public DetailWindow WindowFor(bool finished)
    {
        if (finished) return new DetailWindow(0, 0, 1, 1);
        var size = 1.0 / Zoom;
        var x = Math.Clamp(FocusX - size / 2, 0, 1 - size);
        var y = Math.Clamp(FocusY - size / 2, 0, 1 - size);
        return new DetailWindow(x, y, size, size);
    }

    /// <summary>
    /// Le détail d'un jour. Les cartes sont classées par nom puis mélangées par « tour » (un tour = autant de jours que de cartes) :
    /// aucune carte ne revient avant la fin du tour. Le centre de la zone est tiré au milieu de l'image (de 25 % à 75 %).
    /// </summary>
    public static DetailGame? ForDay(KnowledgeBase kb, DateOnly day)
    {
        var cards = kb.Cards.Where(c => !string.IsNullOrEmpty(c.Image)).OrderBy(c => c.SanitizedName, StringComparer.Ordinal).ToList();
        if (cards.Count == 0) return null;
        var number = Math.Max(0, DailyClock.Number(day));
        var round = number / cards.Count;
        var order = Enumerable.Range(0, cards.Count).ToArray();
        var rng = new SeededRandom(SeededRandom.SeedOf("detail|" + round));
        for (var i = order.Length - 1; i > 0; i--) { var j = rng.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }
        var card = cards[order[number % cards.Count]];
        var zone = new SeededRandom(SeededRandom.SeedOf("detail-zone|" + number));
        var fx = 0.25 + 0.5 * zone.Next(1001) / 1000.0;
        var fy = 0.25 + 0.5 * zone.Next(1001) / 1000.0;
        return new DetailGame(card, fx, fy);
    }
}
