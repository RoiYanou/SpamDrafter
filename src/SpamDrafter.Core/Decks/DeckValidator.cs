using SpamDrafter.Core.Knowledge;

namespace SpamDrafter.Core.Decks;

public static class DeckValidator
{
    /// <summary>Liste des problèmes ; vide si le deck est valide. Un deck complet a 4 cartes.</summary>
    public static IReadOnlyList<string> Validate(Deck deck, KnowledgeBase kb, bool requireComplete = true)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(deck.Name)) problems.Add("Le deck n'a pas de nom.");
        if (deck.Slots.Count == 0) problems.Add("Le deck est vide.");
        if (deck.Slots.Count > Deck.MaxCards) problems.Add($"Un deck contient au plus {Deck.MaxCards} cartes.");
        if (requireComplete && deck.Slots.Count != Deck.MaxCards)
            problems.Add($"Un deck complet contient {Deck.MaxCards} cartes ({deck.Slots.Count} choisies).");

        foreach (var dup in deck.Slots.GroupBy(s => s.CardId).Where(g => g.Count() > 1))
            problems.Add($"Carte en double : {(kb.FindCard(dup.Key) is { } c ? kb.CardName(c) : dup.Key)}.");

        foreach (var s in deck.Slots)
        {
            var card = kb.FindCard(s.CardId);
            if (card is null) { problems.Add($"Carte inconnue : {s.CardId}."); continue; }
            if (s.TalentId is not null && !card.Talents.Contains(s.TalentId))
                problems.Add($"Le talent « {s.TalentId} » n'appartient pas à la carte {kb.CardName(card)}.");
        }
        return problems;
    }
}
