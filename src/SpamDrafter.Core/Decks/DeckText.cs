using SpamDrafter.Core.Knowledge;
using SpamDrafter.Core.Text;

namespace SpamDrafter.Core.Decks;

/// <summary>Résultat de la lecture d'un deck collé : le deck, ou la raison pour laquelle il est illisible.</summary>
public sealed record DeckTextResult(Deck? Deck, IReadOnlyList<string> Problems)
{
    public bool Ok => Deck is not null && Problems.Count == 0;
}

/// <summary>
/// Deck en texte lisible, pour l'échanger (phase 12, lot A) :
/// <code>
/// SpamDrafter · Nom du deck
/// Oisillons de Midas — Imprudent
/// Panthère imaginaire — Aucun talent
/// </code>
/// </summary>
public static class DeckText
{
    public const string Header = "SpamDrafter · ";
    public const string NoTalent = "Aucun talent";
    private static readonly string[] Separators = { " — ", " – ", " | ", " - " };

    public static string Format(Deck deck, KnowledgeBase kb)
    {
        var lines = new List<string> { Header + deck.Name };
        foreach (var s in deck.Slots)
        {
            var card = kb.FindCard(s.CardId);
            var name = card is null ? s.CardId : kb.CardName(card);
            lines.Add($"{name} — {(s.TalentId is null ? NoTalent : kb.TalentName(s.TalentId))}");
        }
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Lit un deck collé. Les accents et la casse sont ignorés ; un talent absent vaut « aucun talent ».</summary>
    public static DeckTextResult Parse(string text, KnowledgeBase kb)
    {
        var problems = new List<string>();
        var deck = new Deck { GameVersion = kb.Manifest.GameVersion };
        var lines = (text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count > 0 && lines[0].StartsWith(Header.TrimEnd(), StringComparison.OrdinalIgnoreCase))
        {
            deck.Name = lines[0][Header.TrimEnd().Length..].TrimStart(' ', '·').Trim();
            lines.RemoveAt(0);
        }
        if (lines.Count == 0) { problems.Add("Le texte ne contient aucune carte."); return new(null, problems); }

        foreach (var line in lines)
        {
            string cardPart = line, talentPart = "";
            foreach (var sep in Separators)
            {
                var i = line.IndexOf(sep, StringComparison.Ordinal);
                if (i > 0) { cardPart = line[..i]; talentPart = line[(i + sep.Length)..]; break; }
            }
            var card = FindCard(kb, cardPart);
            if (card is null) { problems.Add($"Carte inconnue : « {cardPart.Trim()} »."); continue; }
            string? talentId = null;
            if (TextSearch.Normalize(talentPart) is { Length: > 0 } wanted && wanted != TextSearch.Normalize(NoTalent))
            {
                talentId = card.Talents.FirstOrDefault(t => TextSearch.Normalize(kb.TalentName(t)) == wanted);
                if (talentId is null)
                    problems.Add($"Talent inconnu pour {kb.CardName(card)} : « {talentPart.Trim()} ».");
            }
            deck.Slots.Add(new DeckSlot(card.Id, talentId));
        }
        if (deck.Slots.Count > Deck.MaxCards) problems.Add($"Un deck contient au plus {Deck.MaxCards} cartes ({deck.Slots.Count} lues).");
        foreach (var dup in deck.Slots.GroupBy(s => s.CardId).Where(g => g.Count() > 1))
            problems.Add($"Carte en double : {kb.CardName(kb.FindCard(dup.Key)!)}.");
        return new(problems.Count == 0 ? deck : null, problems);
    }

    private static Card? FindCard(KnowledgeBase kb, string name)
    {
        var key = TextSearch.Normalize(name);
        if (key.Length == 0) return null;
        return kb.Cards.FirstOrDefault(c => TextSearch.Normalize(kb.CardName(c)) == key);
    }
}
