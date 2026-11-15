namespace SpamDrafter.Core.Decks;

/// <summary>Un deck trouvé par le Drafter.</summary>
public sealed record DeckMatch(Deck Deck, bool IsComplete);

public static class DeckSearch
{
    /// <summary>
    /// Decks sauvegardés qui contiennent TOUTES les cartes demandées (D-017), quelle que soit leur position.
    /// Les decks complets (4 cartes) passent avant les brouillons, puis tri par nom.
    /// </summary>
    /// <exception cref="ArgumentException">Plus de 4 cartes, ou carte en double (D-020).</exception>
    public static IReadOnlyList<DeckMatch> Find(IEnumerable<Deck> decks, IReadOnlyCollection<string> cardIds)
    {
        if (cardIds.Count > Deck.MaxCards)
            throw new ArgumentException($"Une recherche porte sur {Deck.MaxCards} cartes au plus.", nameof(cardIds));
        if (cardIds.Distinct().Count() != cardIds.Count)
            throw new ArgumentException("Une carte ne peut pas être demandée deux fois.", nameof(cardIds));
        if (cardIds.Count == 0) return Array.Empty<DeckMatch>();

        return decks
            .Where(d => cardIds.All(id => d.Slots.Any(s => s.CardId == id)))
            .Select(d => new DeckMatch(d, d.Slots.Count == Deck.MaxCards))
            .OrderByDescending(m => m.IsComplete)
            .ThenBy(m => m.Deck.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
