using SpamDrafter.Core.Knowledge;

namespace SpamDrafter.Core.Decks;

public sealed record ElementCount(string ElementId, int Count);

/// <summary>Résistance moyenne de l'équipe à un élément (en %, 🃏 données des cartes).</summary>
public sealed record ElementResistance(string ElementId, double Average);

/// <summary>Une carte vulnérable à un élément : résistance négative (🃏).</summary>
public sealed record Weakness(string CardId, string ElementId, int Value);

/// <summary>
/// Compteurs d'un deck (phase 12, lot D) : ce qu'on lit directement dans les données des cartes, sans simulation, donc
/// instantané. Les résistances sont des pourcentages (une valeur négative est une faiblesse). Rien n'est interprété : le
/// jeu n'a pas de « coût » de carte (il y a un revenu en or) ; les coûts propres aux capacités ne sont pas comptés ici.
/// </summary>
public sealed record DeckStats(
    IReadOnlyList<ElementCount> Elements,
    int TotalHp,
    int TotalIncome,
    IReadOnlyList<ElementResistance> Resistances,
    IReadOnlyList<Weakness> Weaknesses)
{
    public static DeckStats Compute(IEnumerable<Card> cards, KnowledgeBase kb)
    {
        var list = cards.ToList();
        var elements = kb.Elements
            .Select(e => new ElementCount(e.Id, list.Count(c => c.Element == e.Id)))
            .Where(e => e.Count > 0).ToList();
        // Seuls les éléments pour lesquels les cartes ont une résistance (les 6 éléments de combat ; pas l'éther).
        var resisted = kb.Cards.SelectMany(c => c.Resistances.Keys).ToHashSet();
        var resistances = kb.Elements.Where(e => resisted.Contains(e.Id))
            .Select(e => new ElementResistance(e.Id, list.Count == 0 ? 0 : list.Average(c => c.Resistances.GetValueOrDefault(e.Id))))
            .ToList();
        var weaknesses = list
            .SelectMany(c => kb.Elements.Where(e => resisted.Contains(e.Id)).Select(e => new Weakness(c.Id, e.Id, c.Resistances.GetValueOrDefault(e.Id))))
            .Where(w => w.Value < 0)
            .OrderBy(w => w.Value).ThenBy(w => w.CardId, StringComparer.Ordinal).ToList();
        return new DeckStats(elements, list.Sum(c => c.Hp), list.Sum(c => c.GoldIncome), resistances, weaknesses);
    }
}
